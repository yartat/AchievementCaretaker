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
/// The vtable of Steam's <c>ISteamUser</c> interface, version 012: one slot per virtual method, in declaration order.
/// </summary>
/// <remarks>
/// The order of the fields is an ABI contract with Steam's client library. Never reorder, insert or remove one.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ISteamUser012
{
    /// <summary>Slot of <c>ISteamUser::GetHSteamUser</c>.</summary>
    public IntPtr GetHSteamUser;
    /// <summary>Slot of <c>ISteamUser::LoggedOn</c>.</summary>
    public IntPtr LoggedOn;
    /// <summary>Slot of <c>ISteamUser::GetSteamID</c>.</summary>
    public IntPtr GetSteamID;
    /// <summary>Slot of <c>ISteamUser::InitiateGameConnection</c>.</summary>
    public IntPtr InitiateGameConnection;
    /// <summary>Slot of <c>ISteamUser::TerminateGameConnection</c>.</summary>
    public IntPtr TerminateGameConnection;
    /// <summary>Slot of <c>ISteamUser::TrackAppUsageEvent</c>.</summary>
    public IntPtr TrackAppUsageEvent;
    /// <summary>Slot of <c>ISteamUser::GetUserDataFolder</c>.</summary>
    public IntPtr GetUserDataFolder;
    /// <summary>Slot of <c>ISteamUser::StartVoiceRecording</c>.</summary>
    public IntPtr StartVoiceRecording;
    /// <summary>Slot of <c>ISteamUser::StopVoiceRecording</c>.</summary>
    public IntPtr StopVoiceRecording;
    /// <summary>Slot of <c>ISteamUser::GetCompressedVoice</c>.</summary>
    public IntPtr GetCompressedVoice;
    /// <summary>Slot of <c>ISteamUser::DecompressVoice</c>.</summary>
    public IntPtr DecompressVoice;
    /// <summary>Slot of <c>ISteamUser::GetAuthSessionTicket</c>.</summary>
    public IntPtr GetAuthSessionTicket;
    /// <summary>Slot of <c>ISteamUser::BeginAuthSession</c>.</summary>
    public IntPtr BeginAuthSession;
    /// <summary>Slot of <c>ISteamUser::EndAuthSession</c>.</summary>
    public IntPtr EndAuthSession;
    /// <summary>Slot of <c>ISteamUser::CancelAuthTicket</c>.</summary>
    public IntPtr CancelAuthTicket;
    /// <summary>Slot of <c>ISteamUser::UserHasLicenseForApp</c>.</summary>
    public IntPtr UserHasLicenseForApp;
}
