/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Steam.Protocol;
using Grpc.Core;
using Xunit;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>The helper's standard-input protocol, with pipes standing in for the process's streams.</summary>
public sealed class SteamWorkerHostTests
{
    private sealed class OneAchievementBackend : ISteamAppBackend
    {
        public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<StoreAchievement> list = new[]
            {
                new StoreAchievement("A", "Name", "", null, false, null, null, true, null, null),
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<StoreStat>> GetStatsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoreStat>>(Array.Empty<StoreStat>());

        public Task CommitAsync(
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResetAsync(bool includeAchievements, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LibraryScan> ScanLibraryAsync(
            IReadOnlyCollection<LibraryCandidate> candidates, IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new LibraryScan("42", Array.Empty<StoreGame>(), true));

        public Task<string> GetAccountAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoreGame?> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Pair : IDisposable
    {
        private readonly AnonymousPipeServerStream _toHelper = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromHelper = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _helperIn;
        private readonly AnonymousPipeClientStream _helperOut;

        public StreamWriter Send { get; }
        public StreamReader Receive { get; }
        public StreamReader HelperInput { get; }
        public StreamWriter HelperOutput { get; }

        public Pair()
        {
            _helperIn = new AnonymousPipeClientStream(PipeDirection.In, _toHelper.ClientSafePipeHandle);
            _helperOut = new AnonymousPipeClientStream(PipeDirection.Out, _fromHelper.ClientSafePipeHandle);
            Send = new StreamWriter(_toHelper) { AutoFlush = true };
            Receive = new StreamReader(_fromHelper);
            HelperInput = new StreamReader(_helperIn);
            HelperOutput = new StreamWriter(_helperOut) { AutoFlush = true };
        }

        public void Dispose()
        {
            Send.Dispose();
            Receive.Dispose();
            HelperInput.Dispose();
            HelperOutput.Dispose();
        }
    }

    [Fact]
    public async Task The_helper_reads_its_handshake_announces_its_port_and_leaves_when_input_closes()
    {
        using Pair pipes = new();
        var handshake = SteamWorkerHandshake.Create();
        var run = SteamWorkerHost.RunAsync(new OneAchievementBackend(), pipes.HelperInput, pipes.HelperOutput);

        await pipes.Send.WriteLineAsync(handshake.ToLine());
        var ready = await pipes.Receive.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("READY ", ready);
        var port = int.Parse(ready.AsSpan(6));

        using SteamWorkerChannel channel = new(new SteamWorkerEndpoint(port, handshake.CertificateHash, handshake.Token));
        var reply = await channel.Client.GetAchievementsAsync(
            new GetAchievementsRequest(), channel.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("A", Assert.Single(reply.Achievements).Id);

        // The application going away (or letting go) is how the helper learns to leave.
        pipes.Send.Close();
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_streaming_call_needs_the_token_too(bool rightToken)
    {
        using Pair pipes = new();
        var handshake = SteamWorkerHandshake.Create();
        var run = SteamWorkerHost.RunAsync(new OneAchievementBackend(), pipes.HelperInput, pipes.HelperOutput);

        await pipes.Send.WriteLineAsync(handshake.ToLine());
        var port = int.Parse((await pipes.Receive.ReadLineAsync(TestContext.Current.CancellationToken)).AsSpan(6));

        var token = rightToken == true ? handshake.Token : "wrong";
        using (SteamWorkerChannel channel = new(new SteamWorkerEndpoint(port, handshake.CertificateHash, token)))
        {
            using var call = channel.Client.ScanLibrary(
                new ScanLibraryRequest(), channel.Headers, cancellationToken: TestContext.Current.CancellationToken);

            if (rightToken == true)
            {
                Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
            }
            else
            {
                var refused = await Assert.ThrowsAsync<RpcException>(
                    () => call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
                Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);
            }
        }

        pipes.Send.Close();
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void The_handshake_does_not_print_its_key_or_its_token()
    {
        var handshake = SteamWorkerHandshake.Create();

        var printed = handshake.ToString();

        Assert.DoesNotContain(handshake.Token, printed);
        Assert.DoesNotContain(handshake.PfxBase64[..32], printed);
    }

    [Fact]
    public void The_helper_is_recognised_by_the_sha256_of_its_certificate()
    {
        var handshake = SteamWorkerHandshake.Create();
        using var certificate = handshake.LoadCertificate();

        Assert.Equal(Convert.ToHexString(certificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)), handshake.CertificateHash);
    }

    [Fact]
    public async Task A_shutdown_call_makes_the_helper_leave()
    {
        using Pair pipes = new();
        var handshake = SteamWorkerHandshake.Create();
        var run = SteamWorkerHost.RunAsync(new OneAchievementBackend(), pipes.HelperInput, pipes.HelperOutput);

        await pipes.Send.WriteLineAsync(handshake.ToLine());
        var port = int.Parse((await pipes.Receive.ReadLineAsync(TestContext.Current.CancellationToken)).AsSpan(6));

        using SteamWorkerChannel channel = new(new SteamWorkerEndpoint(port, handshake.CertificateHash, handshake.Token));
        await channel.Client.ShutdownAsync(
            new ShutdownRequest(), channel.Headers, cancellationToken: TestContext.Current.CancellationToken);

        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
