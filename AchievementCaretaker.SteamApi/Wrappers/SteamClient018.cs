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
using System.Runtime.InteropServices;
using AchievementCaretaker.SteamApi.Interfaces;

namespace AchievementCaretaker.SteamApi.Wrappers;

/// <summary>
/// Steam's <c>ISteamClient</c> interface, version 018: the entry point that hands out a pipe to
/// the running client, a user on that pipe, and every other interface.
/// </summary>
public class SteamClient018 : NativeWrapper<ISteamClient018>
{
    #region CreateSteamPipe
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int NativeCreateSteamPipe(IntPtr self);

    /// <summary>Opens a pipe to the running Steam client.</summary>
    /// <returns>The pipe, or 0 when the client could not be reached.</returns>
    public int CreateSteamPipe()
    {
        return Call<int, NativeCreateSteamPipe>(Functions.CreateSteamPipe, ObjectAddress);
    }
    #endregion

    #region ReleaseSteamPipe
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeReleaseSteamPipe(IntPtr self, int pipe);

    /// <summary>Closes a pipe opened by <see cref="CreateSteamPipe"/>.</summary>
    /// <param name="pipe">The pipe to close.</param>
    /// <returns>True when Steam closed it.</returns>
    public bool ReleaseSteamPipe(int pipe)
    {
        return Call<bool, NativeReleaseSteamPipe>(Functions.ReleaseSteamPipe, ObjectAddress, pipe);
    }
    #endregion

    #region CreateLocalUser
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int NativeCreateLocalUser(IntPtr self, ref int pipe, Types.AccountType type);

    /// <summary>Creates a local user of its own, for a client that does not use the logged-in one.</summary>
    /// <param name="pipe">The pipe the user is created on; Steam may replace it.</param>
    /// <param name="type">The kind of account to create.</param>
    /// <returns>The user, or 0 on failure.</returns>
    public int CreateLocalUser(ref int pipe, Types.AccountType type)
    {
        var call = GetFunction<NativeCreateLocalUser>(Functions.CreateLocalUser);
        return call(ObjectAddress, ref pipe, type);
    }
    #endregion

    #region ConnectToGlobalUser
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int NativeConnectToGlobalUser(IntPtr self, int pipe);

    /// <summary>Connects to the user who is logged in to the Steam client.</summary>
    /// <param name="pipe">The pipe to connect on.</param>
    /// <returns>The user, or 0 when nobody is logged in.</returns>
    public int ConnectToGlobalUser(int pipe)
    {
        return Call<int, NativeConnectToGlobalUser>(
            Functions.ConnectToGlobalUser,
            ObjectAddress,
            pipe);
    }
    #endregion

    #region ReleaseUser
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NativeReleaseUser(IntPtr self, int pipe, int user);

    /// <summary>Lets go of a user obtained from <see cref="ConnectToGlobalUser"/>.</summary>
    /// <param name="pipe">The pipe the user is on.</param>
    /// <param name="user">The user to release.</param>
    public void ReleaseUser(int pipe, int user)
    {
        Call<NativeReleaseUser>(Functions.ReleaseUser, ObjectAddress, pipe, user);
    }
    #endregion

    #region SetLocalIPBinding
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void NativeSetLocalIPBinding(IntPtr self, uint host, ushort port);

    /// <summary>Binds the client's network traffic to a local address.</summary>
    /// <param name="host">The IPv4 address, in host byte order.</param>
    /// <param name="port">The port.</param>
    public void SetLocalIPBinding(uint host, ushort port)
    {
        Call<NativeSetLocalIPBinding>(Functions.SetLocalIPBinding, ObjectAddress, host, port);
    }
    #endregion

    #region GetISteamUser
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetISteamUser(IntPtr self, int user, int pipe, IntPtr version);

    private TClass GetISteamUser<TClass>(int user, int pipe, string version)
        where TClass : INativeWrapper, new()
    {
        using (var nativeVersion = NativeStrings.StringToStringHandle(version))
        {
            IntPtr address = Call<IntPtr, NativeGetISteamUser>(
                Functions.GetISteamUser,
                ObjectAddress,
                user,
                pipe,
                nativeVersion.Handle);
            TClass result = new();
            result.SetupFunctions(address);
            return result;
        }
    }
    #endregion

    #region GetSteamUser012
    /// <summary>The <c>ISteamUser</c> interface of a user.</summary>
    /// <param name="user">The user.</param>
    /// <param name="pipe">The pipe the user is on.</param>
    public SteamUser012 GetSteamUser012(int user, int pipe)
    {
        return GetISteamUser<SteamUser012>(user, pipe, "SteamUser012");
    }
    #endregion

    #region GetISteamUserStats

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetISteamUserStats(IntPtr self, int user, int pipe, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

    private TClass GetISteamUserStats<TClass>(int user, int pipe, string version)
        where TClass : INativeWrapper, new()
    {
        var address = Call<IntPtr, NativeGetISteamUserStats>(
            Functions.GetISteamUserStats,
            ObjectAddress,
            user,
            pipe,
            version);
        var result = new TClass();
        result.SetupFunctions(address);
        return result;
    }

    #endregion

    #region GetSteamUserStats013

    /// <summary>The <c>ISteamUserStats</c> interface of a user.</summary>
    /// <param name="user">The user.</param>
    /// <param name="pipe">The pipe the user is on.</param>
    public SteamUserStats013 GetSteamUserStats013(int user, int pipe) =>
        GetISteamUserStats<SteamUserStats013>(user, pipe, "STEAMUSERSTATS_INTERFACE_VERSION013");

    #endregion

    #region GetISteamUtils

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetISteamUtils(IntPtr self, int pipe, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

    /// <summary>The <c>ISteamUtils</c> interface, in the version a wrapper class stands for.</summary>
    /// <typeparam name="TClass">The wrapper for that version of the interface.</typeparam>
    /// <param name="pipe">The pipe to the client.</param>
    /// <param name="version">The interface version string to ask Steam for.</param>
    public TClass GetISteamUtils<TClass>(int pipe, string version)
        where TClass : INativeWrapper, new()
    {
        var address = Call<IntPtr, NativeGetISteamUtils>(
            Functions.GetISteamUtils,
            ObjectAddress,
            pipe,
            version);
        var result = new TClass();
        result.SetupFunctions(address);
        return result;
    }

    #endregion

    #region GetSteamUtils004

    /// <summary>The <c>ISteamUtils</c> interface, asked for as version 004.</summary>
    /// <param name="pipe">The pipe to the client.</param>
    public SteamUtils005 GetSteamUtils004(int pipe) =>
         GetISteamUtils<SteamUtils005>(pipe, "SteamUtils004");

    #endregion

    #region GetISteamApps

    // Every ISteamClient accessor is a C++ member function, so the object
    // pointer has to be passed explicitly as the first argument under the
    // thiscall convention. This one was missing both the attribute and the
    // `self` parameter: on x86 it survived because the non-this arguments
    // still landed at the right stack offsets, but on x64 every argument
    // shifts by one register and Steam returns a null interface.
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetISteamApps(IntPtr self, int user, int pipe, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

    private TClass GetISteamApps<TClass>(int user, int pipe, string version)
        where TClass : INativeWrapper, new()
    {
        var address = Call<IntPtr, NativeGetISteamApps>(
            Functions.GetISteamApps,
            ObjectAddress,
            user,
            pipe,
            version);
        var result = new TClass();
        result.SetupFunctions(address);
        return result;
    }

    #endregion

    #region GetSteamApps001

    /// <summary>The <c>ISteamApps</c> interface of a user, version 001.</summary>
    /// <param name="user">The user.</param>
    /// <param name="pipe">The pipe the user is on.</param>
    public SteamApps001 GetSteamApps001(int user, int pipe) =>
        GetISteamApps<SteamApps001>(user, pipe, "STEAMAPPS_INTERFACE_VERSION001");

    #endregion

    #region GetSteamApps008
    /// <summary>The <c>ISteamApps</c> interface of a user, version 008.</summary>
    /// <param name="user">The user.</param>
    /// <param name="pipe">The pipe the user is on.</param>
    public SteamApps008 GetSteamApps008(int user, int pipe)
    {
        return GetISteamApps<SteamApps008>(user, pipe, "STEAMAPPS_INTERFACE_VERSION008");
    }
    #endregion
}
