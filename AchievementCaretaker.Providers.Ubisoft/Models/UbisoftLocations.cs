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
using System.IO;

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>
/// Where Ubisoft Connect keeps what this provider reads. A record so that a test can point it at a
/// folder of its own instead of the machine's.
/// </summary>
/// <param name="LauncherData">
/// The launcher's per-user folder, <c>%LOCALAPPDATA%\Ubisoft Game Launcher</c>: the ownership cache is
/// under <c>cache\ownership</c> and the spool under <c>spool</c>.
/// </param>
/// <param name="AchievementArchives">
/// The folder of achievement archives, <c>%PROGRAMDATA%\Ubisoft\Ubisoft Game Launcher\cache\achievements</c>.
/// </param>
public sealed record UbisoftLocations(string LauncherData, string AchievementArchives)
{
    /// <summary>The folder with one file per user, named by the user's id, listing the ids of what they own.</summary>
    public string OwnershipDirectory => Path.Combine(LauncherData, "cache", "ownership");

    /// <summary>The launcher's catalog of every product it knows, with their names and pictures.</summary>
    public string ConfigurationFile => Path.Combine(LauncherData, "cache", "configuration", "configurations");

    /// <summary>The folder with one folder per user, each holding a <c>.spool</c> file per game.</summary>
    public string SpoolDirectory => Path.Combine(LauncherData, "spool");

    /// <summary>The locations of the current user on this computer.</summary>
    public static UbisoftLocations ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ubisoft Game Launcher"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ubisoft", "Ubisoft Game Launcher", "cache", "achievements"));
}
