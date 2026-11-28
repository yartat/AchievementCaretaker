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
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Services;

/// <summary>The file of the user's own ratings: an app id and a number for each.</summary>
/// <remarks>
/// These cannot be rebuilt from anywhere, so a file that cannot be read is never simply
/// replaced: it is copied to <c>.corrupt-&lt;time&gt;</c> first, and when even that fails (the
/// file is locked), nothing is saved until a later load succeeds.
/// </remarks>
public sealed class RatingFile
{
    private readonly string _path;
    private readonly Dictionary<uint, int> _ratings;
    private readonly bool _readOnly;

    private RatingFile(string path, Dictionary<uint, int> ratings, bool readOnly)
    {
        _path = path;
        _ratings = ratings;
        _readOnly = readOnly;
    }

    /// <summary>Reads the file; a missing or unreadable one gives no ratings.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The ratings.</returns>
    public static RatingFile Load(string path) => Load(path, path);

    /// <summary>Reads one file and saves to another.</summary>
    /// <param name="readFrom">Where the ratings are now.</param>
    /// <param name="saveTo">Where changes are written.</param>
    /// <returns>The ratings.</returns>
    public static RatingFile Load(string readFrom, string saveTo)
    {
        Dictionary<uint, int> ratings = new();
        if (File.Exists(readFrom) == false)
        {
            return new(saveTo, ratings, false);
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(readFrom));
            if (parsed != null)
            {
                foreach (var kv in parsed)
                {
                    if (uint.TryParse(kv.Key, out var id) == true)
                    {
                        ratings[id] = kv.Value;
                    }
                }
            }
            return new(saveTo, ratings, false);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
        {
            return new(saveTo, ratings, KeepAside(readFrom) == false);
        }
    }

    private static bool KeepAside(string path)
    {
        try
        {
            // Named by what it holds, so the same damage is kept once however often it is read.
            var content = File.ReadAllBytes(path);
            var copy = path + ".corrupt-" + Convert.ToHexString(SHA256.HashData(content))[..8];
            if (File.Exists(copy) == false)
            {
                File.WriteAllBytes(copy, content);
            }
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The rating of a game, 0 when there is none.</summary>
    /// <param name="appId">The game.</param>
    /// <returns>The stored number.</returns>
    public int Get(uint appId) => _ratings.TryGetValue(appId, out var rating) == true ? rating : 0;

    /// <summary>Stores a rating; 0 removes it.</summary>
    /// <param name="appId">The game.</param>
    /// <param name="rating">The number to keep.</param>
    /// <returns>False if it could not be saved.</returns>
    public bool Set(uint appId, int rating)
    {
        if (_readOnly == true)
        {
            return false;
        }
        if (rating == 0)
        {
            _ratings.Remove(appId);
        }
        else
        {
            _ratings[appId] = rating;
        }
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory) == false)
            {
                Directory.CreateDirectory(directory);
            }
            Dictionary<string, int> payload = new();
            foreach (var kv in _ratings)
            {
                payload[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
            }
            AtomicFile.WriteAllBytes(
                _path,
                JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions() { WriteIndented = true }));
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }
}
