/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services.Models;

/// <summary>What reading one game's statistics came to.</summary>
/// <param name="Stats">Empty when <paramref name="Failure"/> is set, or when the provider has no statistics.</param>
/// <param name="Failure">A sentence for the user, or null when the read worked.</param>
public sealed record StoreStatsResult(IReadOnlyList<StoreStat> Stats, string Failure);
