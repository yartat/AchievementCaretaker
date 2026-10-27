/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services.Models;

/// <summary>What reading one game's achievements came to: the list, or why there is none.</summary>
/// <param name="Achievements">Empty when <paramref name="Failure"/> is set.</param>
/// <param name="Failure">A sentence for the user, or null when the read worked.</param>
public sealed record StoreAchievementsResult(IReadOnlyList<StoreAchievement> Achievements, string? Failure);
