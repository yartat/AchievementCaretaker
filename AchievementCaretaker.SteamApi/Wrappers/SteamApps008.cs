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

/// <summary>Steam's <c>ISteamApps</c> interface, version 008: what the account owns and in which language.</summary>
public class SteamApps008 : NativeWrapper<ISteamApps008>
{
    #region IsSubscribed
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NativeIsSubscribedApp(IntPtr self, uint gameId);

    /// <summary>Whether the logged-in account owns an app.</summary>
    /// <param name="gameId">The app id.</param>
    public bool IsSubscribedApp(uint gameId)
    {
        return Call<bool, NativeIsSubscribedApp>(Functions.IsSubscribedApp, ObjectAddress, gameId);
    }
    #endregion

    #region GetCurrentGameLanguage
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate IntPtr NativeGetCurrentGameLanguage(IntPtr self);

    /// <summary>The language Steam is set to for games, such as <c>english</c>.</summary>
    /// <returns>The language, or null when Steam does not say.</returns>
    public string? GetCurrentGameLanguage()
    {
        var languagePointer = Call<IntPtr, NativeGetCurrentGameLanguage>(
            Functions.GetCurrentGameLanguage,
            ObjectAddress);
        return NativeStrings.PointerToString(languagePointer);
    }
    #endregion
}
