/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>
    /// What the helper does with Steam for the one app id it was started for. The gRPC
    /// service is a thin face over this, so the real thing (Steam's native interface) and a
    /// fake in a test are interchangeable.
    /// </summary>
    public interface ISteamAppBackend
    {
        Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<StoreStat>> GetStatsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sets each achievement and statistic to the wanted value and stores them. Nothing is
        /// stored if any is refused.
        /// </summary>
        Task CommitAsync(
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken);

        Task ResetAsync(bool includeAchievements, CancellationToken cancellationToken);
    }
}
