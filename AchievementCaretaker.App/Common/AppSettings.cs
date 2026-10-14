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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AchievementCaretaker.App.Common
{
    /// <summary>
    /// User-configurable cache locations.
    /// </summary>
    /// <remarks>
    /// The settings file itself lives at a fixed <c>~/.sac/settings.json</c>.
    /// It cannot live inside the configurable database directory — that is
    /// where the path to read would have to come from.
    /// </remarks>
    internal sealed class AppSettings
    {
        public string DatabasePath { get; set; }
        public string IconCachePath { get; set; }

        /// <summary>
        /// Serialized by name rather than by number so the settings file stays
        /// readable and reordering the enum cannot silently change what an
        /// existing file means. An absent value is <see cref="AppTheme.System"/>,
        /// which is what the application did before this was configurable.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
        public AppTheme Theme { get; set; }

        [JsonIgnore]
        public static string HomeDirectory => InUserProfile(".sac");

        /// <summary>
        /// Where Steam Achievement Manager kept the same files, and where every
        /// Caretaker build before 1.0 did too. Moved across once; see
        /// <see cref="EnsureMigrated"/>.
        /// </summary>
        [JsonIgnore]
        public static string LegacyHomeDirectory => InUserProfile(".sam");

        private static string InUserProfile(string name)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                name);
        }

        [JsonIgnore]
        public static string SettingsPath => Path.Combine(HomeDirectory, "settings.json");

        public static string DefaultDatabasePath => Path.Combine(HomeDirectory, "games.db");

        public static string DefaultIconCachePath => Path.Combine(HomeDirectory, "icons");

        /// <summary>
        /// Moves the whole directory over from <see cref="LegacyHomeDirectory"/>
        /// the first time, so an existing Steam Achievement Manager install
        /// keeps its caches and - the part that cannot be rebuilt - the user's
        /// own like/dislike ratings.
        /// </summary>
        /// <remarks>
        /// Called from every entry point that reads one of these files rather
        /// than from a single place, because the order they run in is not
        /// guaranteed: <c>RatingStore.Load</c> is a field initialiser and so
        /// runs before the constructor that calls <see cref="Load"/>.
        /// Re-running it costs two directory probes and does nothing.
        /// </remarks>
        public static void EnsureMigrated()
        {
            try
            {
                if (Directory.Exists(HomeDirectory) == true ||
                    Directory.Exists(LegacyHomeDirectory) == false)
                {
                    return;
                }

                Directory.Move(LegacyHomeDirectory, HomeDirectory);
                RepointMovedPaths();
            }
            catch (Exception)
            {
                // A locked or read-only profile carries on with defaults rather
                // than failing to start. The caches rebuild themselves; only the
                // ratings would be missed, and they are still where they were.
            }
        }

        /// <summary>
        /// The database and icon cache are stored as absolute paths, so any that
        /// pointed inside the directory just moved have to follow it. Paths the
        /// user pointed somewhere else are left alone.
        /// </summary>
        private static void RepointMovedPaths()
        {
            if (File.Exists(SettingsPath) == false)
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (settings == null)
            {
                return;
            }

            var database = Repoint(settings.DatabasePath);
            var icons = Repoint(settings.IconCachePath);
            if (database == settings.DatabasePath && icons == settings.IconCachePath)
            {
                return;
            }

            settings.DatabasePath = database;
            settings.IconCachePath = icons;
            settings.Save();
        }

        private static string Repoint(string path)
        {
            var prefix = LegacyHomeDirectory + Path.DirectorySeparatorChar;
            return string.IsNullOrWhiteSpace(path) == false &&
                   path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
                ? Path.Combine(HomeDirectory, path[prefix.Length..])
                : path;
        }

        public static AppSettings Load()
        {
            EnsureMigrated();

            AppSettings settings = null;
            try
            {
                if (File.Exists(SettingsPath) == true)
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                }
            }
            catch (Exception)
            {
                // A corrupt settings file falls back to defaults rather than
                // blocking startup.
            }

            settings ??= new();
            if (string.IsNullOrWhiteSpace(settings.DatabasePath) == true)
            {
                settings.DatabasePath = DefaultDatabasePath;
            }
            if (string.IsNullOrWhiteSpace(settings.IconCachePath) == true)
            {
                settings.IconCachePath = DefaultIconCachePath;
            }
            return settings;
        }

        /// <summary>Returns false if the settings could not be written.</summary>
        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(HomeDirectory);
                File.WriteAllText(
                    SettingsPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions() { WriteIndented = true }));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public AppSettings Clone()
        {
            return new()
            {
                DatabasePath = this.DatabasePath,
                IconCachePath = this.IconCachePath,
                Theme = this.Theme,
            };
        }
    }
}
