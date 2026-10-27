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
using AchievementCaretaker.Providers.Steam.Models;
using AchievementCaretaker.SteamApi;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// The few native Steam calls the helper makes for its one app id. The real one wraps
/// <see cref="Client"/>; a test substitutes a fake, so everything above this runs without Steam.
/// </summary>
public interface ISteamAppGateway : IDisposable
{
    /// <summary>The app id this connection acts as; 0 for the library as a whole.</summary>
    long AppId { get; }

    /// <summary>The language Steam shows this game in, which is the one the schema is read in.</summary>
    string Language { get; }

    /// <summary>
    /// Asks Steam for the user's stats for this game and returns once it has answered. Done
    /// once; later calls return at once.
    /// </summary>
    Task EnsureUserStatsAsync(CancellationToken cancellationToken);

    /// <summary>Steam's cached schema for this game, or null if it has none.</summary>
    KeyValue? LoadSchema();

    /// <summary>False when Steam does not know the achievement.</summary>
    bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime);

    /// <summary>Unlocks or locks an achievement. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="id">The achievement's API name.</param>
    /// <param name="unlocked">True to unlock it, false to lock it again.</param>
    /// <returns>False when Steam refuses.</returns>
    bool SetAchievement(string id, bool unlocked);

    /// <summary>Reads an integer statistic; false when Steam has no such one.</summary>
    /// <param name="id">The statistic's API name.</param>
    /// <param name="value">The value.</param>
    bool TryGetIntStat(string id, out int value);

    /// <summary>Reads a floating-point statistic; false when Steam has no such one.</summary>
    /// <param name="id">The statistic's API name.</param>
    /// <param name="value">The value.</param>
    bool TryGetFloatStat(string id, out float value);

    /// <summary>Sets an integer statistic. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="id">The statistic's API name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>False when Steam refuses.</returns>
    bool SetIntStat(string id, int value);

    /// <summary>Sets a floating-point statistic. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="id">The statistic's API name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>False when Steam refuses.</returns>
    bool SetFloatStat(string id, float value);

    /// <summary>Resets every statistic, and the achievements too if asked. Cannot be undone.</summary>
    /// <param name="includeAchievements">True to lock every achievement as well.</param>
    /// <returns>False when Steam refuses.</returns>
    bool ResetAllStats(bool includeAchievements);

    /// <summary>Sends every change made so far to Steam.</summary>
    /// <returns>False when Steam could not take them.</returns>
    bool StoreStats();

    #region Library (the helper started for app id 0)

    /// <summary>The 64-bit id of the account Steam is logged in as.</summary>
    ulong SteamId { get; }

    /// <summary>Whether the account owns the app. Answers false for everything once Steam is gone.</summary>
    bool IsSubscribed(uint appId);

    /// <summary>One of Steam's metadata keys for an app (<c>name</c>, <c>small_capsule/english</c>, ...), or null.</summary>
    string? GetAppData(uint appId, string key);

    /// <summary>What Steam's own cache files say about a game: playtime and achievement counts. Null when they say nothing.</summary>
    GameStats? GetCachedStats(uint appId);

    /// <summary>
    /// Whether the Steam this was connected to is still the one running, asked directly and now.
    /// A scan over the whole published list can finish against a pipe that died halfway, with every
    /// later call answering "not owned"; a scan asks this when it ends.
    /// </summary>
    bool IsSteamStillRunning();

    #endregion
}
