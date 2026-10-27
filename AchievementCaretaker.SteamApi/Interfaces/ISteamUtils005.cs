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
/// The vtable of Steam's <c>ISteamUtils</c> interface, version 005: one slot per virtual method, in declaration order.
/// </summary>
/// <remarks>
/// The order of the fields is an ABI contract with Steam's client library. Never reorder, insert or remove one.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ISteamUtils005
{
    /// <summary>Slot of <c>ISteamUtils::GetSecondsSinceAppActive</c>.</summary>
    public IntPtr GetSecondsSinceAppActive;
    /// <summary>Slot of <c>ISteamUtils::GetSecondsSinceComputerActive</c>.</summary>
    public IntPtr GetSecondsSinceComputerActive;
    /// <summary>Slot of <c>ISteamUtils::GetConnectedUniverse</c>.</summary>
    public IntPtr GetConnectedUniverse;
    /// <summary>Slot of <c>ISteamUtils::GetServerRealTime</c>.</summary>
    public IntPtr GetServerRealTime;
    /// <summary>Slot of <c>ISteamUtils::GetIPCountry</c>.</summary>
    public IntPtr GetIPCountry;
    /// <summary>Slot of <c>ISteamUtils::GetImageSize</c>.</summary>
    public IntPtr GetImageSize;
    /// <summary>Slot of <c>ISteamUtils::GetImageRGBA</c>.</summary>
    public IntPtr GetImageRGBA;
    /// <summary>Slot of <c>ISteamUtils::GetCSERIPPort</c>.</summary>
    public IntPtr GetCSERIPPort;
    /// <summary>Slot of <c>ISteamUtils::GetCurrentBatteryPower</c>.</summary>
    public IntPtr GetCurrentBatteryPower;
    /// <summary>Slot of <c>ISteamUtils::GetAppID</c>.</summary>
    public IntPtr GetAppID;
    /// <summary>Slot of <c>ISteamUtils::SetOverlayNotificationPosition</c>.</summary>
    public IntPtr SetOverlayNotificationPosition;
    /// <summary>Slot of <c>ISteamUtils::IsAPICallCompleted</c>.</summary>
    public IntPtr IsAPICallCompleted;
    /// <summary>Slot of <c>ISteamUtils::GetAPICallFailureReason</c>.</summary>
    public IntPtr GetAPICallFailureReason;
    /// <summary>Slot of <c>ISteamUtils::GetAPICallResult</c>.</summary>
    public IntPtr GetAPICallResult;
    /// <summary>Slot of <c>ISteamUtils::RunFrame</c>.</summary>
    public IntPtr RunFrame;
    /// <summary>Slot of <c>ISteamUtils::GetIPCCallCount</c>.</summary>
    public IntPtr GetIPCCallCount;
    /// <summary>Slot of <c>ISteamUtils::SetWarningMessageHook</c>.</summary>
    public IntPtr SetWarningMessageHook;
    /// <summary>Slot of <c>ISteamUtils::IsOverlayEnabled</c>.</summary>
    public IntPtr IsOverlayEnabled;
    /// <summary>Slot of <c>ISteamUtils::OverlayNeedsPresent</c>.</summary>
    public IntPtr OverlayNeedsPresent;
}
