/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AchievementCaretaker.Providers
{
    /// <summary>
    /// Reads and writes a provider's saved sign-in.
    /// </summary>
    /// <remarks>
    /// A session file holds a refresh token, which is a credential for the whole
    /// store account. On Windows the serialized JSON is encrypted with DPAPI
    /// (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>)
    /// before it is written, so the file on disk is only readable by whoever can
    /// already act as the signed-in Windows user — the same account DPAPI itself
    /// is keyed to. There is no BCL equivalent on Linux or macOS (that needs
    /// libsecret or Keychain, neither of which this project depends on), so
    /// there the file stays plain JSON, written user-only
    /// (<see cref="UnixFileMode.UserRead"/> | <see cref="UnixFileMode.UserWrite"/>).
    /// Nothing here or in the callers logs or prints a token either way. Both
    /// providers go through this one place so that stays true of both.
    /// </remarks>
    internal static class SessionFile
    {
        /// <summary>Returns null when there is no usable session file.</summary>
        public static T Load<T>(string path)
            where T : class
        {
            try
            {
                if (File.Exists(path) == false)
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                if (OperatingSystem.IsWindows())
                {
                    bytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                }

                return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception)
            {
                // A corrupt or undecryptable file means signing in again, not
                // failing to start.
                return null;
            }
        }

        public static void Save<T>(string path, T session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            var bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(session, new JsonSerializerOptions() { WriteIndented = true }));

            // Plain `if (!OperatingSystem.IsWindows())` rather than this file's
            // usual `== false`: CA1416 only recognises the bare form as a guard.
            if (!OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(path, bytes);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return;
            }

            File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
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
