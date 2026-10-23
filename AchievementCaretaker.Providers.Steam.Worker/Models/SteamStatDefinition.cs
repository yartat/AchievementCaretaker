/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers.Steam.Models;

/// <summary>One statistic as Steam's schema describes it.</summary>
/// <param name="Id">The statistic's API name, which it is set by.</param>
/// <param name="DisplayName">The name in the game's language, falling back to English and then to <paramref name="Id"/>.</param>
/// <param name="Kind">Whether the value is a whole number or has a fraction.</param>
/// <param name="Min">The lowest value Steam accepts.</param>
/// <param name="Max">The highest value Steam accepts.</param>
/// <param name="MaxChange">The most the value may change in one go; 0 when unlimited.</param>
/// <param name="IncrementOnly">The value can only grow.</param>
/// <param name="DefaultValue">What the statistic starts at and returns to when reset.</param>
/// <param name="Permission">Steam's permission bits; bit 2 means it cannot be changed from a client.</param>
public sealed record SteamStatDefinition(
    string Id, string DisplayName, StoreStatKind Kind, double Min, double Max, double MaxChange,
    bool IncrementOnly, double DefaultValue, int Permission)
{
    /// <summary>Steam will not take a change to it from a client (permission bit 2).</summary>
    public bool Protected => (Permission & 2) != 0;
}
