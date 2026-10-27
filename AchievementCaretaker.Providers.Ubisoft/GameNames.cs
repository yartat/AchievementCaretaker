/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// What the games' ids are called. The ownership cache lists ids and nothing else, so the names come
/// from a list that ships with the application: <c>Data\UplayGames.txt</c>, one <c>id|name</c> per line,
/// from UbiSlot (the ids were collected by NikitaGareev).
/// </summary>
/// <remarks>
/// A list that ships with the application ages: a game released after it has no name here and is left out
/// of the library, which is why it is the list that decides what counts as a game.
/// </remarks>
public sealed partial class GameNames
{
    private static readonly Lazy<GameNames> _embedded = new(LoadEmbedded);

    private readonly Dictionary<uint, string> _names;

    private GameNames(Dictionary<uint, string> names)
    {
        _names = names;
    }

    /// <summary>The list that ships with the application.</summary>
    public static GameNames Embedded => _embedded.Value;

    /// <summary>How many games the list names.</summary>
    public int Count => _names.Count;

    /// <summary>
    /// Reads a list: <c>id|name</c> lines. Lines that are not that are skipped, and a name is tidied for
    /// display (<see cref="Tidy"/>).
    /// </summary>
    /// <param name="lines">The lines of the list.</param>
    public static GameNames Parse(IEnumerable<string> lines)
    {
        Dictionary<uint, string> names = new();
        foreach (var line in lines)
        {
            var separator = line.IndexOf('|');
            if (separator <= 0 ||
                uint.TryParse(line[..separator].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) == false)
            {
                continue;
            }

            var name = Tidy(line[(separator + 1)..]);
            if (name.Length > 0)
            {
                names[id] = name;
            }
        }

        return new GameNames(names);
    }

    /// <summary>The name of a game, or false when the list does not know it.</summary>
    /// <param name="id">The game's id.</param>
    /// <param name="name">Its name.</param>
    public bool TryGetName(uint id, out string name)
    {
        if (_names.TryGetValue(id, out var found) == true)
        {
            name = found;
            return true;
        }

        name = "";
        return false;
    }

    /// <summary>
    /// A name as it is shown: without the trademark signs, and without the note of which edition of the
    /// product the id is for (<c>(Steam)</c>, <c>(Uplay+Steam)</c>), which is not part of the game's name.
    /// </summary>
    /// <param name="name">The name as the list has it.</param>
    public static string Tidy(string name)
    {
        var tidy = name.Replace("®", "").Replace("™", "").Replace("©", "");
        return EditionNote().Replace(tidy, "").Trim();
    }

    private static GameNames LoadEmbedded()
    {
        using var stream = typeof(GameNames).Assembly.GetManifestResourceStream("UplayGames.txt")
            ?? throw new InvalidOperationException("The list of Ubisoft game names is missing from the assembly.");
        using StreamReader reader = new(stream);

        List<string> lines = new();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }

        return Parse(lines);
    }

    [GeneratedRegex(@"\s*\((?:Steam|Uplay|Uplay\+Steam|Steam Version|Steam version)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EditionNote();
}
