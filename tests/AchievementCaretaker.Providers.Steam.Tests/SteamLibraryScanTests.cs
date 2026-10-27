/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;
using AchievementCaretaker.SteamApi;
using Xunit;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>The helper's library session: which of the published games the account owns, and what Steam says about them.</summary>
public sealed class SteamLibraryScanTests
{
    private sealed class FakeLibraryGateway : ISteamAppGateway
    {
        public long AppId => 0;
        public string Language => "english";
        public ulong SteamId => 76561197960265728UL + 12345;
        public HashSet<uint> Owned { get; } = new() { 480, 105600 };
        public Dictionary<(uint, string), string> AppData { get; } = new();
        public Dictionary<uint, GameStats> Cached { get; } = new();
        public bool SteamStillRunning { get; set; } = true;
        public List<uint> Asked { get; } = new();

        public bool IsSubscribed(uint appId)
        {
            Asked.Add(appId);
            return Owned.Contains(appId);
        }

        public string? GetAppData(uint appId, string key) => AppData.TryGetValue((appId, key), out var value) ? value : null;

        public GameStats? GetCachedStats(uint appId) => Cached.TryGetValue(appId, out var stats) ? stats : null;

        public bool IsSteamStillRunning() => SteamStillRunning;

        // The achievement session is not what these tests are about.
        public Task EnsureUserStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public KeyValue? LoadSchema() => throw new NotSupportedException();
        public bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime) => throw new NotSupportedException();
        public bool SetAchievement(string id, bool unlocked) => throw new NotSupportedException();
        public bool TryGetIntStat(string id, out int value) => throw new NotSupportedException();
        public bool TryGetFloatStat(string id, out float value) => throw new NotSupportedException();
        public bool SetIntStat(string id, int value) => throw new NotSupportedException();
        public bool SetFloatStat(string id, float value) => throw new NotSupportedException();
        public bool ResetAllStats(bool includeAchievements) => throw new NotSupportedException();
        public bool StoreStats() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class SynchronousProgress : IProgress<LibraryScanProgress>
    {
        private readonly Action<LibraryScanProgress> _report;

        public SynchronousProgress(Action<LibraryScanProgress> report) => _report = report;

        public void Report(LibraryScanProgress value) => _report(value);
    }

    private readonly FakeLibraryGateway _gateway = new();

    private SteamAppBackend Backend() => new(_gateway);

    private static List<LibraryCandidate> Candidates(params uint[] ids) =>
        ids.Select(id => new LibraryCandidate(id.ToString(), "normal")).ToList();

    [Fact]
    public async Task A_scan_keeps_only_the_candidates_the_account_owns()
    {
        _gateway.AppData[(480, "name")] = "Spacewar";
        _gateway.AppData[(105600, "name")] = "Terraria";

        var scan = await Backend().ScanLibraryAsync(Candidates(480, 1, 105600, 2), null, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Spacewar", "Terraria" }, scan.Games.Select(g => g.Title));
        Assert.Equal("12345", scan.Account);
        Assert.True(scan.Complete);
        Assert.All(scan.Games, g => Assert.Equal("steam", g.Provider));
    }

    [Fact]
    public async Task A_candidate_listed_twice_is_asked_about_once_and_found_once()
    {
        var scan = await Backend().ScanLibraryAsync(Candidates(480, 480, 480), null, TestContext.Current.CancellationToken);

        Assert.Single(scan.Games);
        Assert.Single(_gateway.Asked);
    }

    [Fact]
    public async Task An_id_that_is_not_a_number_is_skipped()
    {
        var scan = await Backend().ScanLibraryAsync(
            new[] { new LibraryCandidate("nope", "normal"), new LibraryCandidate("480", "normal") },
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("480", Assert.Single(scan.Games).Id);
    }

    [Fact]
    public async Task What_steam_knows_about_a_game_is_carried_and_what_it_does_not_is_null()
    {
        _gateway.AppData[(480, "name")] = "Spacewar";
        _gateway.AppData[(480, "review_percentage")] = "93";
        _gateway.AppData[(480, "review_score")] = "8";
        _gateway.AppData[(480, "steam_release_date")] = "1300000000";
        var played = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        _gateway.Cached[480] = new GameStats(120, 5, 2, played);

        var scan = await Backend().ScanLibraryAsync(Candidates(480, 105600), null, TestContext.Current.CancellationToken);

        var known = scan.Games.Single(g => g.Id == "480");
        Assert.Equal(93, known.RatingPercent);
        Assert.Equal(8, known.RatingScore);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_300_000_000), known.ReleaseDate);
        Assert.Equal(120, known.PlaytimeMinutes);
        Assert.Equal(new DateTimeOffset(played), known.LastPlayed);
        Assert.Equal(5, known.AchievementsTotal);
        Assert.Equal(2, known.AchievementsEarned);

        var bare = scan.Games.Single(g => g.Id == "105600");
        Assert.Null(bare.Title);
        Assert.Null(bare.RatingPercent);
        Assert.Null(bare.ReleaseDate);
        Assert.Null(bare.PlaytimeMinutes);
        Assert.Null(bare.LastPlayed);
        Assert.Null(bare.AchievementsTotal);
    }

    [Fact]
    public async Task A_release_date_of_zero_is_unknown_not_1970()
    {
        _gateway.AppData[(480, "steam_release_date")] = "0";

        var game = Assert.Single((await Backend().ScanLibraryAsync(Candidates(480), null, TestContext.Current.CancellationToken)).Games);

        Assert.Null(game.ReleaseDate);
    }

    [Fact]
    public async Task A_cache_that_only_knows_playtime_leaves_the_achievement_counts_unknown()
    {
        _gateway.Cached[480] = new GameStats(90, -1, -1, null);

        var game = Assert.Single((await Backend().ScanLibraryAsync(Candidates(480), null, TestContext.Current.CancellationToken)).Games);

        Assert.Equal(90, game.PlaytimeMinutes);
        Assert.Null(game.AchievementsTotal);
        Assert.Null(game.AchievementsEarned);
        Assert.Null(game.LastPlayed);
    }

    [Fact]
    public async Task The_game_type_comes_from_the_published_list_and_defaults_to_normal()
    {
        var scan = await Backend().ScanLibraryAsync(
            new[] { new LibraryCandidate("480", "demo"), new LibraryCandidate("105600", "") },
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("demo", scan.Games.Single(g => g.Id == "480").Type);
        Assert.Equal("normal", scan.Games.Single(g => g.Id == "105600").Type);
    }

    [Fact]
    public async Task The_capsule_is_looked_for_in_the_games_language_then_english_then_the_old_logo()
    {
        // The fake's language is English, so the language-specific key is the English one too.
        _gateway.AppData[(480, "small_capsule/english")] = "cap.jpg";
        _gateway.AppData[(105600, "logo")] = "abc123";

        var scan = await Backend().ScanLibraryAsync(Candidates(480, 105600), null, TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/480/cap.jpg",
            scan.Games.Single(g => g.Id == "480").ImageUrl);
        Assert.Equal(
            "https://cdn.steamstatic.com/steamcommunity/public/images/apps/105600/abc123.jpg",
            scan.Games.Single(g => g.Id == "105600").ImageUrl);
    }

    [Fact]
    public async Task A_game_with_no_art_at_all_has_no_image()
    {
        var game = Assert.Single((await Backend().ScanLibraryAsync(Candidates(480), null, TestContext.Current.CancellationToken)).Games);

        Assert.Null(game.ImageUrl);
    }

    [Fact]
    public async Task A_scan_that_ended_after_steam_had_gone_is_not_complete()
    {
        _gateway.SteamStillRunning = false;

        var scan = await Backend().ScanLibraryAsync(Candidates(480), null, TestContext.Current.CancellationToken);

        Assert.False(scan.Complete);
    }

    [Fact]
    public async Task Progress_is_reported_and_ends_at_the_total()
    {
        List<LibraryScanProgress> seen = new();

        await Backend().ScanLibraryAsync(
            Candidates(Enumerable.Range(1, 1000).Select(i => (uint)i).ToArray()),
            new SynchronousProgress(seen.Add),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(seen);
        Assert.Equal(new LibraryScanProgress(1000, 1000), seen[^1]);
        Assert.True(seen.Count < 1000, "a report per candidate would flood the stream");
    }

    [Fact]
    public async Task A_cancelled_scan_stops()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Backend().ScanLibraryAsync(Candidates(480), null, cancel.Token));
    }

    [Fact]
    public async Task An_owned_game_is_described_and_one_that_is_not_is_null()
    {
        _gateway.AppData[(480, "name")] = "Spacewar";
        var backend = Backend();

        var owned = await backend.FindOwnedAsync(new LibraryCandidate("480", "normal"), TestContext.Current.CancellationToken);
        var other = await backend.FindOwnedAsync(new LibraryCandidate("999", "normal"), TestContext.Current.CancellationToken);

        Assert.Equal("Spacewar", owned!.Title);
        Assert.Null(other);
    }
}
