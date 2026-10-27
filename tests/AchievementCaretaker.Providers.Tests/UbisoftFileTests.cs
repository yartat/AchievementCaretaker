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
using AchievementCaretaker.Providers.Ubisoft;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>The readers of the launcher's files, on files built to their format.</summary>
public sealed class UbisoftFileTests : IDisposable
{
    private readonly UbisoftFiles _files = new();

    public void Dispose() => _files.Dispose();

    #region Ownership

    [Fact]
    public void The_ownership_file_gives_the_ids_of_both_fields_sorted_and_once()
    {
        var data = UbisoftFiles.Ownership((7013, 13504), (920, null), (7013, 920));

        Assert.Equal(new uint[] { 920, 7013, 13504 }, OwnershipFile.ReadIds(data));
    }

    [Fact]
    public void A_file_that_is_only_a_header_or_shorter_has_no_ids()
    {
        Assert.Empty(OwnershipFile.ReadIds(new byte[264]));
        Assert.Empty(OwnershipFile.ReadIds(new byte[10]));
        Assert.Empty(OwnershipFile.ReadIds(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void A_file_cut_short_keeps_what_was_read_before_the_cut()
    {
        var whole = UbisoftFiles.Ownership((100, null), (200, null), (300, null));
        var cut = whole[..^2];

        var ids = OwnershipFile.ReadIds(cut);

        Assert.Equal(new uint[] { 100, 200 }, ids);
    }

    [Fact]
    public void The_ownership_cache_is_the_file_named_by_a_guid_and_that_is_the_user()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((7013, null)), "not-a-user-id");
        _files.WriteOwnership(UbisoftFiles.Ownership((920, null)));

        var cache = OwnershipFile.Find(_files.Locations);

        Assert.NotNull(cache);
        Assert.Equal(UbisoftFiles.UserId, cache.UserId);
        Assert.Equal(new uint[] { 920 }, cache.GameIds);
    }

    [Fact]
    public void With_several_users_the_newest_cache_is_the_current_one()
    {
        var older = _files.WriteOwnership(UbisoftFiles.Ownership((1, null)), "11111111-1111-4111-8111-111111111111");
        var newer = _files.WriteOwnership(UbisoftFiles.Ownership((2, null)), "22222222-2222-4222-8222-222222222222");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var cache = OwnershipFile.Find(_files.Locations);

        Assert.Equal("22222222-2222-4222-8222-222222222222", cache?.UserId);
    }

    [Fact]
    public void Without_a_launcher_there_is_no_ownership_cache()
    {
        Assert.Null(OwnershipFile.Find(_files.Locations));
    }

    #endregion

    #region Spool

    [Fact]
    public void A_spool_gives_each_achievement_with_the_time_it_was_earned()
    {
        var records = SpoolFile.Read(UbisoftFiles.Spool((14, 1_614_534_450), (1, 1_614_461_834)));

        Assert.Equal(new uint[] { 1, 14 }, records.Select(r => r.AchievementId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_614_461_834), records[0].EarnedAt);
    }

    [Fact]
    public void A_time_in_milliseconds_is_told_from_one_in_seconds()
    {
        var records = SpoolFile.Read(UbisoftFiles.Spool((1, 1_614_461_834), (2, 1_614_461_834_000)));

        Assert.Equal(records[0].EarnedAt, records[1].EarnedAt);
    }

    [Fact]
    public void An_achievement_in_the_spool_twice_is_one_record_with_the_earlier_time()
    {
        var records = SpoolFile.Read(UbisoftFiles.Spool((5, 2_000_000_000), (5, 1_600_000_000)));

        var only = Assert.Single(records);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000), only.EarnedAt);
    }

    [Fact]
    public void A_record_without_an_id_or_a_time_is_no_record()
    {
        var data = UbisoftFiles.Spool((9, 1_600_000_000)).Concat(new byte[] { 0x0A, 0x02, 0x10, 0x05 }).ToArray();

        Assert.Equal(new uint[] { 9 }, SpoolFile.Read(data).Select(r => r.AchievementId));
    }

    [Fact]
    public void A_time_that_is_no_moment_at_all_is_no_record()
    {
        var records = SpoolFile.Read(UbisoftFiles.Spool((1, long.MaxValue), (2, 1_600_000_000)));

        Assert.Equal(new uint[] { 2 }, records.Select(r => r.AchievementId));
    }

    [Fact]
    public void A_spool_cut_short_keeps_what_was_read_before_the_cut()
    {
        var whole = UbisoftFiles.Spool((1, 1_600_000_000), (2, 1_600_000_100));

        Assert.Equal(new uint[] { 1 }, SpoolFile.Read(whole[..^3]).Select(r => r.AchievementId));
    }

    [Fact]
    public void A_game_with_no_spool_has_no_records_and_one_that_is_there_is_read()
    {
        Assert.Empty(SpoolFile.ReadForGame(_files.Locations, UbisoftFiles.UserId, 920));

        _files.WriteSpool(920, UbisoftFiles.Spool((3, 1_600_000_000)));

        Assert.Equal(new uint[] { 3 }, SpoolFile.ReadForGame(_files.Locations, UbisoftFiles.UserId, 920).Select(r => r.AchievementId));
        Assert.Empty(SpoolFile.ReadForGame(_files.Locations, "another-user", 920));
    }

    #endregion

    #region Names

    [Fact]
    public void The_list_of_names_is_id_bar_name_and_skips_what_is_not()
    {
        var names = GameNames.Parse(new[] { "920|Far Cry 6", "", "no separator", "x|Not a number", "|No id", "5|" });

        Assert.Equal(1, names.Count);
        Assert.True(names.TryGetName(920, out var name));
        Assert.Equal("Far Cry 6", name);
        Assert.False(names.TryGetName(5, out _));
    }

    [Theory]
    [InlineData("Assassin's Creed® Valhalla (Steam)", "Assassin's Creed Valhalla")]
    [InlineData("Anno 1800 (Uplay+Steam)", "Anno 1800")]
    [InlineData("Far Cry® 6 (Steam Version)", "Far Cry 6")]
    [InlineData("Watch_Dogs (Rus)", "Watch_Dogs (Rus)")]
    [InlineData("Tom Clancy's Ghost Recon™", "Tom Clancy's Ghost Recon")]
    public void A_name_loses_its_trademark_signs_and_the_note_of_the_edition_and_nothing_else(string listed, string shown)
    {
        Assert.Equal(shown, GameNames.Tidy(listed));
    }

    [Fact]
    public void The_list_that_ships_with_the_application_names_the_games()
    {
        var names = GameNames.Embedded;

        Assert.True(names.Count > 400);
        Assert.True(names.TryGetName(7013, out var valhalla));
        Assert.Equal("Assassin's Creed Valhalla", valhalla);
    }

    #endregion

    #region Archives

    private static byte[] SampleArchive() => UbisoftFiles.Archive(
        new Dictionary<string, string>
        {
            ["en-US"] = "\n3\tThird\tDo it\twith a tab in it\n1\tFirst\tBegin\nnot a line\n\n2\tSecond\n",
            ["ru-RU"] = "1\tПервое\tНачни\n2\tВторое\tПродолжи\n3\tТретье\tЗакончи\n",
            ["fr-FR"] = "1\tPremier\tCommence\n",
        },
        new Dictionary<string, byte[]> { ["1.png"] = new byte[] { 1, 2, 3 }, ["3.png"] = new byte[] { 9 }, ["achievements.dat"] = new byte[] { 0 } });

    [Fact]
    public void An_archive_gives_the_achievements_by_id_with_the_description_that_has_tabs_in_it()
    {
        var path = _files.WriteArchive(920, SampleArchive());

        using var archive = AchievementArchive.TryOpen(path)!;
        var achievements = archive.ReadAchievements("en");

        Assert.Equal(new uint[] { 1, 2, 3 }, achievements.Select(a => a.Id));
        Assert.Equal("Third", achievements[2].Name);
        Assert.Equal("Do it\twith a tab in it", achievements[2].Description);
        Assert.Equal("", achievements[1].Description);
    }

    [Theory]
    [InlineData("ru-RU", "ru-RU")]
    [InlineData("RU-ru", "ru-RU")]
    [InlineData("ru", "ru-RU")]
    [InlineData("en", "en-US")]
    [InlineData("de", "en-US")]
    public void A_language_is_the_one_asked_for_or_the_same_language_or_English(string asked, string resolved)
    {
        using var archive = AchievementArchive.TryOpen(_files.WriteArchive(920, SampleArchive()))!;

        Assert.Equal(resolved, archive.ResolveLocale(asked));
    }

    [Fact]
    public void An_archive_without_English_falls_back_to_the_first_language()
    {
        var path = _files.WriteArchive(1, UbisoftFiles.Archive(new Dictionary<string, string> { ["fr-FR"] = "1\tPremier\tX\n", ["de-DE"] = "1\tErster\tX\n" }));

        using var archive = AchievementArchive.TryOpen(path)!;

        Assert.Equal("de-DE", archive.ResolveLocale("en"));
    }

    [Fact]
    public void An_archive_without_any_localization_has_no_achievements()
    {
        var path = _files.WriteArchive(1, UbisoftFiles.Archive(new Dictionary<string, string>(), new Dictionary<string, byte[]> { ["1.png"] = new byte[] { 1 } }));

        using var archive = AchievementArchive.TryOpen(path)!;

        Assert.Null(archive.ResolveLocale("en"));
        Assert.Empty(archive.ReadAchievements("en"));
    }

    [Fact]
    public void An_icon_is_in_the_archive_or_it_is_not_and_comes_out_as_it_went_in()
    {
        using var archive = AchievementArchive.TryOpen(_files.WriteArchive(920, SampleArchive()))!;

        Assert.True(archive.HasIcon(1));
        Assert.False(archive.HasIcon(2));
        using MemoryStream copy = new();
        archive.CopyIcon(1, copy);
        Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_not_an_archive()
    {
        var path = Path.Combine(_files.Locations.AchievementArchives, "file_abc.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        Assert.Null(AchievementArchive.TryOpen(path));
    }

    [Fact]
    public void A_games_archive_is_found_by_its_id_whether_or_not_the_name_ends_in_zip()
    {
        _files.WriteArchive(920, SampleArchive(), "920_6a1b0c9d8e7f");
        File.WriteAllBytes(Path.Combine(_files.Locations.AchievementArchives, "file_920_x.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_files.Locations.AchievementArchives, "9200_aaaa.zip"), new byte[] { 1 });

        using var found = AchievementArchive.Find(_files.Locations.AchievementArchives, 920);

        Assert.NotNull(found);
        Assert.EndsWith("920_6a1b0c9d8e7f", found.Path);
        Assert.Null(AchievementArchive.Find(_files.Locations.AchievementArchives, 4554));
        Assert.Null(AchievementArchive.Find(Path.Combine(_files.Locations.AchievementArchives, "missing"), 920));
    }

    [Fact]
    public void Of_two_archives_for_a_game_the_newer_that_opens_is_used()
    {
        var old = _files.WriteArchive(920, UbisoftFiles.Archive(new Dictionary<string, string> { ["en-US"] = "1\tOld\tX\n" }), "920_old.zip");
        var current = _files.WriteArchive(920, UbisoftFiles.Archive(new Dictionary<string, string> { ["en-US"] = "1\tCurrent\tX\n" }), "920_new.zip");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-9));
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow);

        using var found = AchievementArchive.Find(_files.Locations.AchievementArchives, 920);

        Assert.Equal("Current", found?.ReadAchievements("en")[0].Name);
    }

    #endregion
}
