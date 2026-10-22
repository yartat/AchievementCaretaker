/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers
{
    /// <summary>The kind of number a statistic holds.</summary>
    public enum StoreStatKind
    {
        Integer = 0,
        Float = 1,
    }

    /// <summary>
    /// One game statistic with its current value and the limits the store puts on it.
    /// </summary>
    /// <remarks>
    /// Values are doubles so one type serves both kinds; an <see cref="StoreStatKind.Integer"/>
    /// statistic never holds a fraction, and every 32-bit integer is exact in a double.
    /// </remarks>
    public sealed record StoreStat(
        string Id,
        string DisplayName,
        StoreStatKind Kind,
        double Value,
        double Min,
        double Max,
        double MaxChange,
        bool IncrementOnly,
        double DefaultValue,
        bool Protected);

    /// <summary>
    /// A store that has statistics beside achievements, and can reset them. Only Steam does.
    /// </summary>
    /// <remarks>
    /// Achievements and statistics are committed together, in one go: Steam stores both with one
    /// call, and splitting them would store half of what the user asked for if the second part
    /// failed.
    /// </remarks>
    public interface IStatsStoreProvider : IWritableStoreProvider
    {
        Task<IReadOnlyList<StoreStat>> GetStatsAsync(string gameKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets each listed achievement and statistic and stores them. Nothing is stored if any
        /// of them is refused. A <see cref="ProviderException"/> says which.
        /// </summary>
        Task CommitAsync(
            string gameKey,
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken = default);

        /// <summary>Resets every statistic, and the achievements too if asked. Cannot be undone.</summary>
        Task ResetAsync(string gameKey, bool includeAchievements, CancellationToken cancellationToken = default);
    }
}
