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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.SteamApi;
using Xunit;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>Builds the binary KeyValues form of Steam's schema file, which the reader parses.</summary>
internal static class Kv
{
    public static byte[] Node(string name, params byte[][] children) =>
        Concat(new byte[] { 0 }, Text(name), Concat(children), new byte[] { 8 });

    public static byte[] Str(string name, string value) => Concat(new byte[] { 1 }, Text(name), Text(value));

    public static byte[] Int(string name, int value) => Concat(new byte[] { 2 }, Text(name), BitConverter.GetBytes(value));

    public static KeyValue Parse(params byte[][] top)
    {
        using MemoryStream stream = new(Concat(Concat(top), new byte[] { 8 }));
        KeyValue root = new();
        Assert.True(root.ReadAsBinary(stream));
        return root;
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}

public sealed class SteamSchemaReaderTests
{
    private static byte[] Achievement(string id, string name, string desc, string icon, string gray, int hidden, int permission = 0) =>
        Kv.Node(
            "1",
            Kv.Str("name", id),
            Kv.Node(
                "display",
                Kv.Node("name", Kv.Str("english", name), Kv.Str("german", name + "-de")),
                Kv.Node("desc", Kv.Str("english", desc)),
                Kv.Str("icon", icon),
                Kv.Str("icon_gray", gray),
                Kv.Int("hidden", hidden)),
            Kv.Int("permission", permission));

    internal static KeyValue Schema(string typeNode) =>
        Kv.Parse(Kv.Node(
            "480",
            Kv.Node(
                "stats",
                Kv.Node(
                    "1", Kv.Str("name", "NUM_GAMES"), Kv.Str("type", "INT"), Kv.Int("min", 0), Kv.Int("max", 1000),
                    Kv.Int("incrementonly", 1), Kv.Int("permission", 0)),
                Kv.Node(
                    "3", Kv.Str("name", "DISTANCE"), Kv.Str("type", "FLOAT"), Kv.Int("permission", 2),
                    Kv.Node("display", Kv.Node("name", Kv.Str("english", "Distance")))),
                Kv.Node(
                    "2",
                    typeNode == "new" ? Kv.Str("type", "ACHIEVEMENTS") : Kv.Int("type_int", 4),
                    Kv.Node(
                        "bits",
                        Achievement("ACH_ONE", "First", "Do it", "a.jpg", "a_gray.jpg", 0),
                        Achievement("ACH_TWO", "Second", "Secret", "b.jpg", "b_gray.jpg", 1),
                        Achievement("ACH_PROTECTED", "Locked by Steam", "", "c.jpg", "c_gray.jpg", 0, 3))))));

    [Theory]
    [InlineData("new")]
    [InlineData("old")]
    public void Both_schema_shapes_give_the_achievements_and_skip_other_stats(string shape)
    {
        var list = SteamSchemaReader.ReadAchievements(Schema(shape), 480, "english");

        Assert.Equal(new[] { "ACH_ONE", "ACH_TWO", "ACH_PROTECTED" }, list.Select(a => a.Id));
        Assert.Equal("First", list[0].Name);
        Assert.Equal("Do it", list[0].Description);
        Assert.Equal("a_gray.jpg", list[0].IconLocked);
        Assert.False(list[0].Hidden);
        Assert.True(list[1].Hidden);
    }

    [Fact]
    public void The_language_is_used_and_english_is_the_fallback()
    {
        var german = SteamSchemaReader.ReadAchievements(Schema("new"), 480, "german");

        Assert.Equal("First-de", german[0].Name);
        // The description has no German text, so English stands in for it.
        Assert.Equal("Do it", german[0].Description);
    }

    [Fact]
    public void A_schema_for_another_app_gives_nothing()
    {
        Assert.Empty(SteamSchemaReader.ReadAchievements(Schema("new"), 999, "english"));
    }
}

public sealed class SteamAppBackendTests
{
    private sealed class FakeGateway : ISteamAppGateway
    {
        public long AppId => 480;
        public string Language => "english";
        public KeyValue? Schema { get; set; } = SteamSchemaReaderTests.Schema("new");
        public Dictionary<string, (bool Unlocked, uint Time)> State { get; } = new()
        {
            ["ACH_ONE"] = (true, 1_700_000_000),
            ["ACH_TWO"] = (false, 0),
            ["ACH_PROTECTED"] = (false, 0),
        };
        public List<string> Calls { get; } = new();
        public bool FailSet { get; set; }
        public string? FailSetOf { get; set; }
        /// <summary>The achievement that takes a change once and then refuses to be changed back.</summary>
        public string? FailUndoOf { get; set; }
        public bool FailStore { get; set; }
        public Dictionary<string, int> IntStats { get; } = new() { ["NUM_GAMES"] = 7 };
        public Dictionary<string, float> FloatStats { get; } = new() { ["DISTANCE"] = 1.5f };
        public bool? Reset { get; private set; }

        public bool TryGetIntStat(string id, out int value) => IntStats.TryGetValue(id, out value);

        public bool TryGetFloatStat(string id, out float value) => FloatStats.TryGetValue(id, out value);

        public bool SetIntStat(string id, int value)
        {
            Calls.Add($"int {id}={value}");
            return FailSet == false;
        }

        public bool SetFloatStat(string id, float value)
        {
            Calls.Add($"float {id}={value}");
            return FailSet == false;
        }

        public bool ResetAllStats(bool includeAchievements)
        {
            Reset = includeAchievements;
            return true;
        }

        public Task EnsureUserStatsAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stats");
            return Task.CompletedTask;
        }

        public KeyValue? LoadSchema() => Schema;

        public bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime)
        {
            var found = State.TryGetValue(id, out var s);
            unlocked = s.Unlocked;
            unlockTime = s.Time;
            return found;
        }

        public bool SetAchievement(string id, bool unlocked)
        {
            var again = Calls.Any(c => c.StartsWith($"set {id}=", StringComparison.Ordinal));
            Calls.Add($"set {id}={unlocked}");
            return FailSet == false && id != FailSetOf && (id != FailUndoOf || again == false);
        }

        public bool StoreStats()
        {
            Calls.Add("store");
            return FailStore == false;
        }

        public ulong SteamId => 0;
        public bool IsSubscribed(uint appId) => false;
        public string? GetAppData(uint appId, string key) => null;
        public GameStats? GetCachedStats(uint appId) => null;
        public bool IsSteamStillRunning() => true;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task Achievements_combine_the_schema_with_the_account_state()
    {
        var backend = new SteamAppBackend(new FakeGateway());

        var list = await backend.GetAchievementsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, list.Count);
        Assert.True(list[0].Unlocked);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), list[0].UnlockDate);
        Assert.Equal("https://cdn.steamstatic.com/steamcommunity/public/images/apps/480/a.jpg", list[0].IconUrl);

        Assert.False(list[1].Unlocked);
        Assert.Null(list[1].UnlockDate);
        Assert.True(list[1].Hidden);
        // A locked achievement shows the grey icon.
        Assert.EndsWith("/b_gray.jpg", list[1].IconUrl);
        Assert.Null(list[1].XP);
        Assert.False(list[1].Protected);
        Assert.True(list[2].Protected);
    }

    [Fact]
    public async Task An_achievement_steam_does_not_know_is_left_out()
    {
        var gateway = new FakeGateway();
        gateway.State.Remove("ACH_TWO");
        gateway.State.Remove("ACH_PROTECTED");

        var list = await new SteamAppBackend(gateway).GetAchievementsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ACH_ONE", Assert.Single(list).Id);
    }

    [Fact]
    public async Task Without_a_cached_schema_the_error_says_so()
    {
        var backend = new SteamAppBackend(new FakeGateway { Schema = null });

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => backend.GetAchievementsAsync(TestContext.Current.CancellationToken));

        Assert.Equal("no_schema", error.ErrorCode);
    }

    [Fact]
    public async Task Changes_are_set_one_by_one_then_stored_once()
    {
        var gateway = new FakeGateway();

        await new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_ONE", false), new KeyValuePair<string, bool>("ACH_TWO", true) },
            Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "stats", "set ACH_ONE=False", "set ACH_TWO=True", "store" }, gateway.Calls);
    }

    [Fact]
    public async Task A_refused_change_stops_before_anything_is_stored()
    {
        var gateway = new FakeGateway { FailSet = true };

        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_ONE", false) }, Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken));

        Assert.Equal("set_failed", error.ErrorCode);
        Assert.DoesNotContain("store", gateway.Calls);
    }

    private static readonly KeyValuePair<string, bool>[] _noAchievements = Array.Empty<KeyValuePair<string, bool>>();

    private static async Task<ProviderException> Refused(
        FakeGateway gateway, KeyValuePair<string, bool>[] achievements, params (string Id, double Value)[] statistics)
    {
        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            achievements,
            statistics.Select(s => new KeyValuePair<string, double>(s.Id, s.Value)).ToArray(),
            TestContext.Current.CancellationToken));
        Assert.DoesNotContain("store", gateway.Calls);
        return error;
    }

    private static void NothingWasSet(FakeGateway gateway) => Assert.Equal(new[] { "stats" }, gateway.Calls);

    [Theory]
    [InlineData(double.NaN, "invalid_value")]
    [InlineData(double.PositiveInfinity, "invalid_value")]
    [InlineData(3_000_000_000d, "invalid_value")]
    [InlineData(1001, "out_of_range")]
    [InlineData(-1, "out_of_range")]
    [InlineData(6, "increment_only")]
    public async Task A_bad_statistic_refuses_the_whole_commit_before_anything_is_set(double value, string code)
    {
        var gateway = new FakeGateway();

        var error = await Refused(gateway, new[] { new KeyValuePair<string, bool>("ACH_TWO", true) }, ("NUM_GAMES", value));

        Assert.Equal(code, error.ErrorCode);
        NothingWasSet(gateway);
    }

    [Theory]
    [InlineData("NOT_THERE", "unknown_stat")]
    [InlineData("DISTANCE", "protected")]
    public async Task A_statistic_that_may_not_be_set_stops_the_ones_before_it(string id, string code)
    {
        var gateway = new FakeGateway();

        var error = await Refused(gateway, _noAchievements, ("NUM_GAMES", 40), (id, 1));

        Assert.Equal(code, error.ErrorCode);
        NothingWasSet(gateway);
    }

    [Theory]
    [InlineData("ACH_NOT_THERE", "unknown_achievement")]
    [InlineData("ACH_PROTECTED", "protected")]
    public async Task An_achievement_that_may_not_be_set_stops_the_ones_before_it(string id, string code)
    {
        var gateway = new FakeGateway();

        var error = await Refused(gateway, new[] { new KeyValuePair<string, bool>("ACH_TWO", true), new KeyValuePair<string, bool>(id, true) });

        Assert.Equal(code, error.ErrorCode);
        NothingWasSet(gateway);
    }

    [Fact]
    public async Task Without_a_schema_no_achievement_is_changed()
    {
        var gateway = new FakeGateway { Schema = null };

        var error = await Refused(gateway, new[] { new KeyValuePair<string, bool>("ACH_TWO", true) });

        Assert.Equal("no_schema", error.ErrorCode);
        NothingWasSet(gateway);
    }

    [Fact]
    public async Task A_setter_that_fails_half_way_puts_back_what_was_already_set()
    {
        var gateway = new FakeGateway { FailSetOf = "ACH_ONE" };

        var error = await Refused(
            gateway, new[] { new KeyValuePair<string, bool>("ACH_TWO", true), new KeyValuePair<string, bool>("ACH_ONE", false) });

        Assert.Equal("set_failed", error.ErrorCode);
        // ACH_TWO was locked before the commit, so it is locked again; nothing is left to be stored later.
        Assert.Equal(new[] { "stats", "set ACH_TWO=True", "set ACH_ONE=False", "set ACH_TWO=False" }, gateway.Calls);
    }

    [Fact]
    public async Task A_statistic_may_not_move_further_at_once_than_the_schema_allows()
    {
        var gateway = new FakeGateway
        {
            Schema = Kv.Parse(Kv.Node(
                "480",
                Kv.Node("stats", Kv.Node("1", Kv.Str("name", "NUM_GAMES"), Kv.Str("type", "INT"), Kv.Int("maxchange", 5))))),
        };

        var error = await Refused(gateway, _noAchievements, ("NUM_GAMES", 13));

        Assert.Equal("max_change", error.ErrorCode);
        NothingWasSet(gateway);
    }

    [Fact]
    public async Task A_store_that_fails_puts_every_change_back()
    {
        var gateway = new FakeGateway { FailStore = true };

        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_TWO", true) }, new[] { new KeyValuePair<string, double>("NUM_GAMES", 40) },
            TestContext.Current.CancellationToken));

        Assert.Equal("store_failed", error.ErrorCode);
        // What was set and not stored would be written by the next store, ours or the game's.
        Assert.Equal(
            new[] { "stats", "set ACH_TWO=True", "int NUM_GAMES=40", "store", "int NUM_GAMES=7", "set ACH_TWO=False" },
            gateway.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_value_that_cannot_be_put_back_is_said_so(bool storeFails)
    {
        // Either the store fails, or a later setter does; in both, ACH_TWO will not go back.
        var gateway = new FakeGateway { FailUndoOf = "ACH_TWO", FailStore = storeFails, FailSetOf = storeFails ? null : "ACH_ONE" };

        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_TWO", true), new KeyValuePair<string, bool>("ACH_ONE", false) },
            Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken));

        Assert.Equal("undo_failed", error.ErrorCode);
        Assert.Contains("ACH_TWO", error.Message);
    }

    [Fact]
    public async Task A_store_that_fails_is_reported()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(new FakeGateway { FailStore = true }).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_ONE", false) }, Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken));

        Assert.Equal("store_failed", error.ErrorCode);
    }

    [Fact]
    public async Task No_changes_do_not_touch_steam()
    {
        var gateway = new FakeGateway();

        await new SteamAppBackend(gateway).CommitAsync(
            Array.Empty<KeyValuePair<string, bool>>(), Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken);

        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Statistics_carry_their_kind_value_and_limits()
    {
        var stats = await new SteamAppBackend(new FakeGateway()).GetStatsAsync(TestContext.Current.CancellationToken);

        var games = Assert.Single(stats, s => s.Id == "NUM_GAMES");
        Assert.Equal(StoreStatKind.Integer, games.Kind);
        Assert.Equal(7, games.Value);
        Assert.Equal(0, games.Min);
        Assert.Equal(1000, games.Max);
        Assert.True(games.IncrementOnly);
        Assert.False(games.Protected);

        var distance = Assert.Single(stats, s => s.Id == "DISTANCE");
        Assert.Equal(StoreStatKind.Float, distance.Kind);
        Assert.Equal(1.5, distance.Value);
        Assert.True(distance.Protected);
    }

    [Fact]
    public async Task Statistics_are_set_by_kind_and_stored_with_the_achievements()
    {
        var gateway = new FakeGateway();
        // The schema marks DISTANCE as protected, so only the integer one is a legal target here.
        await new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_TWO", true) },
            new[] { new KeyValuePair<string, double>("NUM_GAMES", 40.0) },
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "stats", "set ACH_TWO=True", "int NUM_GAMES=40", "store" }, gateway.Calls);
    }

    [Fact]
    public async Task A_protected_statistic_is_refused_before_anything_is_stored()
    {
        var gateway = new FakeGateway();

        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            Array.Empty<KeyValuePair<string, bool>>(),
            new[] { new KeyValuePair<string, double>("DISTANCE", 2.0) },
            TestContext.Current.CancellationToken));

        Assert.Equal("protected", error.ErrorCode);
        Assert.DoesNotContain("store", gateway.Calls);
    }

    [Fact]
    public async Task An_unknown_statistic_is_refused()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(new FakeGateway()).CommitAsync(
            Array.Empty<KeyValuePair<string, bool>>(),
            new[] { new KeyValuePair<string, double>("NOPE", 1.0) },
            TestContext.Current.CancellationToken));

        Assert.Equal("unknown_stat", error.ErrorCode);
    }

    [Fact]
    public async Task A_protected_achievement_is_refused()
    {
        var gateway = new FakeGateway();

        var error = await Assert.ThrowsAsync<ProviderException>(() => new SteamAppBackend(gateway).CommitAsync(
            new[] { new KeyValuePair<string, bool>("ACH_PROTECTED", true) },
            Array.Empty<KeyValuePair<string, double>>(),
            TestContext.Current.CancellationToken));

        Assert.Equal("protected", error.ErrorCode);
        Assert.DoesNotContain("store", gateway.Calls);
    }

    [Fact]
    public async Task A_reset_passes_the_choice_on()
    {
        var gateway = new FakeGateway();

        await new SteamAppBackend(gateway).ResetAsync(true, TestContext.Current.CancellationToken);

        Assert.True(gateway.Reset);
    }
}
