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
using System.IO.Compression;
using System.Linq;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// A game's achievement archive: a zip the launcher downloads to its cache, named
/// <c>&lt;game id&gt;_&lt;hash&gt;.zip</c>, with the achievements' names and descriptions in every
/// language (<c>en-US_loc.txt</c>) and their icons (<c>&lt;achievement id&gt;.png</c>).
/// </summary>
/// <remarks>
/// <para>
/// The launcher fetches an archive when it needs one, so most games have none on a given computer: one in
/// 241 files of the cache on the machine this was written on. A game without an archive has no
/// achievement list to show here, whatever the spool says.
/// </para>
/// <para>
/// A localization line is the id, the name and the description, separated by tabs. The archive also has an
/// <c>achievements.dat</c> whose records repeat the ids; what else is in it is not known, and nothing here
/// uses it.
/// </para>
/// </remarks>
public sealed class AchievementArchive : IDisposable
{
    private const string LocalizationSuffix = "_loc.txt";
    private const string DefaultLocale = "en-US";

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _localizations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, ZipArchiveEntry> _icons = new();

    private AchievementArchive(ZipArchive zip, string path)
    {
        _zip = zip;
        Path = path;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith(LocalizationSuffix, StringComparison.OrdinalIgnoreCase) == true)
            {
                _localizations[name[..^LocalizationSuffix.Length]] = entry;
            }
            else if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true &&
                     uint.TryParse(name[..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var id) == true)
            {
                _icons[id] = entry;
            }
        }
    }

    /// <summary>The file the archive was opened from.</summary>
    public string Path { get; }

    /// <summary>The languages the archive has, as the launcher names them (<c>en-US</c>).</summary>
    public IReadOnlyCollection<string> Locales => _localizations.Keys;

    /// <summary>
    /// The newest archive for a game in <paramref name="directory"/> that can be opened, or null. A game
    /// has one at a time, but the name carries a hash that changes with the contents, so the file is found
    /// by its id prefix.
    /// </summary>
    /// <param name="directory">The launcher's folder of achievement archives.</param>
    /// <param name="gameId">The game.</param>
    public static AchievementArchive? Find(string directory, uint gameId)
    {
        if (Directory.Exists(directory) == false)
        {
            return null;
        }

        var prefix = gameId.ToString(CultureInfo.InvariantCulture) + "_";
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory
                .EnumerateFiles(directory, prefix + "*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var archive = TryOpen(candidate);
            if (archive != null)
            {
                return archive;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens an archive, or returns null when the file is not a zip or cannot be read (the launcher may be
    /// writing it).
    /// </summary>
    /// <param name="path">The file.</param>
    public static AchievementArchive? TryOpen(string path)
    {
        try
        {
            return new AchievementArchive(ZipFile.OpenRead(path), path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Which of the archive's languages answers a request for <paramref name="locale"/>: the same one, else
    /// one in the same language (<c>ru</c> for <c>ru-RU</c>), else English, else the first. Null when the
    /// archive has no localization at all.
    /// </summary>
    /// <param name="locale">What was asked for: <c>en</c>, <c>ru-RU</c>.</param>
    public string? ResolveLocale(string locale)
    {
        if (_localizations.Count == 0)
        {
            return null;
        }

        if (_localizations.ContainsKey(locale) == true)
        {
            return _localizations.Keys.First(key => string.Equals(key, locale, StringComparison.OrdinalIgnoreCase));
        }

        var language = locale.Split('-')[0];
        var sameLanguage = _localizations.Keys
            .FirstOrDefault(key => string.Equals(key.Split('-')[0], language, StringComparison.OrdinalIgnoreCase));
        if (sameLanguage != null)
        {
            return sameLanguage;
        }

        return _localizations.ContainsKey(DefaultLocale) == true
            ? DefaultLocale
            : _localizations.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).First();
    }

    /// <summary>The archive's achievements in a language, by id. Empty when the archive has no localization.</summary>
    /// <param name="locale">What to ask for; see <see cref="ResolveLocale"/>.</param>
    public IReadOnlyList<ArchiveAchievement> ReadAchievements(string locale)
    {
        var resolved = ResolveLocale(locale);
        if (resolved == null)
        {
            return Array.Empty<ArchiveAchievement>();
        }

        using StreamReader reader = new(_localizations[resolved].Open());
        Dictionary<uint, ArchiveAchievement> byId = new();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2 || uint.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) == false)
            {
                continue;
            }

            var description = parts.Length >= 3 ? string.Join('\t', parts.Skip(2)).Trim() : "";
            byId[id] = new ArchiveAchievement(id, parts[1].Trim(), description);
        }

        return byId.Values.OrderBy(achievement => achievement.Id).ToList();
    }

    /// <summary>Whether the archive has an icon for an achievement.</summary>
    /// <param name="achievementId">The achievement.</param>
    public bool HasIcon(uint achievementId) => _icons.ContainsKey(achievementId);

    /// <summary>Copies an achievement's icon (a PNG) out of the archive. The achievement must have one.</summary>
    /// <param name="achievementId">The achievement.</param>
    /// <param name="destination">Where to write the image.</param>
    public void CopyIcon(uint achievementId, Stream destination)
    {
        using var source = _icons[achievementId].Open();
        source.CopyTo(destination);
    }

    /// <summary>Closes the archive.</summary>
    public void Dispose() => _zip.Dispose();
}
