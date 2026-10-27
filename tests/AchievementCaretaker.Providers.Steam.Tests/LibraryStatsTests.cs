/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using Xunit;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>Steam's own caches, built in a temporary install directory.</summary>
public sealed class LibraryStatsTests : IDisposable
{
    // The 64-bit id of account 12345: the file names carry only the low 32 bits.
    private const ulong SteamId = 76561197960265728UL + 12345;
    private const string Account = "12345";

    private readonly string _install = Path.Combine(Path.GetTempPath(), "sac-steam-" + Guid.NewGuid().ToString("N"));

    public LibraryStatsTests()
    {
        Directory.CreateDirectory(Path.Combine(_install, "appcache", "stats"));
        Directory.CreateDirectory(Path.Combine(_install, "userdata", Account, "config"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_install, true); } catch (IOException) { }
    }

    private string Stats(string file) => Path.Combine(_install, "appcache", "stats", file);

    private static byte[] Bit(string index, string name) => Kv.Node(index, Kv.Str("name", name));

    /// <summary>Two blocks: block 1 defines bits 0 and 1, block 2 defines bit 0.</summary>
    private void WriteSchema(uint appId) =>
        File.WriteAllBytes(Stats($"UserGameStatsSchema_{appId}.bin"), Concat(
            Kv.Node(
                appId.ToString(),
                Kv.Node(
                    "stats",
                    Kv.Node("1", Kv.Str("type", "ACHIEVEMENTS"), Kv.Node("bits", Bit("0", "A"), Bit("1", "B"))),
                    Kv.Node("2", Kv.Str("type", "ACHIEVEMENTS"), Kv.Node("bits", Bit("0", "C")))))
            , new byte[] { 8 }));

    private void WriteEarned(uint appId, int block1, int block2) =>
        File.WriteAllBytes(Stats($"UserGameStats_{Account}_{appId}.bin"), Concat(
            Kv.Node("cache", Kv.Node("1", Kv.Int("data", block1)), Kv.Node("2", Kv.Int("data", block2))),
            new byte[] { 8 }));

    private void WriteLocalConfig(uint appId, int minutes, long lastPlayed) =>
        File.WriteAllText(
            Path.Combine(_install, "userdata", Account, "config", "localconfig.vdf"),
            "\"UserLocalConfigStore\"\n{\n\"Software\"\n{\n\"Valve\"\n{\n\"Steam\"\n{\n\"apps\"\n{\n" +
            $"\"{appId}\"\n{{\n\"Playtime\"\t\t\"{minutes}\"\n\"LastPlayed\"\t\t\"{lastPlayed}\"\n}}\n" +
            "}\n}\n}\n}\n}\n");

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream stream = new();
        foreach (var part in parts)
        {
            stream.Write(part);
        }
        return stream.ToArray();
    }

    [Fact]
    public void Earned_is_counted_from_the_bitfield_masked_to_the_bits_the_schema_defines()
    {
        WriteSchema(480);
        // Block 1: 0b111 sets bit 2, which the schema does not define; it must not count.
        WriteEarned(480, 0b111, 1);

        var stats = LibraryStats.Create(_install, SteamId)!.TryGet(480);

        Assert.NotNull(stats);
        Assert.Equal(3, stats!.Value.AchievementsTotal);
        Assert.Equal(3, stats!.Value.AchievementsEarned);
    }

    [Fact]
    public void A_schema_without_a_progress_file_is_zero_earned_not_unknown()
    {
        WriteSchema(480);

        var stats = LibraryStats.Create(_install, SteamId)!.TryGet(480);

        Assert.Equal(3, stats!.Value.AchievementsTotal);
        Assert.Equal(0, stats!.Value.AchievementsEarned);
    }

    [Fact]
    public void Without_a_schema_only_playtime_is_known_and_totals_are_unknown()
    {
        WriteLocalConfig(480, 90, 1_700_000_000);

        var stats = LibraryStats.Create(_install, SteamId)!.TryGet(480);

        Assert.Equal(90, stats!.Value.PlaytimeMinutes);
        Assert.Equal(-1, stats!.Value.AchievementsTotal);
        Assert.Equal(-1, stats!.Value.AchievementsEarned);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).LocalDateTime, stats!.Value.LastPlayed);
    }

    [Fact]
    public void A_game_nothing_is_known_about_is_null()
    {
        Assert.Null(LibraryStats.Create(_install, SteamId)!.TryGet(999));
    }

    [Fact]
    public void The_files_are_found_by_the_low_32_bits_of_the_steam_id()
    {
        WriteSchema(480);
        WriteEarned(480, 1, 0);

        // The same account under a different universe/type prefix still finds its files.
        var stats = LibraryStats.Create(_install, SteamId | (1UL << 56))!.TryGet(480);

        Assert.Equal(1, stats!.Value.AchievementsEarned);
    }

    [Fact]
    public void No_install_path_means_no_stats()
    {
        Assert.Null(LibraryStats.Create("", SteamId));
    }
}
