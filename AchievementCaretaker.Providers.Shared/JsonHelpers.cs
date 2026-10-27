/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AchievementCaretaker.Providers;

/// <summary>
/// Tolerant reads over <see cref="JsonElement"/>. Every provider talks to an
/// undocumented service whose fields come and go, so a missing or differently
/// typed field is an absent value, not an exception.
/// </summary>
public static class JsonHelpers
{
    /// <summary>Follows a path of property names down from an element.</summary>
    /// <param name="element">Where to start.</param>
    /// <param name="result">The element the path ends at; meaningless when the method returns false.</param>
    /// <param name="path">The property names, outermost first.</param>
    /// <returns>False when a step is not an object or has no such property.</returns>
    public static bool TryWalk(JsonElement element, out JsonElement result, params string[] path)
    {
        result = element;
        foreach (var name in path)
        {
            if (result.ValueKind != JsonValueKind.Object ||
                result.TryGetProperty(name, out result) == false)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A string property of an object.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string, or null when the element is not an object, has no such property, or it is not a string.</returns>
    public static string? GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) == true &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>The first of several string properties that is not blank.</summary>
    /// <param name="element">The object.</param>
    /// <param name="names">The property names, in order of preference.</param>
    /// <returns>That string, or null when none of them is set.</returns>
    public static string? FirstOf(JsonElement element, params string[] names)
    {
        return names
            .Select(name => GetString(element, name))
            .FirstOrDefault(value => string.IsNullOrWhiteSpace(value) == false);
    }

    /// <summary>A boolean property of an object.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>True only when the property is present and is <c>true</c>.</returns>
    public static bool GetBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) == true &&
               value.ValueKind == JsonValueKind.True;
    }

    /// <summary>A numeric property of an object.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The number, or null when it is absent or not a number.</returns>
    public static double? GetDouble(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) == true &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
    }

    /// <summary>
    /// ISO 8601, including the offset spelt without a colon (<c>+0000</c>) that
    /// GOG writes and .NET will not parse.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The date, or null when it is absent or cannot be read.</returns>
    public static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var text = GetString(element, name);
        if (string.IsNullOrEmpty(text) == true)
        {
            return null;
        }

        text = Regex.Replace(text, @"([+-]\d\d)(\d\d)$", "$1:$2");
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var date) == true
            ? date
            : null;
    }
}
