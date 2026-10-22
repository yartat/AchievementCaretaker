/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Providers.Models;

/// <summary>
/// One achievement: the definition merged with the signed-in player's state.
/// </summary>
/// <remarks>
/// <para>
/// Fields a store does not have are null rather than zero, because unknown and
/// zero are different answers. Epic has XP and progress; GOG has neither.
/// </para>
/// <para>
/// <see cref="Protected"/> marks an achievement the store will not let anyone set (Steam's
/// permission bits); the window shows it but does not offer to change it.
/// </para>
/// <para>
/// <see cref="Unlocked"/> false with a null <see cref="UnlockDate"/> means "not
/// earned". A game the store has no record for comes back with every
/// achievement in that state, which cannot be told from a game played without
/// earning anything; neither store says which.
/// </para>
/// </remarks>
/// <param name="Id">The achievement's identifier, which is what it is set by.</param>
/// <param name="DisplayName">The name to show.</param>
/// <param name="Description">What it takes to earn it; empty for a hidden one the store does not describe.</param>
/// <param name="IconUrl">The address of its icon, or null when it has none.</param>
/// <param name="Hidden">The store hides the description until it is earned.</param>
/// <param name="XP">Experience points it is worth, or null when the store has no such thing.</param>
/// <param name="RarityPercent">The share of players who have it, or null when the store does not say.</param>
/// <param name="Unlocked">The player has earned it.</param>
/// <param name="Progress">How far towards it the player is (0 to 1), or null when the store does not track that.</param>
/// <param name="UnlockDate">When it was earned, or null when it is not earned or the store does not say when.</param>
/// <param name="Protected">The store will not let it be changed.</param>
public sealed record StoreAchievement(
    string Id,
    string DisplayName,
    string Description,
    string IconUrl,
    bool Hidden,
    int? XP,
    double? RarityPercent,
    bool Unlocked,
    double? Progress,
    DateTimeOffset? UnlockDate,
    bool Protected = false);
