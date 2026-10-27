/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 * and on Uplay-Protobufs by UplayDB (MIT)
 * https://github.com/UplayDB/Uplay-Protobufs
 * It speaks the same socket protocol their client does, written again here. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>Ubisoft's socket service failed or refused: the connection dropped, a reply never came, or the client was turned away.</summary>
internal sealed class DemuxException : Exception
{
    public DemuxException(string message)
        : base(message)
    {
    }

    public DemuxException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// One connection to Ubisoft's Demux service: a socket carrying length-prefixed messages (four bytes, big
/// endian, then a protocol buffers message) in both directions. A request gets the response with its request
/// id; the launcher's services (ownership, achievements) are opened by name on the one connection and then
/// exchange their own messages inside data pushes.
/// </summary>
/// <remarks>
/// A background loop reads everything the server sends, so a response, a push of service data, and the
/// server's keep-alive and outdated-client notices are told apart as they arrive. Service calls are made one at
/// a time per connection: the replies carry no id the connection could use to tell two apart in flight.
/// </remarks>
internal sealed class DemuxClient : IAsyncDisposable
{
    private const int MaximumFrame = 16 * 1024 * 1024;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly SemaphoreSlim _calling = new(1, 1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<byte[]>> _responses = new();
    private readonly ConcurrentDictionary<uint, Channel<byte[]>> _services = new();
    private readonly CancellationTokenSource _stop = new();
    private Task _reader = Task.CompletedTask;
    private int _lastRequest;
    private Exception? _failure;

    private DemuxClient(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>False once the connection has dropped or been closed.</summary>
    public bool IsAlive => Volatile.Read(ref _failure) == null;

    /// <summary>Starts reading from a connected stream.</summary>
    /// <param name="stream">The connection, TLS already done.</param>
    public static DemuxClient Start(Stream stream)
    {
        DemuxClient client = new(stream);
        client._reader = Task.Run(client.ReadAsync);
        return client;
    }

    /// <summary>The first field with a number in a message, or default.</summary>
    public static ProtoField Field(System.Collections.Generic.IEnumerable<ProtoField> fields, int number) =>
        fields.FirstOrDefault(field => field.Number == number);

    /// <summary>Sends a request and returns the response message that has its id.</summary>
    /// <param name="build">Makes the <c>Req</c> message for the id the client chose; it must write the id as field 1.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<System.Collections.Generic.List<ProtoField>> RequestAsync(Func<uint, ProtoMessage> build, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        var id = (uint)Interlocked.Increment(ref _lastRequest);
        TaskCompletionSource<byte[]> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses[id] = response;
        try
        {
            ProtoMessage upstream = new();
            upstream.Message(1, build(id));
            await WriteAsync(upstream.ToArray(), cancellationToken).ConfigureAwait(false);
            return ProtoMessage.Read(await WaitAsync(response.Task, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _responses.TryRemove(id, out _);
        }
    }

    /// <summary>Tells the server which version of the protocol this client speaks.</summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    public Task PushVersionAsync(uint version, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        return SendPushAsync(new ProtoMessage().Message(4, new ProtoMessage().Varint(1, version)), cancellationToken);
    }

    /// <summary>Opens a service by name and returns the id of the connection it is reached by.</summary>
    /// <param name="service">The service's name: <c>ownership_service</c>, <c>ach_frontend</c>.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<uint> OpenServiceAsync(string service, CancellationToken cancellationToken)
    {
        var response = await RequestAsync(
            id => new ProtoMessage().Varint(1, id).Message(3, new ProtoMessage().Text(2, service)),
            cancellationToken).ConfigureAwait(false);

        var opened = ProtoMessage.Read(Field(response, 3).Bytes ?? Array.Empty<byte>());
        if (Field(opened, 2).Varint == 0)
        {
            throw new DemuxException($"Ubisoft would not open {service}.");
        }

        var connection = (uint)Field(opened, 1).Varint;
        _services[connection] = Channel.CreateUnbounded<byte[]>();
        return connection;
    }

    /// <summary>Sends a service's message and returns the message it answers with.</summary>
    /// <param name="connection">The connection <see cref="OpenServiceAsync"/> returned.</param>
    /// <param name="message">The service's message.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<byte[]> CallAsync(uint connection, byte[] message, CancellationToken cancellationToken)
    {
        ThrowIfFailed();
        if (_services.TryGetValue(connection, out var replies) == false)
        {
            throw new DemuxException("That service is not open.");
        }

        await _calling.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var framed = new byte[4 + message.Length];
            BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)message.Length);
            message.CopyTo(framed, 4);

            var data = new ProtoMessage().Varint(1, connection).Bytes(2, framed);
            await SendPushAsync(new ProtoMessage().Message(1, data), cancellationToken).ConfigureAwait(false);

            byte[] reply;
            try
            {
                reply = await WaitAsync(replies.Reader.ReadAsync(cancellationToken).AsTask(), cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException e)
            {
                throw e.InnerException as DemuxException ?? new DemuxException("Ubisoft closed the service.", e);
            }

            if (reply.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(reply) != reply.Length - 4)
            {
                throw new DemuxException("Ubisoft's service answered with something that is not a message.");
            }
            return reply.AsSpan(4).ToArray();
        }
        finally
        {
            _calling.Release();
        }
    }

    /// <summary>Closes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        Fail(new DemuxException("The connection was closed."));
        await _stop.CancelAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _reader.ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The reader ends when the stream does.
        }
        _stop.Dispose();
    }

    private async Task ReadAsync()
    {
        try
        {
            var header = new byte[4];
            while (_stop.IsCancellationRequested == false)
            {
                await _stream.ReadExactlyAsync(header, _stop.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (length == 0 || length > MaximumFrame)
                {
                    throw new DemuxException("Ubisoft sent a message that cannot be one.");
                }

                var frame = new byte[length];
                await _stream.ReadExactlyAsync(frame, _stop.Token).ConfigureAwait(false);
                await HandleAsync(frame).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Fail(e as DemuxException ?? new DemuxException("The connection to Ubisoft was lost.", e));
        }
    }

    private async Task HandleAsync(byte[] frame)
    {
        foreach (var field in ProtoMessage.Read(frame))
        {
            if (field.Number == 1 && field.Bytes != null)
            {
                var id = (uint)Field(ProtoMessage.Read(field.Bytes), 1).Varint;
                if (_responses.TryGetValue(id, out var response) == true)
                {
                    response.TrySetResult(field.Bytes);
                }
            }
            else if (field.Number == 2 && field.Bytes != null)
            {
                foreach (var push in ProtoMessage.Read(field.Bytes))
                {
                    await HandlePushAsync(push).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandlePushAsync(ProtoField push)
    {
        switch (push.Number)
        {
            case 1 when push.Bytes != null:
                var data = ProtoMessage.Read(push.Bytes);
                if (_services.TryGetValue((uint)Field(data, 1).Varint, out var replies) == true)
                {
                    replies.Writer.TryWrite(Field(data, 2).Bytes ?? Array.Empty<byte>());
                }
                break;

            case 2 when push.Bytes != null:
                var closing = ProtoMessage.Read(push.Bytes);
                var closed = (uint)Field(closing, 1).Varint;
                if (_services.TryRemove(closed, out var channel) == true)
                {
                    var reason = Field(closing, 2).Varint switch
                    {
                        1 => " (it was told to quit)",
                        2 => " (the account is signed in somewhere else)",
                        3 => " (the account is banned)",
                        4 => " (it was not signed in)",
                        _ => "",
                    };
                    channel.Writer.TryComplete(new DemuxException("Ubisoft closed the service" + reason + "."));
                }
                break;

            case 3:
                await SendPushAsync(new ProtoMessage().Message(3, new ProtoMessage()), _stop.Token).ConfigureAwait(false);
                break;

            case 5:
                Fail(new DemuxException("Ubisoft says this version of its protocol is out of date."));
                break;
        }
    }

    private Task SendPushAsync(ProtoMessage push, CancellationToken cancellationToken) =>
        WriteAsync(new ProtoMessage().Message(2, push).ToArray(), cancellationToken);

    private async Task WriteAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var framed = new byte[4 + message.Length];
            BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)message.Length);
            message.CopyTo(framed, 4);
            await _stream.WriteAsync(framed, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            var failure = new DemuxException("The connection to Ubisoft was lost.", e);
            Fail(failure);
            throw failure;
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException e)
        {
            throw new DemuxException("Ubisoft did not answer.", e);
        }
    }

    private void ThrowIfFailed()
    {
        var failure = Volatile.Read(ref _failure);
        if (failure != null)
        {
            throw failure;
        }
    }

    private void Fail(Exception failure)
    {
        if (Interlocked.CompareExchange(ref _failure, failure, null) != null)
        {
            return;
        }

        foreach (var response in _responses.Values)
        {
            response.TrySetException(failure);
        }
        foreach (var service in _services.Values)
        {
            service.Writer.TryComplete(failure);
        }
    }
}
