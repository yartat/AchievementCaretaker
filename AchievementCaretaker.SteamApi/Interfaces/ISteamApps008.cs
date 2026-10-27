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

namespace AchievementCaretaker.SteamApi.Interfaces;

/// <summary>
/// The vtable of Steam's <c>ISteamApps</c> interface, version 008: one slot per virtual method, in declaration order.
/// </summary>
/// <remarks>
/// The order of the fields is an ABI contract with Steam's client library. Never reorder, insert or remove one.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ISteamApps008
{
    /// <summary>Slot of <c>ISteamApps::IsSubscribed</c>.</summary>
    public IntPtr IsSubscribed;
    /// <summary>Slot of <c>ISteamApps::IsLowViolence</c>.</summary>
    public IntPtr IsLowViolence;
    /// <summary>Slot of <c>ISteamApps::IsCybercafe</c>.</summary>
    public IntPtr IsCybercafe;
    /// <summary>Slot of <c>ISteamApps::IsVACBanned</c>.</summary>
    public IntPtr IsVACBanned;
    /// <summary>Slot of <c>ISteamApps::GetCurrentGameLanguage</c>.</summary>
    public IntPtr GetCurrentGameLanguage;
    /// <summary>Slot of <c>ISteamApps::GetAvailableGameLanguages</c>.</summary>
    public IntPtr GetAvailableGameLanguages;
    /// <summary>Slot of <c>ISteamApps::IsSubscribedApp</c>.</summary>
    public IntPtr IsSubscribedApp;
    /// <summary>Slot of <c>ISteamApps::IsDlcInstalled</c>.</summary>
    public IntPtr IsDlcInstalled;
    /// <summary>Slot of <c>ISteamApps::GetEarliestPurchaseUnixTime</c>.</summary>
    public IntPtr GetEarliestPurchaseUnixTime;
    /// <summary>Slot of <c>ISteamApps::IsSubscribedFromFreeWeekend</c>.</summary>
    public IntPtr IsSubscribedFromFreeWeekend;
    /// <summary>Slot of <c>ISteamApps::GetDLCCount</c>.</summary>
    public IntPtr GetDLCCount;
    /// <summary>Slot of <c>ISteamApps::GetDLCDataByIndex</c>.</summary>
    public IntPtr GetDLCDataByIndex;
    /// <summary>Slot of <c>ISteamApps::InstallDLC</c>.</summary>
    public IntPtr InstallDLC;
    /// <summary>Slot of <c>ISteamApps::UninstallDLC</c>.</summary>
    public IntPtr UninstallDLC;
    /// <summary>Slot of <c>ISteamApps::RequestAppProofOfPurchaseKey</c>.</summary>
    public IntPtr RequestAppProofOfPurchaseKey;
    /// <summary>Slot of <c>ISteamApps::GetCurrentBetaName</c>.</summary>
    public IntPtr GetCurrentBetaName;
    /// <summary>Slot of <c>ISteamApps::MarkContentCorrupt</c>.</summary>
    public IntPtr MarkContentCorrupt;
    /// <summary>Slot of <c>ISteamApps::GetInstalledDepots</c>.</summary>
    public IntPtr GetInstalledDepots;
    /// <summary>Slot of <c>ISteamApps::GetAppInstallDir</c>.</summary>
    public IntPtr GetAppInstallDir;
    /// <summary>Slot of <c>ISteamApps::IsAppInstalled</c>.</summary>
    public IntPtr IsAppInstalled;
    /// <summary>Slot of <c>ISteamApps::GetAppOwner</c>.</summary>
    public IntPtr GetAppOwner;
    /// <summary>Slot of <c>ISteamApps::GetLaunchQueryParam</c>.</summary>
    public IntPtr GetLaunchQueryParam;
    /// <summary>Slot of <c>ISteamApps::GetDlcDownloadProgress</c>.</summary>
    public IntPtr GetDlcDownloadProgress;
    /// <summary>Slot of <c>ISteamApps::GetAppBuildId</c>.</summary>
    public IntPtr GetAppBuildId;
    /// <summary>Slot of <c>ISteamApps::RequestAllProofOfPurchaseKeys</c>.</summary>
    public IntPtr RequestAllProofOfPurchaseKeys;
    /// <summary>Slot of <c>ISteamApps::GetFileDetails</c>.</summary>
    public IntPtr GetFileDetails;
    /// <summary>Slot of <c>ISteamApps::GetLaunchCommandLine</c>.</summary>
    public IntPtr GetLaunchCommandLine;
    /// <summary>Slot of <c>ISteamApps::IsSubscribedFromFamilySharing</c>.</summary>
    public IntPtr IsSubscribedFromFamilySharing;
}
