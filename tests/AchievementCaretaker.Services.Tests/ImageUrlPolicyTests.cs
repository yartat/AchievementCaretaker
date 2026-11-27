/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

/// <summary>Image addresses come from servers and from saved rows, so not all of them are to be trusted.</summary>
public sealed class ImageUrlPolicyTests
{
    private static readonly string _icons = Path.Combine(Path.GetTempPath(), "sac-home", "ubisoft", "icons");

    private static string AddressOf(string path) => new Uri(path).AbsoluteUri;

    [Fact]
    public void An_icon_ubisoft_took_out_of_its_archive_is_read_from_disk()
    {
        var file = Path.Combine(_icons, "920", "1.png");

        Assert.True(ImageUrlPolicy.TryGetLocalFile("ubisoft", AddressOf(file), _icons, out var path));
        Assert.Equal(Path.GetFullPath(file), path);
    }

    [Theory]
    [InlineData("gog")]
    [InlineData("epic")]
    [InlineData("steam")]
    public void No_other_store_reads_files(string provider)
    {
        var file = Path.Combine(_icons, "920", "1.png");

        Assert.False(ImageUrlPolicy.TryGetLocalFile(provider, AddressOf(file), _icons, out _));
    }

    [Theory]
    [InlineData("file://host.example/share/a.png")]
    [InlineData("file:////host.example/share/a.png")]
    public void A_file_on_another_computer_is_never_opened(string url)
    {
        Assert.False(ImageUrlPolicy.TryGetLocalFile("ubisoft", url, _icons, out _));
        Assert.False(ImageUrlPolicy.TryGetDownload(url, out _));
    }

    [Fact]
    public void A_file_outside_the_icon_folder_is_not_read()
    {
        var outside = Path.Combine(Path.GetTempPath(), "sac-home", "ubisoft.json");
        var around = AddressOf(_icons) + "/920/%2E%2E/%2E%2E/%2E%2E/ubisoft.json";
        var sibling = Path.Combine(_icons + "-other", "a.png");

        Assert.False(ImageUrlPolicy.TryGetLocalFile("ubisoft", AddressOf(outside), _icons, out _));
        Assert.False(ImageUrlPolicy.TryGetLocalFile("ubisoft", around, _icons, out _));
        Assert.False(ImageUrlPolicy.TryGetLocalFile("ubisoft", AddressOf(sibling), _icons, out _));
    }

    [Fact]
    public void Only_https_is_downloaded()
    {
        Assert.True(ImageUrlPolicy.TryGetDownload("https://images.gog-statics.com/a.jpg", out var address));
        Assert.Equal("images.gog-statics.com", address.Host);
        Assert.False(ImageUrlPolicy.TryGetDownload("http://images.gog-statics.com/a.jpg", out _));
        Assert.False(ImageUrlPolicy.TryGetDownload("ftp://example.com/a.jpg", out _));
        Assert.False(ImageUrlPolicy.TryGetDownload("not an address", out _));
    }
}
