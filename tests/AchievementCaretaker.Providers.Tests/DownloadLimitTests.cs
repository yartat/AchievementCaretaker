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
using System.Net.Http;
using System.Text;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;
using AchievementCaretaker.Providers.Ubisoft;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>What a server or an archive hands over is held in memory or on disk, so each has a limit.</summary>
public sealed class DownloadLimitTests : IDisposable
{
    private const int Megabyte = 1024 * 1024;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sac-limits-" + Guid.NewGuid().ToString("N"));

    public DownloadLimitTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData(EpicProvider.HttpClientName, 32)]
    [InlineData(GogProvider.HttpClientName, 32)]
    [InlineData(UbisoftProvider.HttpClientName, 64)]
    public void A_stores_client_does_not_buffer_without_end(string name, int megabytes)
    {
        using var services = new ServiceCollection()
            .AddEpicProvider(_directory)
            .AddGogProvider(_directory)
            .AddUbisoftProvider(_directory)
            .BuildServiceProvider();
        if (name == UbisoftProvider.HttpClientName && OperatingSystem.IsWindows() == false)
        {
            Assert.Skip("Ubisoft Connect is registered on Windows only.");
        }

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(name);

        Assert.Equal(megabytes * Megabyte, client.MaxResponseContentBufferSize);
    }

    // The list of an archive's entries says what each unpacks to; it need not be telling the truth. The size
    // limits rest on the runtime never handing over more than that: .NET 10 stops there, later ones throw.
    [Fact]
    public void An_entry_never_gives_more_than_it_says_it_holds()
    {
        var bytes = UbisoftFiles.Archive(new Dictionary<string, string> { ["en-US"] = "1\tName\t" + new string('x', 20 * Megabyte) });
        for (var i = 0; i < bytes.Length - 4; i++)
        {
            // An entry of the central directory, whose unpacked size is 24 bytes in.
            if (bytes[i] == 'P' && bytes[i + 1] == 'K' && bytes[i + 2] == 1 && bytes[i + 3] == 2)
            {
                BitConverter.GetBytes(100).CopyTo(bytes, i + 24);
            }
        }
        var path = Path.Combine(_directory, "920_lies.zip");
        File.WriteAllBytes(path, bytes);

        using var archive = AchievementArchive.TryOpen(path)!;

        var read = archive.ReadAchievements("en");

        Assert.True(read.Sum(achievement => achievement.Name.Length + achievement.Description.Length) <= 100);
    }

    [Fact]
    public void An_oversized_entry_of_an_archive_is_not_taken()
    {
        var path = Path.Combine(_directory, "920_hash.zip");
        File.WriteAllBytes(path, UbisoftFiles.Archive(
            new Dictionary<string, string>
            {
                ["en-US"] = "1\tName\tText\n",
                ["ru-RU"] = new string('x', 9 * Megabyte),
            },
            new Dictionary<string, byte[]> { ["1.png"] = new byte[1024], ["2.png"] = new byte[17 * Megabyte] }));

        using var archive = AchievementArchive.TryOpen(path)!;

        Assert.True(archive.HasIcon(1));
        Assert.False(archive.HasIcon(2));
        Assert.Equal(new[] { "en-US" }, archive.Locales);
    }
}
