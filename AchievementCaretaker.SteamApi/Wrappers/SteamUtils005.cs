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

/// <summary>Steam's <c>ISteamUtils</c> interface, version 005: facts about the client and the app it is used as.</summary>
public class SteamUtils005 : NativeWrapper<ISteamUtils005>
{
    #region GetConnectedUniverse
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int NativeGetConnectedUniverse(IntPtr self);

    /// <summary>The Steam universe the client is connected to.</summary>
    public int GetConnectedUniverse()
    {
        return Call<int, NativeGetConnectedUniverse>(Functions.GetConnectedUniverse, ObjectAddress);
    }
    #endregion

    #region GetIPCountry
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetIPCountry(IntPtr self);

    /// <summary>The two-letter country code Steam places the client's IP address in.</summary>
    /// <returns>The code, or null when Steam does not say.</returns>
    public string? GetIPCountry()
    {
        var result = Call<IntPtr, NativeGetIPCountry>(Functions.GetIPCountry, ObjectAddress);
        return NativeStrings.PointerToString(result);
    }
    #endregion

    #region GetImageSize
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetImageSize(IntPtr self, int index, out int width, out int height);

    /// <summary>The size of an image Steam holds, such as an achievement icon.</summary>
    /// <param name="index">The image handle.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>False when there is no such image.</returns>
    public bool GetImageSize(int index, out int width, out int height)
    {
        var call = GetFunction<NativeGetImageSize>(Functions.GetImageSize);
        return call(ObjectAddress, index, out width, out height);
    }
    #endregion

    #region GetImageRGBA
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeGetImageRGBA(IntPtr self, int index, byte[] buffer, int length);

    /// <summary>Copies an image Steam holds, as 8-bit RGBA, into a buffer.</summary>
    /// <param name="index">The image handle.</param>
    /// <param name="data">The buffer, as large as width times height times four.</param>
    /// <returns>False when there is no such image or the buffer is too small.</returns>
    public bool GetImageRGBA(int index, byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException("data");
        }
        var call = GetFunction<NativeGetImageRGBA>(Functions.GetImageRGBA);
        return call(ObjectAddress, index, data, data.Length);
    }
    #endregion

    #region GetAppID
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate uint NativeGetAppId(IntPtr self);

    /// <summary>The app id this process told Steam it is.</summary>
    public uint GetAppId()
    {
        return Call<uint, NativeGetAppId>(Functions.GetAppID, ObjectAddress);
    }
    #endregion
}
