/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Globalization;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services;

/// <summary>
/// Reads what the user typed into a statistic's value box and says whether the store would
/// take it. The window shows the error beside the box and sends nothing that failed.
/// </summary>
public static class StatValueParser
{
    /// <param name="stat">The statistic as the store reported it; <see cref="StoreStat.Value"/> is what it holds now.</param>
    /// <param name="text">What was typed.</param>
    /// <param name="culture">How numbers are written; the user's own by default.</param>
    /// <param name="value">The number, when there is no <paramref name="error"/>.</param>
    /// <param name="error">Why it is refused, or null.</param>
    public static bool TryParse(StoreStat stat, string text, IFormatProvider culture, out double value, out string error)
    {
        culture ??= CultureInfo.CurrentCulture;
        value = 0;

        if (stat.Kind == StoreStatKind.Integer)
        {
            if (int.TryParse(text, NumberStyles.Integer, culture, out var whole) == false)
            {
                error = "Invalid value";
                return false;
            }
            value = whole;
        }
        else
        {
            if (float.TryParse(text, NumberStyles.Float, culture, out var fraction) == false || float.IsFinite(fraction) == false)
            {
                error = "Invalid value";
                return false;
            }
            value = fraction;
        }

        if (stat.Protected == true && value != stat.Value)
        {
            error = "Stat is protected! -- you can't modify it";
            return false;
        }

        if (value < stat.Min || value > stat.Max)
        {
            error = string.Create(culture, $"Value must be between {stat.Min} and {stat.Max}");
            return false;
        }

        error = null;
        return true;
    }
}
