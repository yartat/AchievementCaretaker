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
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.SteamApi;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// The few native Steam calls the helper makes for its one app id. The real one wraps
/// <see cref="Client"/>; a test substitutes a fake, so everything above this runs without Steam.
/// </summary>
public interface ISteamAppGateway : IDisposable
{
    long AppId { get; }

    /// <summary>The language Steam shows this game in, which is the one the schema is read in.</summary>
    string Language { get; }

    /// <summary>
    /// Asks Steam for the user's stats for this game and returns once it has answered. Done
    /// once; later calls return at once.
    /// </summary>
    Task EnsureUserStatsAsync(CancellationToken cancellationToken);

    /// <summary>Steam's cached schema for this game, or null if it has none.</summary>
    KeyValue LoadSchema();

    /// <summary>False when Steam does not know the achievement.</summary>
    bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime);

    bool SetAchievement(string id, bool unlocked);

    bool TryGetIntStat(string id, out int value);

    bool TryGetFloatStat(string id, out float value);

    bool SetIntStat(string id, int value);

    bool SetFloatStat(string id, float value);

    bool ResetAllStats(bool includeAchievements);

    bool StoreStats();
}
