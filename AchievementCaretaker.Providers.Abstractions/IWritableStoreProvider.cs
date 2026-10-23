/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers;

/// <summary>
/// A store that lets a signed-in player unlock and re-lock their own achievements.
/// </summary>
/// <remarks>
/// Separate from <see cref="IStoreProvider"/> because not every store can: Epic's
/// achievements cannot be re-locked, so Epic does not implement this and the
/// application shows no way to change them.
/// </remarks>
public interface IWritableStoreProvider : IStoreProvider
{
    /// <summary>
    /// Sets each listed achievement to the wanted state: true unlocks it, false
    /// locks it again. Stops at the first failure by throwing
    /// <see cref="ProviderException"/>; some changes may already have been made, so
    /// the caller reads the achievements again afterwards.
    /// </summary>
    /// <param name="gameKey">The game's <see cref="StoreGame.Key"/>.</param>
    /// <param name="changes">Achievement id and the state it should have.</param>
    Task SetAchievementsAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken = default);
}
