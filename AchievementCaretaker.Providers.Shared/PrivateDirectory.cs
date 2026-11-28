/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;

namespace AchievementCaretaker.Providers;

/// <summary>Makes the folders that hold the user's own data.</summary>
public static class PrivateDirectory
{
    /// <summary>
    /// On Unix, closes a folder that is there to everyone but its owner. For the application's own
    /// folder only, never one the user chose. A folder that is someone else's is left as it is.
    /// </summary>
    /// <param name="path">The folder.</param>
    public static void Tighten(string path)
    {
        if (OperatingSystem.IsWindows() || Directory.Exists(path) == false)
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }


    /// <summary>
    /// Creates the folder if it is not there, on Unix readable by its owner only. One that exists
    /// is left as it is: it may be a place the user chose.
    /// </summary>
    /// <param name="path">The folder.</param>
    public static void Create(string path)
    {
        if (Directory.Exists(path) == true)
        {
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            // The user profile is already the user's own.
            Directory.CreateDirectory(path);
            return;
        }
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
