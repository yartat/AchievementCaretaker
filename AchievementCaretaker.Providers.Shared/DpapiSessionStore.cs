/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace AchievementCaretaker.Providers;

/// <summary>
/// Keeps sessions on Windows, encrypted with DPAPI
/// (<see cref="ProtectedData"/>, <see cref="DataProtectionScope.CurrentUser"/>).
/// </summary>
/// <remarks>
/// The file is only readable by whoever can already act as the signed-in Windows user,
/// which is the account DPAPI itself is keyed to.
/// <para>
/// Before 2.1.0 the file was plain JSON on Windows too. <see cref="Load{T}"/> still
/// reads that and writes it back encrypted, so a sign-in made with an older version is
/// kept instead of asking the user to sign in again.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSessionStore : ISessionStore
{
    /// <inheritdoc/>
    public T? Load<T>(string path)
        where T : class
    {
        try
        {
            if (File.Exists(path) == false)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);

            // A file written before the encryption existed is plain JSON. It is told
            // apart by its first byte, not by trying to decrypt it and catching the
            // failure: a DPAPI blob starts with 0x01, never with '{'.
            if (LooksLikePlainJson(bytes) == true)
            {
                var legacy = JsonSerializer.Deserialize<T>(bytes.AsSpan(BomLength(bytes)));
                if (legacy != null)
                {
                    // Encrypted from now on, so the token does not stay readable on disk.
                    Save(path, legacy);
                }
                return legacy;
            }

            return JsonSerializer.Deserialize<T>(
                ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            // A corrupt or undecryptable file means signing in again, not
            // failing to start.
            return null;
        }
    }

    /// <inheritdoc/>
    public void Save<T>(string path, T session)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(session, new JsonSerializerOptions() { WriteIndented = true });
        File.WriteAllBytes(path, ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser));
    }

    /// <inheritdoc/>
    public void Delete(string path)
    {
        if (File.Exists(path) == true)
        {
            File.Delete(path);
        }
    }

    /// <summary>The length of a UTF-8 byte-order mark at the start, which the JSON reader does not skip.</summary>
    private static int BomLength(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

    /// <summary>
    /// True when the first byte that is not a byte-order mark or white space is
    /// an opening brace.
    /// </summary>
    private static bool LooksLikePlainJson(byte[] bytes)
    {
        var index = BomLength(bytes);
        while (index < bytes.Length && (bytes[index] == (byte)' ' || bytes[index] == (byte)'\t' ||
                                        bytes[index] == (byte)'\r' || bytes[index] == (byte)'\n'))
        {
            index++;
        }

        return index < bytes.Length && bytes[index] == (byte)'{';
    }
}
