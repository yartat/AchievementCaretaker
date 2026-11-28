/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

public sealed class PrivateDataTests : IDisposable
{
    public sealed class Sample
    {
        public string RefreshToken { get; set; } = "";
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sac-private-" + Guid.NewGuid().ToString("N"));

    public PrivateDataTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void A_new_data_folder_is_the_users_own()
    {
        var path = Path.Combine(_directory, "home");

        PrivateDirectory.Create(path);

        Assert.True(Directory.Exists(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
        }
    }

    // An installation made by an earlier version left its folder readable by everyone.
    [Fact]
    public void A_data_folder_that_others_can_read_is_closed_to_them()
    {
        if (!OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_directory, "old");
            const UnixFileMode open = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            Directory.CreateDirectory(path, open);
            File.SetUnixFileMode(path, open);

            PrivateDirectory.Tighten(path);
            PrivateDirectory.Tighten(Path.Combine(_directory, "not-there"));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
            Assert.False(Directory.Exists(Path.Combine(_directory, "not-there")));
        }
        else
        {
            Assert.Skip("Windows has no such modes; the user profile is the user's own.");
        }
    }

    [Fact]
    public void A_session_that_cannot_be_read_is_kept_for_looking_at()
    {
        using var services = new ServiceCollection().AddSessionStore().BuildServiceProvider();
        var store = services.GetRequiredService<ISessionStore>();
        var path = Path.Combine(_directory, "a.json");
        File.WriteAllText(path, "{ damaged");

        Assert.Null(store.Load<Sample>(path));

        var copy = Assert.Single(Directory.GetFiles(_directory, "a.json.unreadable*"));
        Assert.Equal("a.json.unreadable", Path.GetFileName(copy));
        Assert.Equal("{ damaged", File.ReadAllText(copy));
    }

    private static ISessionStore Store() =>
        new ServiceCollection().AddSessionStore().BuildServiceProvider().GetRequiredService<ISessionStore>();

    private string[] Copies() => Directory.GetFiles(_directory, "*.unreadable*");

    [Fact]
    public async Task Reading_it_again_makes_no_second_copy()
    {
        var path = Path.Combine(_directory, "a.json");
        File.WriteAllText(path, "{ damaged");

        Store().Load<Sample>(path);
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        Store().Load<Sample>(path);

        Assert.Single(Copies());
    }

    // A file that is only busy is most likely fine, and a copy of it would be a copy of a credential.
    [Fact]
    public void A_file_that_is_only_locked_is_not_copied()
    {
        var path = Path.Combine(_directory, "a.json");
        Store().Save(path, new Sample { RefreshToken = "token" });
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(Store().Load<Sample>(path));
        }

        Assert.Empty(Copies());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_copy_goes_when_the_session_is_saved_again_or_signed_out_of(bool save)
    {
        var path = Path.Combine(_directory, "a.json");
        File.WriteAllText(path, "{ damaged");
        var store = Store();
        store.Load<Sample>(path);

        if (save == true)
        {
            store.Save(path, new Sample { RefreshToken = "token" });
        }
        else
        {
            store.Delete(path);
        }

        Assert.Empty(Copies());
    }

    [Fact]
    public void A_plain_session_that_cannot_be_encrypted_is_still_used_and_not_copied()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only the Windows store turns a plain file into an encrypted one.");
        }
        var path = Path.Combine(_directory, "a.json");
        File.WriteAllText(path, "{\"RefreshToken\":\"token\"}");
        // The new file is written to this name first, which a folder now holds.
        Directory.CreateDirectory(path + ".tmp");

        var session = Store().Load<Sample>(path);

        Assert.Equal("token", session?.RefreshToken);
        Assert.Empty(Copies());
    }

    [Fact]
    public void A_missing_session_leaves_nothing_behind()
    {
        using var services = new ServiceCollection().AddSessionStore().BuildServiceProvider();

        Assert.Null(services.GetRequiredService<ISessionStore>().Load<Sample>(Path.Combine(_directory, "none.json")));

        Assert.Empty(Directory.GetFiles(_directory));
    }
}
