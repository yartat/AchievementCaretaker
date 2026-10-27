/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Ubisoft;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>A game the fake ownership service lists.</summary>
internal sealed record FakeProduct(uint Id, uint Type = 0, bool Owned = true, string? Configuration = null);

/// <summary>
/// Ubisoft's Demux service, played over a loopback socket in the real framing (four bytes, big endian, then a
/// protocol buffers message), with just enough of the protocol: the version, the sign-in by ticket, opening a
/// service, the ownership list, a product's block and the achievements. It writes the messages with its own
/// little encoder, so a test checks the provider against the protocol and not against itself.
/// </summary>
internal sealed class FakeDemuxServer : IUbisoftConnector, IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;

    public FakeDemuxServer()
    {
        _listener.Start();
        _accepting = Task.Run(AcceptAsync);
    }

    public string AcceptedTicket { get; set; } = "ticket";

    public List<FakeProduct> Products { get; } = new();

    /// <summary>Block of YAML by product id, for the products whose block is not in the list.</summary>
    public Dictionary<uint, string> Blocks { get; } = new();

    /// <summary>Earned achievements by the key the service knows a game by: (id, time).</summary>
    public Dictionary<string, (uint Id, ulong Time)[]> Earned { get; } = new();

    public int Connections { get; private set; }

    public int OwnershipCalls { get; private set; }

    public int AchievementCalls { get; private set; }

    public int WriteCalls { get; private set; }

    /// <summary>The achievements the last write carried: (id, time).</summary>
    public List<(uint Id, ulong Time)> LastWrite { get; } = new();

    /// <summary>Answers a write as the service does, but keeps nothing.</summary>
    public bool IgnoreWrites { get; set; }

    public string? LastAchievementUser { get; private set; }

    public string? LastAchievementProduct { get; private set; }

    public string? LastAchievementPlatform { get; private set; }

    public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port, cancellationToken);
        return client.GetStream();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The listener ends the accept loop.
        }
    }

    private async Task AcceptAsync()
    {
        while (_stop.IsCancellationRequested == false)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            Connections++;
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var header = new byte[4];
            try
            {
                while (true)
                {
                    await stream.ReadExactlyAsync(header, _stop.Token);
                    var frame = new byte[(header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]];
                    await stream.ReadExactlyAsync(frame, _stop.Token);

                    var answer = Handle(frame);
                    if (answer != null)
                    {
                        await Send(stream, answer);
                    }
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away.
            }
        }
    }

    private byte[]? Handle(byte[] upstream)
    {
        foreach (var (number, value) in Parse(upstream))
        {
            if (number == 1 && value is byte[] request)
            {
                return HandleRequest(Parse(request));
            }
            if (number == 2 && value is byte[] push)
            {
                var data = Parse(push).FirstOrDefault(f => f.Number == 1).Value as byte[];
                if (data != null)
                {
                    return HandleData(Parse(data));
                }
            }
        }
        return null;
    }

    private byte[] HandleRequest(List<(int Number, object Value)> request)
    {
        var id = (ulong)request.First(f => f.Number == 1).Value;
        var response = Varint(1, id);

        foreach (var (number, value) in request)
        {
            switch (number)
            {
                case 5:
                    response = response.Concat(Bytes(5, Varint(1, 1).Concat(Bytes(2, Encoding.UTF8.GetBytes("DEFAULT"))).Concat(Varint(5, 13393)).ToArray())).ToArray();
                    break;

                case 2:
                    var token = Parse((byte[])Parse((byte[])value).First(f => f.Number == 1).Value);
                    var ticket = Encoding.UTF8.GetString((byte[])token.First(f => f.Number == 3).Value);
                    response = response.Concat(Bytes(2, Varint(1, ticket == AcceptedTicket ? 1UL : 0UL))).ToArray();
                    break;

                case 3:
                    var name = Encoding.UTF8.GetString((byte[])Parse((byte[])value).First(f => f.Number == 2).Value);
                    response = response.Concat(Bytes(3, Varint(1, name == "ownership_service" ? 1UL : 2UL).Concat(Varint(2, 1)).ToArray())).ToArray();
                    break;
            }
        }
        return Bytes(1, response);
    }

    private byte[]? HandleData(List<(int Number, object Value)> data)
    {
        var connection = (ulong)data.First(f => f.Number == 1).Value;
        var inner = ((byte[])data.First(f => f.Number == 2).Value)[4..];

        byte[] reply;
        if (connection == 1)
        {
            OwnershipCalls++;
            // Upstream { request { id, initialize_req | get_product_config_req, ubi_ticket, ubi_session_id } }
            var request = Parse((byte[])Parse(inner).First(f => f.Number == 1).Value);
            var id = (ulong)request.First(f => f.Number == 1).Value;
            var response = Varint(1, id);
            if (request.Any(f => f.Number == 2))
            {
                var games = Products.SelectMany(product =>
                {
                    var game = Varint(1, product.Id).Concat(Varint(8, product.Type)).Concat(Varint(13, 3)).Concat(Varint(20, product.Owned ? 1UL : 0UL));
                    if (product.Configuration != null)
                    {
                        game = game.Concat(Bytes(17, Encoding.UTF8.GetBytes(product.Configuration)));
                    }
                    return Bytes(1, game.ToArray());
                }).ToArray();
                response = response.Concat(Bytes(2, Varint(1, 1).Concat(Bytes(2, games)).ToArray())).ToArray();
            }
            else if (request.FirstOrDefault(f => f.Number == 9).Value is byte[] config)
            {
                var product = (uint)(ulong)Parse(config).First(f => f.Number == 1).Value;
                var found = Blocks.TryGetValue(product, out var yaml);
                response = response.Concat(Bytes(9, Varint(1, found ? 1UL : 2UL).Concat(found ? Bytes(3, Encoding.UTF8.GetBytes(yaml!)) : Array.Empty<byte>()).ToArray())).ToArray();
            }
            reply = Bytes(1, response);
        }
        else
        {
            AchievementCalls++;
            // Req { id, read_achievements_req { user_id, product { product_id, platform_id } } }
            var request = Parse(inner);
            var id = (ulong)request.First(f => f.Number == 1).Value;
            if (request.FirstOrDefault(f => f.Number == 4).Value is byte[] write)
            {
                // write_achievements_req { achievement_blob { product_achievements { product, achievements { achievements* } } } }
                WriteCalls++;
                var writtenProduct = Parse((byte[])Parse((byte[])Parse(write).First(f => f.Number == 1).Value).First(f => f.Number == 1).Value);
                var key = Encoding.UTF8.GetString((byte[])Parse((byte[])writtenProduct.First(f => f.Number == 1).Value).First(f => f.Number == 1).Value);
                var writtenEntries = Parse((byte[])writtenProduct.First(f => f.Number == 2).Value)
                    .Where(f => f.Number == 1)
                    .Select(f => Parse((byte[])f.Value))
                    .Select(e => ((uint)(ulong)e.First(x => x.Number == 1).Value, (ulong)e.First(x => x.Number == 2).Value))
                    .ToArray();
                LastWrite.Clear();
                LastWrite.AddRange(writtenEntries);
                if (IgnoreWrites == false)
                {
                    Earned[key] = writtenEntries;
                }

                var written = Varint(1, id).Concat(Bytes(4, Array.Empty<byte>())).ToArray();
                var framedWrite = new byte[] { (byte)(written.Length >> 24), (byte)(written.Length >> 16), (byte)(written.Length >> 8), (byte)written.Length }.Concat(written).ToArray();
                return Bytes(2, Bytes(1, Varint(1, connection).Concat(Bytes(2, framedWrite)).ToArray()));
            }
            var read = Parse((byte[])request.First(f => f.Number == 3).Value);
            LastAchievementUser = Encoding.UTF8.GetString((byte[])read.First(f => f.Number == 1).Value);
            var product = Parse((byte[])read.First(f => f.Number == 2).Value);
            LastAchievementProduct = Encoding.UTF8.GetString((byte[])product.First(f => f.Number == 1).Value);
            LastAchievementPlatform = Encoding.UTF8.GetString((byte[])product.First(f => f.Number == 2).Value);

            Earned.TryGetValue(LastAchievementProduct, out var earned);
            var entries = (earned ?? Array.Empty<(uint, ulong)>())
                .SelectMany(e => Bytes(1, Varint(1, e.Id).Concat(Varint(2, e.Time)).ToArray()))
                .ToArray();
            var forProduct = Bytes(1, Bytes(1, Encoding.UTF8.GetBytes(LastAchievementProduct)).Concat(Bytes(2, entries)).ToArray());
            var blob = Bytes(2, forProduct);
            reply = Varint(1, id).Concat(Bytes(3, Bytes(1, Encoding.UTF8.GetBytes(LastAchievementUser)).Concat(blob).ToArray())).ToArray();
        }

        var framed = new byte[] { (byte)(reply.Length >> 24), (byte)(reply.Length >> 16), (byte)(reply.Length >> 8), (byte)reply.Length }.Concat(reply).ToArray();
        return Bytes(2, Bytes(1, Varint(1, connection).Concat(Bytes(2, framed)).ToArray()));
    }

    private static async Task Send(Stream stream, byte[] message)
    {
        var framed = new byte[] { (byte)(message.Length >> 24), (byte)(message.Length >> 16), (byte)(message.Length >> 8), (byte)message.Length }
            .Concat(message).ToArray();
        await stream.WriteAsync(framed);
        await stream.FlushAsync();
    }

    #region Protocol buffers, as little as this needs

    private static byte[] Varint(int number, ulong value) => Encode(((ulong)number << 3) | 0).Concat(Encode(value)).ToArray();

    private static byte[] Bytes(int number, byte[] value) => Encode(((ulong)number << 3) | 2).Concat(Encode((ulong)value.Length)).Concat(value).ToArray();

    private static byte[] Encode(ulong value)
    {
        List<byte> bytes = new();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static List<(int Number, object Value)> Parse(byte[] message)
    {
        List<(int, object)> fields = new();
        var position = 0;
        while (position < message.Length)
        {
            var tag = Decode(message, ref position);
            if ((tag & 7) == 0)
            {
                fields.Add(((int)(tag >> 3), Decode(message, ref position)));
            }
            else
            {
                var length = (int)Decode(message, ref position);
                fields.Add(((int)(tag >> 3), message[position..(position + length)]));
                position += length;
            }
        }
        return fields;
    }

    private static ulong Decode(byte[] message, ref int position)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var current = message[position++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }
            shift += 7;
        }
    }

    #endregion
}
