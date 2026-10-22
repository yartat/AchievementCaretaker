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

namespace AchievementCaretaker.Services
{
    /// <summary>What reading one game's achievements came to: the list, or why there is none.</summary>
    /// <param name="Achievements">Empty when <paramref name="Failure"/> is set.</param>
    /// <param name="Failure">A sentence for the user, or null when the read worked.</param>
    public sealed record StoreAchievementsResult(IReadOnlyList<StoreAchievement> Achievements, string Failure);

    /// <summary>What reading one game's statistics came to.</summary>
    public sealed record StoreStatsResult(IReadOnlyList<StoreStat> Stats, string Failure);

    /// <summary>
    /// One store game's achievements: reading them, and sending changes back. The window
    /// keeps the list on screen and the unsent ticks; what goes to the store, and how a
    /// failure is put to the user, is here.
    /// </summary>
    public interface IStoreAchievementsService
    {
        /// <summary>Whether the provider can change achievements (Epic cannot, GOG can).</summary>
        bool CanModify(IStoreProvider provider);

        /// <summary>
        /// Reads the achievements. A store failure does not throw; it comes back as
        /// <see cref="StoreAchievementsResult.Failure"/>. Cancellation does throw.
        /// </summary>
        Task<StoreAchievementsResult> LoadAsync(IStoreProvider provider, string gameKey, CancellationToken cancellationToken);

        /// <summary>
        /// Sends each change (achievement id, wanted state). Returns null when all were sent,
        /// otherwise a sentence saying why not; some may have been made, so read again
        /// afterwards. Cancellation throws.
        /// </summary>
        Task<string> CommitAsync(
            IStoreProvider provider,
            string gameKey,
            IReadOnlyCollection<KeyValuePair<string, bool>> changes,
            CancellationToken cancellationToken);

        /// <summary>Whether the provider has statistics to show and edit (only Steam does).</summary>
        bool HasStatistics(IStoreProvider provider);

        /// <summary>Reads the statistics. A failure comes back as text; cancellation throws.</summary>
        Task<StoreStatsResult> LoadStatsAsync(IStoreProvider provider, string gameKey, CancellationToken cancellationToken);

        /// <summary>
        /// Sends achievement and statistic changes together, as one store. Returns null when all
        /// were sent, otherwise why not. Cancellation throws.
        /// </summary>
        Task<string> CommitAsync(
            IStoreProvider provider,
            string gameKey,
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken);

        /// <summary>Resets the statistics, and the achievements too if asked. Returns null, or why not.</summary>
        Task<string> ResetAsync(IStoreProvider provider, string gameKey, bool includeAchievements, CancellationToken cancellationToken);
    }
}
