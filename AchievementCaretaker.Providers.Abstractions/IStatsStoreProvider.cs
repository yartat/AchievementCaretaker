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
