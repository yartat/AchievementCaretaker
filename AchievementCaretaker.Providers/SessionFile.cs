/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Text.Json;

namespace AchievementCaretaker.Providers
{
    /// <summary>
    /// Reads and writes a provider's saved sign-in.
    /// </summary>
    /// <remarks>
    /// A session file holds a refresh token, which is a credential for the whole
    /// store account. It lives in the user's profile, is written user-only on
    /// Unix, and nothing here or in the callers logs or prints it. Both providers
    /// go through this one place so that stays true of both.
    /// </remarks>
    internal static class SessionFile
    {
        /// <summary>Returns null when there is no usable session file.</summary>
        public static T Load<T>(string path)
            where T : class
        {
            try
            {
                return File.Exists(path) == true
                    ? JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                    : null;
            }
            catch (Exception)
            {
                // A corrupt file means signing in again, not failing to start.
                return null;
            }
        }

        public static void Save<T>(string path, T session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonSerializer.Serialize(session, new JsonSerializerOptions() { WriteIndented = true }));

            // Plain `if (!OperatingSystem.IsWindows())` rather than this file's
            // usual `== false`: CA1416 only recognises the bare form as a guard.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        public static void Delete(string path)
        {
            if (File.Exists(path) == true)
            {
                File.Delete(path);
            }
        }
    }
}
