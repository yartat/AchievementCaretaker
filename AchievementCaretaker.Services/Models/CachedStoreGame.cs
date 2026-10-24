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

namespace AchievementCaretaker.Services.Models;

/// <summary>
/// A row of a store's cached library (Epic, GOG), as stored in SQLite.
/// </summary>
/// <remarks>
/// <see cref="AchievementsTotal"/> and <see cref="AchievementsEarned"/> are -1
/// until a game's achievements have been read, like <see cref="CachedGame"/>:
/// unknown is not zero.
/// </remarks>
public sealed record CachedStoreGame
{
    /// <summary>The game's id within its store.</summary>
    public string Id { get; init; }

    /// <summary>What the store's achievement service is addressed by (see <c>StoreGame.Key</c>).</summary>
    public string Key { get; init; }

    /// <summary>The name to show.</summary>
    public string Name { get; init; }

    /// <summary>The address of the capsule art, or null when the store has none.</summary>
    public string ImageUrl { get; init; }

    /// <summary>How many achievements the game has, or -1 until they have been read.</summary>
    public int AchievementsTotal { get; init; } = -1;

    /// <summary>How many of them the account has earned, or -1 until they have been read.</summary>
    public int AchievementsEarned { get; init; } = -1;

    /// <summary>Whether the achievement counts have been read; they are not meaningful otherwise.</summary>
    public bool HasProgress { get; init; }
}
