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
using System.IO;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.Services;

namespace AchievementCaretaker.App.Library;

/// <summary>
/// Persists the user's own like/dislike ratings.
/// </summary>
/// <remarks>
/// Stored at <c>~/.sac/ratings.json</c>, alongside the settings file and the
/// caches, rather than next to the executable: this is per-user data, and
/// the app directory is not reliably writable. See <see cref="OwnRating"/>
/// for why this is local-only and never round-trips to Steam.
/// </remarks>
internal sealed class RatingStore
{
    /// <summary>
    /// Where ratings lived before they joined the rest of the per-user
    /// files in the home directory. Read once, then moved.
    /// </summary>
    private static string LegacyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamAchievementManager",
        "ratings.json");

    private readonly RatingFile _file;

    private RatingStore(RatingFile file)
    {
        _file = file;
    }

    public static RatingStore Load()
    {
        // This runs as a field initialiser, so it can be the first thing in
        // the process to touch the home directory.
        AppSettings.EnsureMigrated();

        var path = Path.Combine(AppSettings.HomeDirectory, "ratings.json");

        // Read from wherever they are, which is the old place if the move could not be done, and
        // always saved to the new one.
        return new(RatingFile.Load(Migrate(path), path));
    }

    /// <summary>
    /// Moves a pre-home-directory ratings file across, once. Returns the file
    /// that actually holds the ratings now — still the old one if the move
    /// failed, so a locked or read-only profile loses nothing.
    /// </summary>
    private static string Migrate(string path)
    {
        try
        {
            var legacy = LegacyPath;
            if (File.Exists(path) == true || File.Exists(legacy) == false)
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }
            File.Move(legacy, path);
            return path;
        }
        catch (Exception)
        {
            return File.Exists(path) == true ? path : LegacyPath;
        }
    }

    public OwnRating Get(uint appId)
    {
        var rating = _file.Get(appId);
        return Enum.IsDefined(typeof(OwnRating), rating) == true ? (OwnRating)rating : OwnRating.None;
    }

    /// <summary>Returns false if the change could not be persisted.</summary>
    public bool Set(uint appId, OwnRating rating) => _file.Set(appId, (int)rating);
}
