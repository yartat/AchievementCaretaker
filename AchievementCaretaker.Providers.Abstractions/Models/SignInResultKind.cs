/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>What an address reached while signing in means.</summary>
public enum SignInResultKind
{
    /// <summary>Just a page on the way.</summary>
    None = 0,

    /// <summary>The address itself carries the result (GOG's redirect with <c>code=</c>).</summary>
    Address = 1,

    /// <summary>The page's text carries the result (Epic's small JSON document).</summary>
    PageText = 2,
}
