/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>
/// One game statistic with its current value and the limits the store puts on it.
/// </summary>
/// <remarks>
/// Values are doubles so one type serves both kinds; an <see cref="StoreStatKind.Integer"/>
/// statistic never holds a fraction, and every 32-bit integer is exact in a double.
/// </remarks>
/// <param name="Id">The statistic's name, which is what it is set by.</param>
/// <param name="DisplayName">The name to show; empty when the schema gives none.</param>
/// <param name="Kind">Whether the value is a whole number or has a fraction.</param>
/// <param name="Value">What the statistic holds now.</param>
/// <param name="Min">The lowest value the store accepts.</param>
/// <param name="Max">The highest value the store accepts.</param>
/// <param name="MaxChange">The most the value may change in one go; 0 when unlimited.</param>
/// <param name="IncrementOnly">The value can only grow.</param>
/// <param name="DefaultValue">What the statistic starts at and returns to when reset.</param>
/// <param name="Protected">The store will not take a change to it from a client.</param>
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
