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
using System.Globalization;
using System.IO;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// Tells Steam which app this process is, the way Steamworks programs do outside Steam: a
/// <c>steam_appid.txt</c> in the working directory.
/// </summary>
/// <remarks>
/// <para>
/// The <c>SteamAppId</c> environment variable is the other way, and Steam on Windows honours it.
/// Steam on Linux does not: with only the variable set, <c>ISteamUtils::GetAppId</c> answers 0 and
/// the client refuses to be initialised as the app ("appID mismatch"). The file works on both, so
/// it is written unconditionally and nothing asks which system this is.
/// </para>
/// <para>
/// The file lives in a folder of its own, made for this process and removed with it, and the
/// working directory is moved there while it is in use. Two helpers for two games therefore do
/// not share a file, and nothing is written next to the executable.
/// </para>
/// </remarks>
public sealed class SteamAppIdFile : IDisposable
{
    private readonly string _folder;
    private readonly string _previous;
    private bool _disposed;

    private SteamAppIdFile(string folder, string previous)
    {
        _folder = folder;
        _previous = previous;
    }

    /// <summary>The folder that holds the file, which is the working directory until this is disposed.</summary>
    public string Folder => _folder;

    /// <summary>Writes the file for <paramref name="appId"/> and makes its folder the working directory.</summary>
    public static SteamAppIdFile Create(long appId)
    {
        var previous = Directory.GetCurrentDirectory();
        var folder = Path.Combine(Path.GetTempPath(), "sac-steam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "steam_appid.txt"), appId.ToString(CultureInfo.InvariantCulture));
            Directory.SetCurrentDirectory(folder);
        }
        catch
        {
            Directory.Delete(folder, true);
            throw;
        }
        return new SteamAppIdFile(folder, previous);
    }

    /// <summary>Restores the working directory and removes the folder and the file in it.</summary>
    public void Dispose()
    {
        if (_disposed == true)
        {
            return;
        }
        _disposed = true;

        // A process cannot remove the folder it is working in on Windows: leave it first.
        Directory.SetCurrentDirectory(_previous);
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // Someone else has it open for a moment; it is a temporary folder and holds one line.
        }
    }
}
