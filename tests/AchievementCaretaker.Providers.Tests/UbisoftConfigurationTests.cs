/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Linq;
using AchievementCaretaker.Providers.Ubisoft;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>The launcher's configuration catalog: the shapes of block seen in the wild.</summary>
public sealed class UbisoftConfigurationTests
{
    [Fact]
    public void A_name_that_is_a_key_is_found_in_the_localization_table_and_tidied()
    {
        var game = GameConfigurations.Parse(1, 1, "root:\n  name: NAME\n  start_game:\n    x: 1\nlocalizations:\n  default:\n    NAME: \"Far Cry\u00ae 6 (Steam)\"\n");

        Assert.Equal("Far Cry 6", game.Name);
        Assert.True(game.IsGame);
    }

    [Fact]
    public void A_placeholder_name_falls_back_to_the_installer_identifier_and_then_to_nothing()
    {
        var withInstaller = GameConfigurations.Parse(1, 1, "root:\n  name: l1\n  installer:\n    game_identifier: Anno 1800\n");
        var stub = GameConfigurations.Parse(2, 2, "root:\n  name: l1\n");

        Assert.Equal("Anno 1800", withInstaller.Name);
        Assert.Null(stub.Name);
        Assert.False(stub.IsGame);
    }

    [Fact]
    public void Pictures_and_the_achievements_list_are_read_through_the_table_and_tabs_are_tolerated()
    {
        var game = GameConfigurations.Parse(1, 1,
            "root:\n\tname: A\n\tthumb_image: THUMB\n\tuplay:\n\t\tachievements: hash.zip\n\t\tachievements_sync_id: ACK\n" +
            "localizations:\n  default:\n    THUMB: t.png\n");

        Assert.Equal("t.png", game.ThumbImage);
        Assert.Null(game.BackgroundImage);
        Assert.Equal("hash.zip", game.AchievementsArchive);
        Assert.Equal("ACK", game.AchievementsSyncId);
    }

    [Fact]
    public void A_file_is_read_up_to_where_it_stops_making_sense()
    {
        var file = UbisoftFiles.Configurations((1, 2, "root:\n  name: A\n"), (3, null, "root:\n  name: B\n"));

        Assert.Equal(new uint[] { 1, 3 }, GameConfigurations.Read(file).Select(c => c.ProductId));
        Assert.Equal(new uint[] { 2, 3 }, GameConfigurations.Read(file).Select(c => c.LaunchId));
        Assert.Single(GameConfigurations.Read(file.Concat(new byte[] { 0x0a, 0x7f }).ToArray()), c => c.ProductId == 1);
        Assert.Empty(GameConfigurations.Read(new byte[] { 0xff, 0xff }));
    }
}
