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

using System;

namespace AchievementCaretaker.Services.Models;

/// <summary>
/// A row of the cached Steam library, as stored in SQLite. The account it belongs
/// to is not on the row: every call names it.
/// </summary>
/// <remarks>
/// <see cref="AchievementsTotal"/> and <see cref="AchievementsEarned"/> are -1 until Steam
/// has cached a schema for the game: unknown is not zero.
/// </remarks>
public sealed record CachedGame
{
    /// <summary>The Steam app id.</summary>
    public uint Id { get; init; }

    /// <summary>What kind of app it is ("normal", "demo", "mod", "junk").</summary>
    public string Type { get; init; }

    /// <summary>The name to show.</summary>
    public string Name { get; init; }

    /// <summary>The address of the capsule art, or null when none was found.</summary>
    public string ImageUrl { get; init; }

    /// <summary>When the game was released, or null when Steam does not say.</summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>The percentage of positive Steam reviews, or null when there are none.</summary>
    public int? RatingPercent { get; init; }

    /// <summary>Steam's 1-9 review band, used for the wording of the rating, or null.</summary>
    public int? RatingScore { get; init; }

    /// <summary>Minutes played.</summary>
    public int PlaytimeMinutes { get; init; }

    /// <summary>When the game was last played, or null when never.</summary>
    public DateTime? LastPlayed { get; init; }

    /// <summary>How many achievements the game has, or -1 when unknown.</summary>
    public int AchievementsTotal { get; init; } = -1;

    /// <summary>How many of them the account has earned, or -1 when unknown.</summary>
    public int AchievementsEarned { get; init; } = -1;

    /// <summary>Whether the numbers above were read from Steam's caches at all.</summary>
    public bool HasStats { get; init; }
}
