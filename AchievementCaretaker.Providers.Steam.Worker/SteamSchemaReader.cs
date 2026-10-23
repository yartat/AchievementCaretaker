/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AchievementCaretaker.Providers;
using AchievementCaretaker.SteamApi;
using APITypes = AchievementCaretaker.SteamApi.Types;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// Reads the achievements and statistics out of <c>UserGameStatsSchema_&lt;appid&gt;.bin</c>. It handles both
/// shapes of the file (the newer one where <c>type</c> names the kind of stat, the older one
/// with a number); the statistics themselves are not read here.
/// </summary>
public static class SteamSchemaReader
{
    public static IReadOnlyList<SteamAchievementDefinition> ReadAchievements(KeyValue root, long appId, string language)
    {
        List<SteamAchievementDefinition> result = new();
        var stats = root[appId.ToString(CultureInfo.InvariantCulture)]["stats"];
        if (stats.Valid == false || stats.Children == null)
        {
            return result;
        }

        foreach (var stat in stats.Children)
        {
            if (stat.Valid == false)
            {
                continue;
            }

            var type = ReadType(stat);
            if (type != APITypes.UserStatType.Achievements && type != APITypes.UserStatType.GroupAchievements)
            {
                continue;
            }
            if (stat.Children == null)
            {
                continue;
            }

            foreach (var bits in stat.Children.Where(
                b => string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
            {
                if (bits.Valid == false || bits.Children == null)
                {
                    continue;
                }

                foreach (var bit in bits.Children)
                {
                    var id = bit["name"].AsString("");
                    result.Add(new SteamAchievementDefinition(
                        id,
                        GetLocalizedString(bit["display"]["name"], language, id),
                        GetLocalizedString(bit["display"]["desc"], language, ""),
                        bit["display"]["icon"].AsString(""),
                        bit["display"]["icon_gray"].AsString(""),
                        bit["display"]["hidden"].AsBoolean(false),
                        bit["permission"].AsInteger(0)));
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<SteamStatDefinition> ReadStats(KeyValue root, long appId, string language)
    {
        List<SteamStatDefinition> result = new();
        var stats = root[appId.ToString(CultureInfo.InvariantCulture)]["stats"];
        if (stats.Valid == false || stats.Children == null)
        {
            return result;
        }

        foreach (var stat in stats.Children)
        {
            if (stat.Valid == false)
            {
                continue;
            }

            var id = stat["name"].AsString("");
            switch (ReadType(stat))
            {
                case APITypes.UserStatType.Integer:
                {
                    result.Add(new SteamStatDefinition(
                        id,
                        GetLocalizedString(stat["display"]["name"], language, id),
                        StoreStatKind.Integer,
                        stat["min"].AsInteger(int.MinValue),
                        stat["max"].AsInteger(int.MaxValue),
                        stat["maxchange"].AsInteger(0),
                        stat["incrementonly"].AsBoolean(false),
                        stat["default"].AsInteger(0),
                        stat["permission"].AsInteger(0)));
                    break;
                }

                case APITypes.UserStatType.Float:
                case APITypes.UserStatType.AverageRate:
                {
                    result.Add(new SteamStatDefinition(
                        id,
                        GetLocalizedString(stat["display"]["name"], language, id),
                        StoreStatKind.Float,
                        stat["min"].AsFloat(float.MinValue),
                        stat["max"].AsFloat(float.MaxValue),
                        stat["maxchange"].AsFloat(0.0f),
                        stat["incrementonly"].AsBoolean(false),
                        stat["default"].AsFloat(0.0f),
                        stat["permission"].AsInteger(0)));
                    break;
                }
            }
        }
        return result;
    }

    private static APITypes.UserStatType ReadType(KeyValue stat)
    {
        APITypes.UserStatType type;

        // schema in the new format?
        var typeNode = stat["type"];
        if (typeNode.Valid == true && typeNode.Type == KeyValueType.String &&
            Enum.TryParse((string)typeNode.Value, true, out type) == true)
        {
            return type;
        }

        // schema in the old format?
        var typeIntNode = stat["type_int"];
        var rawType = typeIntNode.Valid == true ? typeIntNode.AsInteger(0) : typeNode.AsInteger(0);
        return (APITypes.UserStatType)rawType;
    }

    private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
    {
        var name = kv[language].AsString("");
        if (string.IsNullOrEmpty(name) == false)
        {
            return name;
        }

        if (language != "english")
        {
            name = kv["english"].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }
        }

        name = kv.AsString("");
        return string.IsNullOrEmpty(name) == false ? name : defaultValue;
    }
}
