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

    public static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) == true &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static string FirstOf(JsonElement element, params string[] names)
    {
        return names
            .Select(name => GetString(element, name))
            .FirstOrDefault(value => string.IsNullOrWhiteSpace(value) == false);
    }

    public static bool GetBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) == true &&
               value.ValueKind == JsonValueKind.True;
    }

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
