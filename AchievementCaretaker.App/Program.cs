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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AchievementCaretaker.Providers.Steam;
using Avalonia;

namespace AchievementCaretaker.App;

/// <summary>
/// The single entry point for both of the application's windows.
/// </summary>
/// <remarks>
/// Steam Achievement Manager shipped two executables because
/// <c>ISteamUserStats</c> is scoped to the one application id the process
/// was initialised with: a process that asked Steam for the library cannot
/// also ask it for one game's achievements. Caretaker keeps that rule and
/// drops the second executable instead - one binary, launched with no
/// arguments for the library window and re-launched with an application id
/// for the achievement editor.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Reads the application id the achievement editor was asked to open.
    /// No arguments means the library window, which initialises Steam with
    /// application id 0.
    /// </summary>
    internal static bool TryParseAppId(string[] args, out long appId)
    {
        if (args == null || args.Length == 0)
        {
            appId = 0;
            return true;
        }
        return long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out appId);
    }

    /// <summary>
    /// The path of the running executable. <see cref="Environment.ProcessPath"/>
    /// is the apphost; the fallback covers a host that does not report one.
    /// </summary>
    internal static string GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) == false)
        {
            return path;
        }
        // The apphost has no extension outside Windows.
        var name = OperatingSystem.IsWindows() == true
            ? "AchievementCaretaker.exe"
            : "AchievementCaretaker";
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    /// <summary>
    /// Both sides are normalised before comparing: on .NET
    /// <see cref="AppContext.BaseDirectory"/> ends with a directory
    /// separator and the registry's install path does not, so a raw string
    /// comparison silently never matches.
    /// </summary>
    internal static bool IsRunningFromSteamDirectory()
    {
        var installPath = SteamApi.Steam.GetInstallPath();
        if (string.IsNullOrEmpty(installPath) == true)
        {
            return false;
        }
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            StringComparison.OrdinalIgnoreCase);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        // The hidden helper that talks to Steam for one game: no window, no Avalonia.
        if (args.Length == 2 && args[0] == SteamWorkerProcessLauncher.WorkerSwitch)
        {
            return SteamWorkerEntry.RunAsync(args[1]).GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
