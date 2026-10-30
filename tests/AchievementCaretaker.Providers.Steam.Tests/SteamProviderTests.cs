/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Steam.Protocol;
using Grpc.Core;
using Xunit;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam.Tests;

public sealed class SteamProviderTests
{
    private sealed class FakeBackend : ISteamAppBackend
    {
        public List<KeyValuePair<string, bool>> Written { get; } = new();
        public List<KeyValuePair<string, double>> WrittenStats { get; } = new();
        public bool? ResetAchievements { get; private set; }
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }
            IReadOnlyList<StoreAchievement> list = new[]
            {
                new StoreAchievement("A1", "First", "Do it", "http://i/1", false, null, 12.5, true, null,
                    DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
                new StoreAchievement("A2", "Second", "", null, true, 10, null, false, 0.5, null, true),
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<StoreStat>> GetStatsAsync(CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }
            IReadOnlyList<StoreStat> list = new[]
            {
                new StoreStat("S1", "Games", StoreStatKind.Integer, 12, 0, 100, 5, true, 0, false),
                new StoreStat("S2", "Distance", StoreStatKind.Float, 3.5, -1.5, 9.25, 0, false, 1.5, true),
            };
            return Task.FromResult(list);
        }

        public Task CommitAsync(
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }
            Written.AddRange(achievements);
            WrittenStats.AddRange(statistics);
            return Task.CompletedTask;
        }

        public Task ResetAsync(bool includeAchievements, CancellationToken cancellationToken)
        {
            ResetAchievements = includeAchievements;
            return Task.CompletedTask;
        }

        public IReadOnlyCollection<LibraryCandidate>? Scanned { get; private set; }
        public bool ScanIsComplete { get; set; } = true;

        public Task<LibraryScan> ScanLibraryAsync(
            IReadOnlyCollection<LibraryCandidate> candidates, IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }
            Scanned = candidates;
            progress?.Report(new LibraryScanProgress(1, 2));
            progress?.Report(new LibraryScanProgress(2, 2));
            IReadOnlyList<StoreGame> games = new[]
            {
                new StoreGame(
                    "steam", "480", "480", "Spacewar", "https://img/480", "normal",
                    DateTimeOffset.FromUnixTimeSeconds(1_300_000_000), 93, 8, 120, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 5, 2),
                new StoreGame("steam", "7", "7", null, null, "demo"),
            };
            return Task.FromResult(new LibraryScan("12345", games, ScanIsComplete));
        }

        public Task<string> GetAccountAsync(CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }
            return Task.FromResult("12345");
        }

        public Task<StoreGame?> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken) =>
            Task.FromResult(candidate.Id == "480" ? new StoreGame("steam", "480", "480", "Spacewar", null, candidate.Type) : (StoreGame?)null);
    }

    /// <summary>Starts the real server in this process, as the process launcher would start it in another.</summary>
    private sealed class InProcessLauncher : ISteamWorkerLauncher
    {
        private readonly ISteamAppBackend _backend;
        public long LastAppId { get; private set; }
        public int StartCount { get; private set; }
        public bool FailNextStart { get; set; }
        public string? TokenOverride { get; set; }
        public string? ThumbprintOverride { get; set; }

        public InProcessLauncher(ISteamAppBackend backend) => _backend = backend;

        public async Task<ISteamWorker> StartAsync(long appId, CancellationToken cancellationToken)
        {
            if (FailNextStart == true)
            {
                FailNextStart = false;
                throw new ProviderException("worker_start", "no helper");
            }
            StartCount++;
            LastAppId = appId;
            var handshake = SteamWorkerHandshake.Create();
            var host = await SteamWorkerHost.StartAsync(_backend, handshake);
            return new Worker(host, new SteamWorkerEndpoint(
                host.Port, ThumbprintOverride ?? handshake.Thumbprint, TokenOverride ?? handshake.Token));
        }

        private sealed class Worker : ISteamWorker
        {
            private readonly SteamWorkerHost _host;
            public SteamWorkerEndpoint Endpoint { get; }
            public Worker(SteamWorkerHost host, SteamWorkerEndpoint endpoint)
            {
                _host = host;
                Endpoint = endpoint;
            }
            public ValueTask DisposeAsync() => _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Achievements_round_trip_over_tls_with_unknowns_kept_null()
    {
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));

        var list = await provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
        Assert.Equal("First", list[0].DisplayName);
        Assert.True(list[0].Unlocked);
        Assert.Equal(12.5, list[0].RarityPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), list[0].UnlockDate);
        Assert.Null(list[0].XP);
        Assert.Null(list[0].Progress);

        Assert.Null(list[1].IconUrl);
        Assert.Equal(10, list[1].XP);
        Assert.Equal(0.5, list[1].Progress);
        Assert.Null(list[1].UnlockDate);
        Assert.Null(list[1].RarityPercent);

        Assert.False(list[0].Protected);
        Assert.True(list[1].Protected);
    }

    [Fact]
    public async Task Statistics_round_trip_with_their_limits()
    {
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));

        var stats = await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);

        Assert.Equal(2, stats.Count);
        Assert.Equal(new StoreStat("S1", "Games", StoreStatKind.Integer, 12, 0, 100, 5, true, 0, false), stats[0]);
        Assert.Equal(new StoreStat("S2", "Distance", StoreStatKind.Float, 3.5, -1.5, 9.25, 0, false, 1.5, true), stats[1]);
    }

    [Fact]
    public async Task A_commit_carries_achievements_and_statistics_together()
    {
        var backend = new FakeBackend();
        var provider = new SteamProvider(new InProcessLauncher(backend));

        await provider.CommitAsync(
            "480",
            new[] { new KeyValuePair<string, bool>("A1", true) },
            new[] { new KeyValuePair<string, double>("S1", 40), new KeyValuePair<string, double>("S2", 2.25) },
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { new KeyValuePair<string, bool>("A1", true) }, backend.Written);
        Assert.Equal(
            new[] { new KeyValuePair<string, double>("S1", 40), new KeyValuePair<string, double>("S2", 2.25) },
            backend.WrittenStats);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reset_says_whether_achievements_go_too(bool achievementsToo)
    {
        var backend = new FakeBackend();

        await new SteamProvider(new InProcessLauncher(backend)).ResetAsync(
            "480", achievementsToo, TestContext.Current.CancellationToken);

        Assert.Equal(achievementsToo, backend.ResetAchievements);
    }

    [Fact]
    public async Task A_provider_error_keeps_its_code_across_the_wire()
    {
        var provider = new SteamProvider(new InProcessLauncher(
            new FakeBackend { Failure = new ProviderException("protected", "S2 is protected") }));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.CommitAsync(
            "480", Array.Empty<KeyValuePair<string, bool>>(), new[] { new KeyValuePair<string, double>("S2", 1) },
            TestContext.Current.CancellationToken));

        Assert.Equal("protected", error.ErrorCode);
        Assert.Equal("S2 is protected", error.Message);
    }

    [Fact]
    public async Task The_app_id_reaches_the_launcher()
    {
        var launcher = new InProcessLauncher(new FakeBackend());

        await new SteamProvider(launcher).GetAchievementsAsync("105600", "en", TestContext.Current.CancellationToken);

        Assert.Equal(105600, launcher.LastAppId);
    }

    [Fact]
    public async Task Changes_are_delivered_to_the_backend()
    {
        var backend = new FakeBackend();
        var provider = new SteamProvider(new InProcessLauncher(backend));

        await provider.SetAchievementsAsync(
            "480",
            new[] { new KeyValuePair<string, bool>("A1", false), new KeyValuePair<string, bool>("A2", true) },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { new KeyValuePair<string, bool>("A1", false), new KeyValuePair<string, bool>("A2", true) },
            backend.Written);
    }

    [Fact]
    public async Task A_wrong_token_is_refused()
    {
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()) { TokenOverride = "not-the-token" });

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken));

        Assert.Equal("worker_denied", error.ErrorCode);
    }

    [Fact]
    public async Task A_server_with_another_certificate_is_not_trusted()
    {
        var other = SteamWorkerHandshake.Create().Thumbprint;
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()) { ThumbprintOverride = other });

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_backend_failure_becomes_a_provider_error()
    {
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend { Failure = new InvalidOperationException("no steam") }));

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken));

        Assert.Equal("steam_error", error.ErrorCode);
    }

    [Fact]
    public async Task A_game_key_that_is_not_an_app_id_is_refused_before_anything_starts()
    {
        var launcher = new InProcessLauncher(new FakeBackend());

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => new SteamProvider(launcher).GetAchievementsAsync("not-a-number", "en", TestContext.Current.CancellationToken));

        Assert.Equal("bad_game", error.ErrorCode);
        Assert.Equal(0, launcher.LastAppId);
    }

    [Fact]
    public async Task A_library_scan_round_trips_its_games_details_and_progress()
    {
        var backend = new FakeBackend();
        await using var provider = new SteamProvider(new InProcessLauncher(backend));
        // Progress<T> posts each report to the thread pool, so two reports can arrive at once: a List would lose one.
        ConcurrentQueue<LibraryScanProgress> progress = new();

        var scan = await provider.ScanLibraryAsync(
            new[] { new LibraryCandidate("480", "normal"), new LibraryCandidate("7", "demo") },
            new Progress<LibraryScanProgress>(progress.Enqueue),
            TestContext.Current.CancellationToken);

        Assert.Equal("12345", scan.Account);
        Assert.True(scan.Complete);
        Assert.Equal(2, scan.Games.Count);
        Assert.Equal(
            new StoreGame(
                "steam", "480", "480", "Spacewar", "https://img/480", "normal",
                DateTimeOffset.FromUnixTimeSeconds(1_300_000_000), 93, 8, 120, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 5, 2),
            scan.Games[0]);

        // What the store does not know stays unknown: no name, no art, no numbers.
        var bare = scan.Games[1];
        Assert.Null(bare.Title);
        Assert.Null(bare.ImageUrl);
        Assert.Equal("demo", bare.Type);
        Assert.Null(bare.ReleaseDate);
        Assert.Null(bare.PlaytimeMinutes);
        Assert.Null(bare.AchievementsTotal);

        Assert.Equal(new[] { "480", "7" }, backend.Scanned!.Select(c => c.Id));

        // Progress<T> delivers on the thread pool, a moment after the call that reported.
        for (var wait = 0; wait < 100 && progress.Contains(new LibraryScanProgress(2, 2)) == false; wait++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Contains(new LibraryScanProgress(2, 2), progress);
    }

    [Fact]
    public async Task A_scan_cut_short_says_it_was_not_complete()
    {
        await using var provider = new SteamProvider(new InProcessLauncher(new FakeBackend { ScanIsComplete = false }));

        var scan = await provider.ScanLibraryAsync(
            new[] { new LibraryCandidate("480", "normal") }, null, TestContext.Current.CancellationToken);

        Assert.False(scan.Complete);
    }

    [Fact]
    public async Task A_scan_always_starts_a_helper_of_its_own()
    {
        var launcher = new InProcessLauncher(new FakeBackend());
        await using var provider = new SteamProvider(launcher);
        var token = TestContext.Current.CancellationToken;

        await provider.ScanLibraryAsync(new[] { new LibraryCandidate("480", "normal") }, null, token);
        await provider.ScanLibraryAsync(new[] { new LibraryCandidate("480", "normal") }, null, token);

        Assert.Equal(2, launcher.StartCount);
        Assert.Equal(0, launcher.LastAppId);
    }

    [Fact]
    public async Task A_scan_failure_keeps_its_code_across_the_wire()
    {
        await using var provider = new SteamProvider(new InProcessLauncher(
            new FakeBackend { Failure = new ProviderException("steam_gone", "Steam went away") }));

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.ScanLibraryAsync(
            new[] { new LibraryCandidate("480", "normal") }, null, TestContext.Current.CancellationToken));

        Assert.Equal("steam_gone", error.ErrorCode);
    }

    [Fact]
    public async Task An_owned_game_is_found_and_one_that_is_not_is_null()
    {
        await using var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));
        var token = TestContext.Current.CancellationToken;

        var owned = await provider.FindOwnedAsync(new LibraryCandidate("480", "normal"), token);
        var other = await provider.FindOwnedAsync(new LibraryCandidate("999", "normal"), token);

        Assert.Equal("Spacewar", owned!.Title);
        Assert.Null(other);
    }

    [Fact]
    public async Task The_account_comes_back_and_a_failure_keeps_its_reason()
    {
        await using var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));
        Assert.Equal("12345", await provider.GetAccountAsync(TestContext.Current.CancellationToken));

        await using var broken = new SteamProvider(new InProcessLauncher(new FakeBackend { Failure = new ProviderException("steam_gone", "Steam is not running.") }));
        var error = await Assert.ThrowsAsync<ProviderException>(() => broken.GetAccountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Steam is not running.", error.Message);
    }

    [Fact]
    public async Task Listing_through_the_plain_interface_is_not_supported()
    {
        await using var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetLibraryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task One_editing_session_shares_one_helper()
    {
        var launcher = new InProcessLauncher(new FakeBackend());
        await using var provider = new SteamProvider(launcher);
        var token = TestContext.Current.CancellationToken;

        await provider.GetAchievementsAsync("480", "en", token);
        await provider.GetStatsAsync("480", token);
        await provider.CommitAsync("480", Array.Empty<KeyValuePair<string, bool>>(), Array.Empty<KeyValuePair<string, double>>(), token);
        await provider.ResetAsync("480", false, token);

        Assert.Equal(1, launcher.StartCount);
    }

    [Fact]
    public async Task A_reload_starts_a_fresh_helper()
    {
        var launcher = new InProcessLauncher(new FakeBackend());
        await using var provider = new SteamProvider(launcher);

        await provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken);
        await provider.GetAchievementsAsync("480", "en", TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.StartCount);
    }

    [Fact]
    public async Task Different_games_get_different_helpers()
    {
        var launcher = new InProcessLauncher(new FakeBackend());
        await using var provider = new SteamProvider(launcher);

        await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);
        await provider.GetStatsAsync("105600", TestContext.Current.CancellationToken);
        await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.StartCount);
    }

    [Fact]
    public async Task An_idle_helper_is_let_go_and_the_next_call_starts_another()
    {
        var launcher = new InProcessLauncher(new FakeBackend());
        await using var provider = new SteamProvider(launcher, TimeSpan.FromMilliseconds(100));

        await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.StartCount);
    }

    [Fact]
    public async Task A_failed_start_is_not_remembered()
    {
        var launcher = new InProcessLauncher(new FakeBackend()) { FailNextStart = true };
        await using var provider = new SteamProvider(launcher);

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.GetStatsAsync("480", TestContext.Current.CancellationToken));
        var stats = await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);

        Assert.Equal(2, stats.Count);
    }

    [Fact]
    public async Task A_disposed_provider_refuses_calls()
    {
        var provider = new SteamProvider(new InProcessLauncher(new FakeBackend()));
        await provider.GetStatsAsync("480", TestContext.Current.CancellationToken);

        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.GetStatsAsync("480", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Steam_can_write()
    {
        Assert.IsAssignableFrom<IWritableStoreProvider>(new SteamProvider(new InProcessLauncher(new FakeBackend())));
    }
}
