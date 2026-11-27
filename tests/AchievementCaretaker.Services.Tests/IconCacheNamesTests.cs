/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class IconCacheNamesTests
{
    [Theory]
    [InlineData("570.img", 570u)]
    [InlineData("570_0123456789abcdef0123456789ABCDEF01234567.jpg", 570u)]
    public void A_file_the_cache_wrote_belongs_to_its_game(string name, uint expected)
    {
        Assert.True(IconCacheNames.TryGetAppId(name, out var appId));
        Assert.Equal(expected, appId);
    }

    // The directory is chosen by the user, and what is not recognised is never deleted or moved.
    [Theory]
    [InlineData("2024_taxes.pdf")]
    [InlineData("2024_holiday.jpg")]
    [InlineData("1.png")]
    [InlineData("123.txt")]
    [InlineData("123")]
    [InlineData("1.png.bak")]
    [InlineData("570.img.bak")]
    [InlineData("report.img")]
    [InlineData("570_notes.txt")]
    [InlineData("99999999999.img")]
    public void Anything_else_is_not_the_caches(string name)
    {
        Assert.False(IconCacheNames.TryGetAppId(name, out var appId));
        Assert.Equal(0u, appId);
    }
}
