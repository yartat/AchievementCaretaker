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
    /// Whether changes can be made right now. A store that can only write when signed in to an account (Ubisoft
    /// Connect, whose local files are only read) says false otherwise, and the application offers no way to change
    /// anything.
    /// </summary>
    bool CanWrite => true;

    /// <summary>
    /// Whether an achievement that is unlocked can be locked again. False for a store that only records achievements
    /// (Ubisoft Connect): the application then offers no Lock or Invert, shows what is already unlocked as fixed, and
    /// asks before an unlock is sent, since it cannot be taken back.
    /// </summary>
    bool CanRelock => true;

    /// <summary>
    /// Sets each listed achievement to the wanted state: true unlocks it, false
    /// locks it again. Stops at the first failure by throwing
    /// <see cref="ProviderException"/>; some changes may already have been made, so
    /// the caller reads the achievements again afterwards.
    /// </summary>
    /// <param name="gameKey">The game's <see cref="StoreGame.Key"/>.</param>
    /// <param name="changes">Achievement id and the state it should have.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    Task SetAchievementsAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken = default);
}
