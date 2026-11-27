/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Linq;
using System.Text;
using System.Xml;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class PublishedListTests
{
    [Fact]
    public void A_list_that_declares_a_dtd_is_refused()
    {
        var bytes = Encoding.UTF8.GetBytes("<!DOCTYPE games [<!ENTITY x \"1\">]><games><game>&x;</game></games>");

        Assert.Throws<XmlException>(() => SteamLibraryService.ParseGameList(bytes));
    }

    [Fact]
    public void An_entry_that_is_not_a_number_is_left_out_and_the_rest_are_kept()
    {
        var bytes = Encoding.UTF8.GetBytes("<games><game>480</game><game>oops</game><game type=\"demo\">7</game></games>");

        var games = SteamLibraryService.ParseGameList(bytes);

        Assert.Equal(new[] { "480", "7" }, games.Select(g => g.Id));
    }
}
