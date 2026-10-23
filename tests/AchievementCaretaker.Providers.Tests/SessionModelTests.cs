/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using AchievementCaretaker.Providers.Epic.Models;
using AchievementCaretaker.Providers.Gog.Models;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>The sessions are records, which print every property: they must print none of these.</summary>
public sealed class SessionModelTests
{
    [Fact]
    public void An_epic_session_never_prints_its_tokens()
    {
        var text = new EpicSession { AccessToken = "SECRET-ACCESS", RefreshToken = "SECRET-REFRESH", AccountId = "acct" }.ToString();

        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("acct", text);
    }

    [Fact]
    public void A_gog_session_never_prints_its_tokens()
    {
        var text = new GogSession { AccessToken = "SECRET-ACCESS", RefreshToken = "SECRET-REFRESH", UserId = "user" }.ToString();

        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("user", text);
    }
}
