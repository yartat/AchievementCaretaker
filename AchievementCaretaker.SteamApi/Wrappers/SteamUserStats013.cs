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
/// Steam's <c>ISteamUserStats</c> interface, version 013: a user's achievements and statistics
/// for the app the process was initialised with.
/// </summary>
public class SteamUserStats013 : NativeWrapper<ISteamUserStats013>
{
    /// <summary>
    /// The slot of one overload of a pair. <paramref name="floatField"/> and
    /// <paramref name="integerField"/> are the two fields of the struct, which is in the Windows
    /// client's order; elsewhere the field named for one holds the other. See
    /// <see cref="SteamPlatform.ReversesOverloadedSlots"/>.
    /// </summary>
    private static IntPtr Overload(IntPtr floatField, IntPtr integerField, bool wantInteger) =>
        SteamPlatform.ReversesOverloadedSlots == true
            ? (wantInteger == true ? integerField : floatField)
            : (wantInteger == true ? floatField : integerField);

    #region GetStatValue (int)
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetStatInt(IntPtr self, IntPtr name, out int data);

    /// <summary>Reads an integer statistic.</summary>
    /// <param name="name">The statistic's API name.</param>
    /// <param name="value">The value.</param>
    /// <returns>False when there is no such integer statistic.</returns>
    public bool GetStatValue(string name, out int value)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            var call = GetFunction<NativeGetStatInt>(Overload(Functions.GetStatFloat, Functions.GetStatInteger, wantInteger: true));
            return call(ObjectAddress, nativeName.Handle, out value);
        }
    }
    #endregion

    #region GetStatValue (float)
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetStatFloat(IntPtr self, IntPtr name, out float data);

    /// <summary>Reads a floating-point statistic.</summary>
    /// <param name="name">The statistic's API name.</param>
    /// <param name="value">The value.</param>
    /// <returns>False when there is no such floating-point statistic.</returns>
    public bool GetStatValue(string name, out float value)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            var call = GetFunction<NativeGetStatFloat>(Overload(Functions.GetStatFloat, Functions.GetStatInteger, wantInteger: false));
            return call(ObjectAddress, nativeName.Handle, out value);
        }
    }
    #endregion

    #region SetStatValue (int)
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeSetStatInt(IntPtr self, IntPtr name, int data);

    /// <summary>Sets an integer statistic. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="name">The statistic's API name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>False when Steam refuses it.</returns>
    public bool SetStatValue(string name, int value)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            return Call<bool, NativeSetStatInt>(
                Overload(Functions.SetStatFloat, Functions.SetStatInteger, wantInteger: true),
                ObjectAddress,
                nativeName.Handle,
                value);
        }
    }
    #endregion

    #region SetStatValue (float)
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeSetStatFloat(IntPtr self, IntPtr name, float data);

    /// <summary>Sets a floating-point statistic. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="name">The statistic's API name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>False when Steam refuses it.</returns>
    public bool SetStatValue(string name, float value)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            return Call<bool, NativeSetStatFloat>(
                Overload(Functions.SetStatFloat, Functions.SetStatInteger, wantInteger: false),
                ObjectAddress,
                nativeName.Handle,
                value);
        }
    }
    #endregion

    #region GetAchievement
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetAchievement(
        IntPtr self,
        IntPtr name,
        [MarshalAs(UnmanagedType.I1)] out bool isAchieved);

    /// <summary>Whether an achievement is unlocked.</summary>
    /// <param name="name">The achievement's API name.</param>
    /// <param name="isAchieved">True when it is unlocked.</param>
    /// <returns>False when there is no such achievement.</returns>
    public bool GetAchievement(string name, out bool isAchieved)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            var call = GetFunction<NativeGetAchievement>(Functions.GetAchievement);
            return call(ObjectAddress, nativeName.Handle, out isAchieved);
        }
    }
    #endregion

    #region SetAchievement
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeSetAchievement(IntPtr self, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeClearAchievement(IntPtr self, IntPtr name);

    /// <summary>Unlocks or locks an achievement. Nothing is saved until <see cref="StoreStats"/>.</summary>
    /// <param name="name">The achievement's API name.</param>
    /// <param name="state">True to unlock it, false to lock it again.</param>
    /// <returns>False when Steam refuses it.</returns>
    public bool SetAchievement(string name, bool state)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            if (state == false)
            {
                return Call<bool, NativeClearAchievement>(
                    Functions.ClearAchievement,
                    ObjectAddress,
                    nativeName.Handle);
            }

            return Call<bool, NativeSetAchievement>(
                Functions.SetAchievement,
                ObjectAddress,
                nativeName.Handle);
        }
    }
    #endregion

    #region GetAchievementAndUnlockTime
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetAchievementAndUnlockTime(
        IntPtr self,
        IntPtr name,
        [MarshalAs(UnmanagedType.I1)] out bool isAchieved,
        out uint unlockTime);

    /// <summary>Whether an achievement is unlocked, and when.</summary>
    /// <param name="name">The achievement's API name.</param>
    /// <param name="isAchieved">True when it is unlocked.</param>
    /// <param name="unlockTime">When it was unlocked, in Unix seconds; 0 when it never was or Steam has no time.</param>
    /// <returns>False when there is no such achievement.</returns>
    public bool GetAchievementAndUnlockTime(string name, out bool isAchieved, out uint unlockTime)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            var call = GetFunction<NativeGetAchievementAndUnlockTime>(Functions.GetAchievementAndUnlockTime);
            return call(ObjectAddress, nativeName.Handle, out isAchieved, out unlockTime);
        }
    }
    #endregion

    #region StoreStats
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeStoreStats(IntPtr self);

    /// <summary>Sends every change made since the last call to Steam.</summary>
    /// <returns>False when Steam could not take them.</returns>
    public bool StoreStats()
    {
        return Call<bool, NativeStoreStats>(Functions.StoreStats, ObjectAddress);
    }
    #endregion

    #region GetAchievementIcon
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int NativeGetAchievementIcon(IntPtr self, IntPtr name);

    /// <summary>The handle of an achievement's icon, for <see cref="SteamUtils005.GetImageSize"/> and <see cref="SteamUtils005.GetImageRGBA"/>.</summary>
    /// <param name="name">The achievement's API name.</param>
    /// <returns>The handle, or 0 when the icon is not loaded yet.</returns>
    public int GetAchievementIcon(string name)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        {
            return Call<int, NativeGetAchievementIcon>(
                Functions.GetAchievementIcon,
                ObjectAddress,
                nativeName.Handle);
        }
    }
    #endregion

    #region GetAchievementDisplayAttribute
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetAchievementDisplayAttribute(IntPtr self, IntPtr name, IntPtr key);

    /// <summary>One of an achievement's display attributes: <c>name</c>, <c>desc</c> or <c>hidden</c>.</summary>
    /// <param name="name">The achievement's API name.</param>
    /// <param name="key">The attribute wanted.</param>
    /// <returns>The value, or null when Steam has none.</returns>
    public string? GetAchievementDisplayAttribute(string name, string key)
    {
        using (var nativeName = NativeStrings.StringToStringHandle(name))
        using (var nativeKey = NativeStrings.StringToStringHandle(key))
        {
            var result = Call<IntPtr, NativeGetAchievementDisplayAttribute>(
                Functions.GetAchievementDisplayAttribute,
                ObjectAddress,
                nativeName.Handle,
                nativeKey.Handle);
            return NativeStrings.PointerToString(result);
        }
    }
    #endregion

    #region RequestUserStats
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate CallHandle NativeRequestUserStats(IntPtr self, ulong steamIdUser);

    /// <summary>Asks Steam for a user's statistics and achievements. The answer is a <see cref="Callbacks.UserStatsReceived"/> callback.</summary>
    /// <param name="steamIdUser">The user's 64-bit SteamID.</param>
    /// <returns>The handle of the call, <see cref="CallHandle.Invalid"/> when it was not made.</returns>
    public CallHandle RequestUserStats(ulong steamIdUser)
    {
        return Call<CallHandle, NativeRequestUserStats>(Functions.RequestUserStats, ObjectAddress, steamIdUser);
    }
    #endregion

    #region ResetAllStats
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeResetAllStats(IntPtr self, [MarshalAs(UnmanagedType.I1)] bool achievementsToo);

    /// <summary>Resets every statistic, and the achievements too if asked. Cannot be undone.</summary>
    /// <param name="achievementsToo">True to lock every achievement as well.</param>
    /// <returns>False when Steam refuses.</returns>
    public bool ResetAllStats(bool achievementsToo)
    {
        return Call<bool, NativeResetAllStats>(
            Functions.ResetAllStats,
            ObjectAddress,
            achievementsToo);
    }
    #endregion
}
