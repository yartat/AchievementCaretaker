/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sac-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private string FilePath => Path.Combine(_directory, "a.bin");

    [Fact]
    public void It_writes_and_overwrites_and_leaves_nothing_else()
    {
        AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("one"));
        AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("two"));

        Assert.Equal("two", File.ReadAllText(FilePath));
        Assert.Equal("a.bin", Path.GetFileName(Directory.GetFiles(_directory).Single()));
    }

    // Replacing by rename, not writing in place: whoever holds the old file still reads it whole.
    [Fact]
    public void The_old_content_is_never_written_into()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows does not let an open file be replaced this way.");
        }
        File.WriteAllText(FilePath, "old content");
        using var held = new StreamReader(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("new"));

        Assert.Equal("old content", held.ReadToEnd());
    }

    // A settings file a user keeps elsewhere and links to stays linked.
    [Fact]
    public void A_link_stays_a_link_and_its_target_is_written()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Making a link needs a privilege here.");
        }
        var target = Path.Combine(_directory, "target.json");
        File.WriteAllText(target, "old");
        File.CreateSymbolicLink(FilePath, target);

        AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("new"));

        Assert.Equal(target, new FileInfo(FilePath).LinkTarget);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void A_write_that_fails_leaves_the_old_content()
    {
        File.WriteAllText(FilePath, "old content");
        // The temporary file cannot be made where a directory of its name stands.
        Directory.CreateDirectory(FilePath + ".tmp");

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("new")));

        Assert.Equal("old content", File.ReadAllText(FilePath));
    }
}
