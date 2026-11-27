/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class SignInAddressTests
{
    [Theory]
    [InlineData("https://www.epicgames.com/id/login?x=1", "\U0001F512 www.epicgames.com")]
    [InlineData("http://www.epicgames.com/id/login", "Not secure: www.epicgames.com")]
    [InlineData("https://www.epicgames.com@evil.example/", "\U0001F512 evil.example")]
    [InlineData("about:blank", "")]
    public void The_host_and_whether_it_is_encrypted_are_shown(string address, string expected)
    {
        Assert.Equal(expected, SignInAddress.Describe(new Uri(address)));
    }

    // A look-alike spelled with other letters is shown as what it really is.
    [Fact]
    public void A_name_in_other_letters_is_shown_in_its_ascii_form()
    {
        Assert.StartsWith("\U0001F512 xn--", SignInAddress.Describe(new Uri("https://\u0435picgames.com/")));
    }
}
