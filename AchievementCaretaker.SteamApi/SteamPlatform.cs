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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AchievementCaretaker.SteamApi;

/// <summary>
/// Locates the Steam installation and its client library on the current OS.
/// </summary>
/// <remarks>
/// <para>
/// This application loads Steam's <em>native</em> client library into its own process, so
/// the process architecture and the library architecture must match. Valve
/// ships that library for Windows x86/x64, Linux x86/x64 and macOS x64 only
/// — there is no ARM build of the Steam client on any platform. An ARM64
/// build therefore cannot work natively, however well the managed
/// code is written; on those machines run the x64 build under the OS's
/// emulation layer (Windows on Arm, or Rosetta 2 on Apple Silicon).
/// <see cref="IsArchitectureSupported"/> reports this rather than letting it
/// surface as a confusing "Steam is not running".
/// </para>
/// <para>
/// The Windows paths are verified. The Linux and macOS candidate lists are
/// written from Steam's documented layouts and have <em>not</em> been
/// exercised on those platforms — treat them as a starting point.
/// </para>
/// </remarks>
public static class SteamPlatform
{
    /// <summary>
    /// Whether the compiler that built the Steam client lists the overloads of a virtual function
    /// in reverse declaration order, which is what MSVC does and GCC and Clang do not.
    /// </summary>
    /// <remarks>
    /// <c>ISteamUserStats</c> declares <c>GetStat(int*)</c> before <c>GetStat(float*)</c> (and the same
    /// for <c>SetStat</c>). The vtable structs here are laid out in the order the Windows client
    /// has, float first; on Linux and macOS the same two slots hold the integer and the float
    /// overloads the other way round. Reading an integer statistic through the float slot answers
    /// false, which is how this was found. See invariant #5 in CLAUDE.md. The decision is made
    /// here, once, and <see cref="Wrappers.SteamUserStats013"/> asks.
    /// </remarks>
    public static bool ReversesOverloadedSlots { get; } = OperatingSystem.IsWindows();

    /// <summary>
    /// False when Valve ships no Steam client for this CPU architecture.
    /// </summary>
    public static bool IsArchitectureSupported =>
        RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64;

    /// <summary>
    /// The explanation shown when this process is an architecture Steam has no client library for.
    /// </summary>
    /// <returns>A message that says what is wrong and which build to run instead.</returns>
    public static string DescribeUnsupportedArchitecture()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return
            $"This is a {arch} build, and Steam does not provide a {arch} client library.\n\n" +
            "Steam's client is x86/x64 only on every platform. Run the x64 build " +
            "instead — Windows on Arm and Apple Silicon will run it under emulation, " +
            "alongside the Steam client itself.";
    }

    /// <summary>
    /// The file name of Steam's client library for this OS and bitness.
    /// </summary>
    public static string GetClientLibraryName()
    {
        if (OperatingSystem.IsWindows() == true)
        {
            return Environment.Is64BitProcess == true ? "steamclient64.dll" : "steamclient.dll";
        }
        if (OperatingSystem.IsMacOS() == true)
        {
            return "steamclient.dylib";
        }
        return "steamclient.so";
    }

    /// <summary>
    /// Candidate paths for the client library, relative to the install path,
    /// most specific first.
    /// </summary>
    public static IEnumerable<string> GetClientLibraryCandidates(string installPath)
    {
        var name = GetClientLibraryName();

        if (OperatingSystem.IsWindows() == true)
        {
            yield return Path.Combine(installPath, name);
            yield return Path.Combine(installPath, "bin", name);
            yield break;
        }

        if (OperatingSystem.IsMacOS() == true)
        {
            yield return Path.Combine(installPath, "Steam.AppBundle", "Steam", "Contents", "MacOS", name);
            yield return Path.Combine(installPath, name);
            yield break;
        }

        // Linux. Steam keeps per-bitness directories, and the "ubuntu12_"
        // names are the long-standing runtime directories.
        var bitness = Environment.Is64BitProcess == true ? "64" : "32";
        yield return Path.Combine(installPath, $"linux{bitness}", name);
        yield return Path.Combine(installPath, $"ubuntu12_{bitness}", name);
        yield return Path.Combine(installPath, name);
    }

    /// <summary>
    /// The Steam installation directory, or null if it could not be found.
    /// </summary>
    public static string? GetInstallPath()
    {
        // Plain `if (OperatingSystem.IsWindows())` rather than this file's
        // usual `== true`: CA1416 only recognises the bare form as a
        // platform guard, and warns on the comparison form.
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsInstallPath();
        }

        foreach (var candidate in GetUnixInstallCandidates())
        {
            if (string.IsNullOrEmpty(candidate) == false &&
                Directory.Exists(candidate) == true)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The process id of the running Steam client, or null when none is running or
    /// it cannot be told. Comparing it with the id from when a connection was made
    /// is how a Steam that quit, or quit and came back, is noticed: the pipe of the
    /// first one is dead either way, and the new one has a different id.
    /// </summary>
    /// <remarks>
    /// On Windows Steam keeps its own id in the registry while it runs and writes 0
    /// when it exits; a crash leaves the old value, so the process is checked too.
    /// Elsewhere the process is found by name. Like the other Linux and macOS code
    /// here, that has not been exercised on those platforms.
    /// </remarks>
    public static int? GetRunningClientProcessId()
    {
        if (OperatingSystem.IsWindows())
        {
            var recorded = GetWindowsActiveProcessId();
            return recorded > 0 && IsSteamProcess(recorded) == true ? recorded : null;
        }

        var name = OperatingSystem.IsMacOS() == true ? "steam_osx" : "steam";
        var running = Process.GetProcessesByName(name);
        try
        {
            return running.Length > 0 ? running[0].Id : null;
        }
        finally
        {
            foreach (var process in running)
            {
                process.Dispose();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static int GetWindowsActiveProcessId()
    {
        return Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess",
            "pid",
            0) is int pid ? pid : 0;
    }

    private static bool IsSteamProcess(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.HasExited == false &&
                   string.Equals(process.ProcessName, "steam", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (ArgumentException)
        {
            // No such process.
            return false;
        }
        catch (InvalidOperationException)
        {
            // It exited while being looked at.
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsInstallPath()
    {
        return (string?)Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\Software\Valve\Steam",
            "InstallPath",
            null);
    }

    private static IEnumerable<string> GetUnixInstallCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home) == true)
        {
            yield break;
        }

        if (OperatingSystem.IsMacOS() == true)
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
            yield break;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) == false)
        {
            yield return Path.Combine(dataHome, "Steam");
        }

        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        // Flatpak keeps its own copy of the whole tree.
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
    }
}
