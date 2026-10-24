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
using System.Runtime.InteropServices;

namespace AchievementCaretaker.SteamApi;

public static class Steam
{
    private static Delegate GetExportDelegate<TDelegate>(IntPtr module, string name)
    {
        return NativeLibrary.TryGetExport(module, name, out var address) == false
            ? null
            : Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate));
    }

    private static TDelegate GetExportFunction<TDelegate>(IntPtr module, string name)
        where TDelegate : class
    {
        return (TDelegate)((object)GetExportDelegate<TDelegate>(module, name));
    }

    private static IntPtr _handle = IntPtr.Zero;

    public static string GetInstallPath()
    {
        return SteamPlatform.GetInstallPath();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr NativeCreateInterface(string version, IntPtr returnCode);

    private static NativeCreateInterface _callCreateInterface;

    public static TClass CreateInterface<TClass>(string version)
        where TClass : INativeWrapper, new()
    {
        IntPtr address = _callCreateInterface(version, IntPtr.Zero);

        if (address == IntPtr.Zero)
        {
            return default;
        }

        TClass instance = new();
        instance.SetupFunctions(address);
        return instance;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeSteamGetCallback(int pipe, out Types.CallbackMessage message, out int call);

    private static NativeSteamGetCallback _callSteamBGetCallback;

    public static bool GetCallback(int pipe, out Types.CallbackMessage message, out int call)
    {
        return _callSteamBGetCallback(pipe, out message, out call);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeSteamFreeLastCallback(int pipe);

    private static NativeSteamFreeLastCallback _callSteamFreeLastCallback;

    public static bool FreeLastCallback(int pipe)
    {
        return _callSteamFreeLastCallback(pipe);
    }

    public static bool Load()
    {
        if (_handle != IntPtr.Zero)
        {
            return true;
        }

        // A native library can only be loaded by a process of the same
        // architecture, and Valve ships no ARM build of the Steam client.
        if (SteamPlatform.IsArchitectureSupported == false)
        {
            return false;
        }

        string installPath = GetInstallPath();
        if (installPath == null)
        {
            return false;
        }

        IntPtr module = IntPtr.Zero;
        foreach (var candidate in SteamPlatform.GetClientLibraryCandidates(installPath))
        {
            if (File.Exists(candidate) == false)
            {
                continue;
            }
            // NativeLibrary maps to LoadLibraryEx / dlopen as appropriate,
            // and resolves the library's own dependencies relative to it,
            // which is what the old SetDllDirectory dance was for.
            if (NativeLibrary.TryLoad(candidate, out module) == true)
            {
                break;
            }
            module = IntPtr.Zero;
        }

        if (module == IntPtr.Zero)
        {
            return false;
        }

        _callCreateInterface = GetExportFunction<NativeCreateInterface>(module, "CreateInterface");
        if (_callCreateInterface == null)
        {
            return false;
        }

        _callSteamBGetCallback = GetExportFunction<NativeSteamGetCallback>(module, "Steam_BGetCallback");
        if (_callSteamBGetCallback == null)
        {
            return false;
        }

        _callSteamFreeLastCallback = GetExportFunction<NativeSteamFreeLastCallback>(module, "Steam_FreeLastCallback");
        if (_callSteamFreeLastCallback == null)
        {
            return false;
        }

        _handle = module;
        return true;
    }
}
