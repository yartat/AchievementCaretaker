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
/// The vtable of Steam's <c>ISteamClient</c> interface, version 018: one slot per virtual method, in declaration order.
/// </summary>
/// <remarks>
/// The order of the fields is an ABI contract with Steam's client library. Never reorder, insert or remove one.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ISteamClient018
{
    /// <summary>Slot of <c>ISteamClient::CreateSteamPipe</c>.</summary>
    public IntPtr CreateSteamPipe;
    /// <summary>Slot of <c>ISteamClient::ReleaseSteamPipe</c>.</summary>
    public IntPtr ReleaseSteamPipe;
    /// <summary>Slot of <c>ISteamClient::ConnectToGlobalUser</c>.</summary>
    public IntPtr ConnectToGlobalUser;
    /// <summary>Slot of <c>ISteamClient::CreateLocalUser</c>.</summary>
    public IntPtr CreateLocalUser;
    /// <summary>Slot of <c>ISteamClient::ReleaseUser</c>.</summary>
    public IntPtr ReleaseUser;
    /// <summary>Slot of <c>ISteamClient::GetISteamUser</c>.</summary>
    public IntPtr GetISteamUser;
    /// <summary>Slot of <c>ISteamClient::GetISteamGameServer</c>.</summary>
    public IntPtr GetISteamGameServer;
    /// <summary>Slot of <c>ISteamClient::SetLocalIPBinding</c>.</summary>
    public IntPtr SetLocalIPBinding;
    /// <summary>Slot of <c>ISteamClient::GetISteamFriends</c>.</summary>
    public IntPtr GetISteamFriends;
    /// <summary>Slot of <c>ISteamClient::GetISteamUtils</c>.</summary>
    public IntPtr GetISteamUtils;
    /// <summary>Slot of <c>ISteamClient::GetISteamMatchmaking</c>.</summary>
    public IntPtr GetISteamMatchmaking;
    /// <summary>Slot of <c>ISteamClient::GetISteamMatchmakingServers</c>.</summary>
    public IntPtr GetISteamMatchmakingServers;
    /// <summary>Slot of <c>ISteamClient::GetISteamGenericInterface</c>.</summary>
    public IntPtr GetISteamGenericInterface;
    /// <summary>Slot of <c>ISteamClient::GetISteamUserStats</c>.</summary>
    public IntPtr GetISteamUserStats;
    /// <summary>Slot of <c>ISteamClient::GetISteamGameServerStats</c>.</summary>
    public IntPtr GetISteamGameServerStats;
    /// <summary>Slot of <c>ISteamClient::GetISteamApps</c>.</summary>
    public IntPtr GetISteamApps;
    /// <summary>Slot of <c>ISteamClient::GetISteamNetworking</c>.</summary>
    public IntPtr GetISteamNetworking;
    /// <summary>Slot of <c>ISteamClient::GetISteamRemoteStorage</c>.</summary>
    public IntPtr GetISteamRemoteStorage;
    /// <summary>Slot of <c>ISteamClient::GetISteamScreenshots</c>.</summary>
    public IntPtr GetISteamScreenshots;
    /// <summary>Slot of <c>ISteamClient::GetISteamGameSearch</c>.</summary>
    public IntPtr GetISteamGameSearch;
    /// <summary>Slot of <c>ISteamClient::RunFrame</c>.</summary>
    public IntPtr RunFrame;
    /// <summary>Slot of <c>ISteamClient::GetIPCCallCount</c>.</summary>
    public IntPtr GetIPCCallCount;
    /// <summary>Slot of <c>ISteamClient::SetWarningMessageHook</c>.</summary>
    public IntPtr SetWarningMessageHook;
    /// <summary>Slot of <c>ISteamClient::ShutdownIfAllPipesClosed</c>.</summary>
    public IntPtr ShutdownIfAllPipesClosed;
    /// <summary>Slot of <c>ISteamClient::GetISteamHTTP</c>.</summary>
    public IntPtr GetISteamHTTP;
    /// <summary>Placeholder for the removed method <c>GetISteamUnifiedMessages</c>, kept so the slots after it stay in place.</summary>
    public IntPtr DEPRECATED_GetISteamUnifiedMessages;
    /// <summary>Slot of <c>ISteamClient::GetISteamController</c>.</summary>
    public IntPtr GetISteamController;
    /// <summary>Slot of <c>ISteamClient::GetISteamUGC</c>.</summary>
    public IntPtr GetISteamUGC;
    /// <summary>Slot of <c>ISteamClient::GetISteamAppList</c>.</summary>
    public IntPtr GetISteamAppList;
    /// <summary>Slot of <c>ISteamClient::GetISteamMusic</c>.</summary>
    public IntPtr GetISteamMusic;
    /// <summary>Slot of <c>ISteamClient::GetISteamMusicRemote</c>.</summary>
    public IntPtr GetISteamMusicRemote;
    /// <summary>Slot of <c>ISteamClient::GetISteamHTMLSurface</c>.</summary>
    public IntPtr GetISteamHTMLSurface;
    /// <summary>Placeholder for the removed method <c>Set_SteamAPI_CPostAPIResultInProcess</c>, kept so the slots after it stay in place.</summary>
    public IntPtr DEPRECATED_Set_SteamAPI_CPostAPIResultInProcess;
    /// <summary>Placeholder for the removed method <c>Remove_SteamAPI_CPostAPIResultInProcess</c>, kept so the slots after it stay in place.</summary>
    public IntPtr DEPRECATED_Remove_SteamAPI_CPostAPIResultInProcess;
    /// <summary>Slot of <c>ISteamClient::Set_SteamAPI_CCheckCallbackRegisteredInProcess</c>.</summary>
    public IntPtr Set_SteamAPI_CCheckCallbackRegisteredInProcess;
    /// <summary>Slot of <c>ISteamClient::GetISteamInventory</c>.</summary>
    public IntPtr GetISteamInventory;
    /// <summary>Slot of <c>ISteamClient::GetISteamVideo</c>.</summary>
    public IntPtr GetISteamVideo;
    /// <summary>Slot of <c>ISteamClient::GetISteamParentalSettings</c>.</summary>
    public IntPtr GetISteamParentalSettings;
    /// <summary>Slot of <c>ISteamClient::GetISteamInput</c>.</summary>
    public IntPtr GetISteamInput;
    /// <summary>Slot of <c>ISteamClient::GetISteamParties</c>.</summary>
    public IntPtr GetISteamParties;
}
