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

/// <summary>
/// The working directory is process-wide, so these run one at a time (a collection of their own)
/// and each puts it back.
/// </summary>
[Collection("working directory")]
public sealed class SteamAppIdFileTests
{
    [Fact]
    public void The_file_names_the_app_and_its_folder_is_the_working_directory_meanwhile()
    {
        var before = Directory.GetCurrentDirectory();

        using (var file = SteamAppIdFile.Create(480))
        {
            Assert.Equal("480", File.ReadAllText(Path.Combine(file.Folder, "steam_appid.txt")));
            Assert.Equal(Path.GetFullPath(file.Folder), Path.GetFullPath(Directory.GetCurrentDirectory()));
        }

        Assert.Equal(before, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void The_folder_is_removed_with_it()
    {
        string folder;
        using (var file = SteamAppIdFile.Create(105600))
        {
            folder = file.Folder;
            Assert.True(Directory.Exists(folder));
        }

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var file = SteamAppIdFile.Create(480);

        file.Dispose();
        file.Dispose();
    }

    [Fact]
    public void Two_helpers_do_not_share_a_file()
    {
        using var first = SteamAppIdFile.Create(480);
        using var second = SteamAppIdFile.Create(105600);

        Assert.NotEqual(first.Folder, second.Folder);
        Assert.Equal("480", File.ReadAllText(Path.Combine(first.Folder, "steam_appid.txt")));
        Assert.Equal("105600", File.ReadAllText(Path.Combine(second.Folder, "steam_appid.txt")));
    }

    [Fact]
    public void Nothing_is_left_next_to_the_executable()
    {
        var before = Directory.GetCurrentDirectory();

        using (SteamAppIdFile.Create(480))
        {
        }

        Assert.False(File.Exists(Path.Combine(before, "steam_appid.txt")));
    }
}
