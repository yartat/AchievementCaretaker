/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>The kind of number a statistic holds.</summary>
public enum StoreStatKind
{
    /// <summary>A whole number (a 32-bit integer).</summary>
    Integer = 0,

    /// <summary>A number with a fraction (a 32-bit float).</summary>
    Float = 1,
}
