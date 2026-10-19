/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace AchievementCaretaker.Providers
{
    /// <summary>
    /// Keeps sessions as plain JSON, readable and writable by the user only
    /// (<see cref="UnixFileMode.UserRead"/> | <see cref="UnixFileMode.UserWrite"/>).
    /// </summary>
    /// <remarks>
    /// For Linux and macOS, where there is no BCL equivalent of DPAPI: that needs libsecret or
    /// Keychain, neither of which this project depends on.
    /// </remarks>
    [UnsupportedOSPlatform("windows")]
    public sealed class PlainSessionStore : ISessionStore
    {
        public T Load<T>(string path)
            where T : class
        {
            try
            {
                return File.Exists(path) == true ? JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) : null;
            }
            catch (Exception)
            {
                // A corrupt file means signing in again, not failing to start.
                return null;
            }
        }

        public void Save<T>(string path, T session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            File.WriteAllBytes(
                path,
                JsonSerializer.SerializeToUtf8Bytes(session, new JsonSerializerOptions() { WriteIndented = true }));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        public void Delete(string path)
        {
            if (File.Exists(path) == true)
            {
                File.Delete(path);
            }
        }
    }
}
