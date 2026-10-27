/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 * and on galaxy-integration-uplay by melcom (MIT)
 * https://github.com/melcom-creations/galaxy-integration-uplay
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * configuration catalog, the achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// The launcher's configuration catalog, <c>cache\configuration\configurations</c>: a description of every
/// product the launcher knows, with the names and the pictures the launcher shows.
/// </summary>
/// <remarks>
/// <para>
/// Protocol buffers once more: repeated records (field 1), each with the product's id and the id it is
/// launched by (fields 1 and 2, varints) and a block of YAML (field 3). On the machine this was written on
/// that is 1,348 records, 1,135 of them with YAML, of which a game is one that has a <c>start_game</c>
/// section; the rest are add-ons, tools and internal stubs.
/// </para>
/// <para>
/// A name is not always in the block as such. Often <c>root.name</c> is the <em>key</em> of a line in
/// <c>localizations.default</c> (<c>l1</c>, <c>NAME</c>), and the same goes for the picture names, so both
/// are looked up there first. Stubs that link a Steam purchase to the account carry a placeholder
/// (<c>name</c>, <c>l1</c> with no such key) and have no name here.
/// </para>
/// <para>
/// Only the few fields wanted are read, by indentation, not with a YAML library: the blocks hold tabs and
/// things a strict parser refuses, and nothing else in them matters.
/// </para>
/// </remarks>
public static class GameConfigurations
{
    private static readonly HashSet<string> _placeholderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "gamename", "l1", "name", "ubisoft game",
    };

    /// <summary>Every product in a catalog file that has a block, in the order of the file. Empty for a file that is not one.</summary>
    /// <param name="file">The whole file.</param>
    public static IReadOnlyList<GameConfiguration> Read(ReadOnlySpan<byte> file)
    {
        List<GameConfiguration> products = new();
        var position = 0;
        try
        {
            while (position < file.Length)
            {
                var tag = Protobuf.ReadVarint(file, ref position);
                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);

                if (number == 1 && wireType == 2)
                {
                    var length = Protobuf.ReadVarint(file, ref position);
                    if (length > (ulong)(file.Length - position))
                    {
                        break;
                    }

                    var product = ReadRecord(file.Slice(position, (int)length));
                    if (product != null)
                    {
                        products.Add(product);
                    }
                    position += (int)length;
                }
                else
                {
                    Protobuf.Skip(file, ref position, wireType);
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            // What was read before the file stopped making sense still counts.
        }

        return products;
    }

    /// <summary>The catalog of the launcher in <paramref name="locations"/>, or an empty list when there is none or it cannot be read.</summary>
    /// <param name="locations">Where the launcher keeps its files.</param>
    public static IReadOnlyList<GameConfiguration> ReadFile(UbisoftLocations locations)
    {
        try
        {
            return File.Exists(locations.ConfigurationFile) == true
                ? Read(File.ReadAllBytes(locations.ConfigurationFile))
                : Array.Empty<GameConfiguration>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<GameConfiguration>();
        }
    }

    /// <summary>
    /// What one block of YAML says: the product's name, whether it can be played, and its pictures. Public so that
    /// the shapes of block seen in the wild can be tested without building a catalog around each.
    /// </summary>
    /// <param name="productId">The product's id.</param>
    /// <param name="launchId">The id it is launched by.</param>
    /// <param name="yaml">The block.</param>
    public static GameConfiguration Parse(uint productId, uint launchId, string yaml)
    {
        Dictionary<string, string> root = new();
        HashSet<string> rootSections = new();
        Dictionary<string, string> installer = new();
        Dictionary<string, string> uplay = new();
        Dictionary<string, string> localization = new();

        var section = "";
        var subsection = "";
        foreach (var rawLine in yaml.Replace("\t", "  ").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var content = line.TrimStart(' ');
            if (content.Length == 0 || content[0] == '#')
            {
                continue;
            }

            var indent = line.Length - content.Length;
            if (SplitKeyValue(content) is not var (key, value))
            {
                continue;
            }

            if (indent == 0)
            {
                section = key;
                subsection = "";
            }
            else if (section == "root" && indent == 2)
            {
                subsection = key;
                if (value.Length == 0)
                {
                    rootSections.Add(key);
                }
                else
                {
                    root[key] = value;
                }
            }
            else if (section == "root" && indent == 4 && subsection == "installer" && value.Length > 0)
            {
                installer[key] = value;
            }
            else if (section == "root" && indent == 4 && subsection == "uplay" && value.Length > 0)
            {
                uplay[key] = value;
            }
            else if (section == "localizations" && indent == 2)
            {
                subsection = key;
            }
            else if (section == "localizations" && indent == 4 && subsection == "default" && value.Length > 0)
            {
                localization[key] = value;
            }
        }

        string Lookup(string value) => localization.TryGetValue(value, out var localized) ? localized : value;

        var name = root.TryGetValue("name", out var rootName) ? Lookup(rootName) : "";
        if (_placeholderNames.Contains(name) == true)
        {
            name = installer.TryGetValue("game_identifier", out var identifier) ? Lookup(identifier) : "";
        }
        if (_placeholderNames.Contains(name) == true)
        {
            name = localization.TryGetValue("GAMENAME", out var gameName) ? gameName : "";
        }

        return new GameConfiguration(
            productId,
            launchId,
            _placeholderNames.Contains(name) == true ? null : GameNames.Tidy(name),
            rootSections.Contains("start_game"),
            Picture(root, "background_image", Lookup),
            Picture(root, "thumb_image", Lookup),
            uplay.TryGetValue("achievements", out var archive) && archive.Length > 0 ? Lookup(archive) : null,
            uplay.TryGetValue("achievements_sync_id", out var syncId) && syncId.Length > 0 ? syncId : null);
    }

    private static GameConfiguration? ReadRecord(ReadOnlySpan<byte> record)
    {
        ulong? productId = null;
        ulong? launchId = null;
        string? yaml = null;

        var position = 0;
        while (position < record.Length)
        {
            var tag = Protobuf.ReadVarint(record, ref position);
            var number = (int)(tag >> 3);
            var wireType = (int)(tag & 7);

            if (wireType == 0 && number is 1 or 2)
            {
                var value = Protobuf.ReadVarint(record, ref position);
                if (number == 1)
                {
                    productId = value;
                }
                else
                {
                    launchId = value;
                }
            }
            else if (wireType == 2 && number == 3)
            {
                var length = Protobuf.ReadVarint(record, ref position);
                if (length > (ulong)(record.Length - position))
                {
                    throw new EndOfStreamException("A field runs past the end of the record.");
                }
                yaml = Encoding.UTF8.GetString(record.Slice(position, (int)length));
                position += (int)length;
            }
            else
            {
                Protobuf.Skip(record, ref position, wireType);
            }
        }

        if (productId is not { } id || id == 0 || id > uint.MaxValue || yaml == null)
        {
            return null;
        }

        var product = (uint)id;
        var launch = launchId is > 0 and <= uint.MaxValue ? (uint)launchId.Value : product;
        return Parse(product, launch, yaml);
    }

    private static string? Picture(Dictionary<string, string> root, string key, Func<string, string> lookup) =>
        root.TryGetValue(key, out var value) && lookup(value) is { Length: > 0 } file && file.Contains('.') == true
            ? file
            : null;

    /// <summary><c>key: value</c> with the quotes off the value, or null for a line that is not that.</summary>
    private static (string Key, string Value)? SplitKeyValue(string content)
    {
        var colon = content.IndexOf(':');
        if (colon <= 0 || content[0] == '-')
        {
            return null;
        }

        var value = content[(colon + 1)..].Trim();
        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
        {
            value = value[1..^1];
        }
        return (content[..colon].Trim(), value);
    }
}
