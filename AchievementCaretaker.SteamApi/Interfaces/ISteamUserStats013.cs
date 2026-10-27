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
/// The vtable of Steam's <c>ISteamUserStats</c> interface, version 013: one slot per virtual method, in declaration order.
/// </summary>
/// <remarks>
/// The order of the fields is an ABI contract with Steam's client library. Never reorder, insert or remove one.
/// The overloaded pairs (<c>GetStat</c>, <c>SetStat</c>) are in the Windows client's order; see <see cref="SteamPlatform.ReversesOverloadedSlots"/>.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ISteamUserStats013
{
    /// <summary>Slot of the <c>float</c> overload of <c>ISteamUserStats::GetStat</c> on the Windows client; on Linux and macOS it holds the other overload.</summary>
    public IntPtr GetStatFloat;
    /// <summary>Slot of the <c>integer</c> overload of <c>ISteamUserStats::GetStat</c> on the Windows client; on Linux and macOS it holds the other overload.</summary>
    public IntPtr GetStatInteger;
    /// <summary>Slot of the <c>float</c> overload of <c>ISteamUserStats::SetStat</c> on the Windows client; on Linux and macOS it holds the other overload.</summary>
    public IntPtr SetStatFloat;
    /// <summary>Slot of the <c>integer</c> overload of <c>ISteamUserStats::SetStat</c> on the Windows client; on Linux and macOS it holds the other overload.</summary>
    public IntPtr SetStatInteger;
    /// <summary>Slot of <c>ISteamUserStats::UpdateAvgRateStat</c>.</summary>
    public IntPtr UpdateAvgRateStat;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievement</c>.</summary>
    public IntPtr GetAchievement;
    /// <summary>Slot of <c>ISteamUserStats::SetAchievement</c>.</summary>
    public IntPtr SetAchievement;
    /// <summary>Slot of <c>ISteamUserStats::ClearAchievement</c>.</summary>
    public IntPtr ClearAchievement;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementAndUnlockTime</c>.</summary>
    public IntPtr GetAchievementAndUnlockTime;
    /// <summary>Slot of <c>ISteamUserStats::StoreStats</c>.</summary>
    public IntPtr StoreStats;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementIcon</c>.</summary>
    public IntPtr GetAchievementIcon;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementDisplayAttribute</c>.</summary>
    public IntPtr GetAchievementDisplayAttribute;
    /// <summary>Slot of <c>ISteamUserStats::IndicateAchievementProgress</c>.</summary>
    public IntPtr IndicateAchievementProgress;
    /// <summary>Slot of <c>ISteamUserStats::GetNumAchievements</c>.</summary>
    public IntPtr GetNumAchievements;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementName</c>.</summary>
    public IntPtr GetAchievementName;
    /// <summary>Slot of <c>ISteamUserStats::RequestUserStats</c>.</summary>
    public IntPtr RequestUserStats;
    /// <summary>Slot of <c>ISteamUserStats::GetUserStatFloat</c>.</summary>
    public IntPtr GetUserStatFloat;
    /// <summary>Slot of <c>ISteamUserStats::GetUserStatInt</c>.</summary>
    public IntPtr GetUserStatInt;
    /// <summary>Slot of <c>ISteamUserStats::GetUserAchievement</c>.</summary>
    public IntPtr GetUserAchievement;
    /// <summary>Slot of <c>ISteamUserStats::GetUserAchievementAndUnlockTime</c>.</summary>
    public IntPtr GetUserAchievementAndUnlockTime;
    /// <summary>Slot of <c>ISteamUserStats::ResetAllStats</c>.</summary>
    public IntPtr ResetAllStats;
    /// <summary>Slot of <c>ISteamUserStats::FindOrCreateLeaderboard</c>.</summary>
    public IntPtr FindOrCreateLeaderboard;
    /// <summary>Slot of <c>ISteamUserStats::FindLeaderboard</c>.</summary>
    public IntPtr FindLeaderboard;
    /// <summary>Slot of <c>ISteamUserStats::GetLeaderboardName</c>.</summary>
    public IntPtr GetLeaderboardName;
    /// <summary>Slot of <c>ISteamUserStats::GetLeaderboardEntryCount</c>.</summary>
    public IntPtr GetLeaderboardEntryCount;
    /// <summary>Slot of <c>ISteamUserStats::GetLeaderboardSortMethod</c>.</summary>
    public IntPtr GetLeaderboardSortMethod;
    /// <summary>Slot of <c>ISteamUserStats::GetLeaderboardDisplayType</c>.</summary>
    public IntPtr GetLeaderboardDisplayType;
    /// <summary>Slot of <c>ISteamUserStats::DownloadLeaderboardEntries</c>.</summary>
    public IntPtr DownloadLeaderboardEntries;
    /// <summary>Slot of <c>ISteamUserStats::DownloadLeaderboardEntriesForUsers</c>.</summary>
    public IntPtr DownloadLeaderboardEntriesForUsers;
    /// <summary>Slot of <c>ISteamUserStats::GetDownloadedLeaderboardEntry</c>.</summary>
    public IntPtr GetDownloadedLeaderboardEntry;
    /// <summary>Slot of <c>ISteamUserStats::UploadLeaderboardScore</c>.</summary>
    public IntPtr UploadLeaderboardScore;
    /// <summary>Slot of <c>ISteamUserStats::AttachLeaderboardUGC</c>.</summary>
    public IntPtr AttachLeaderboardUGC;
    /// <summary>Slot of <c>ISteamUserStats::GetNumberOfCurrentPlayers</c>.</summary>
    public IntPtr GetNumberOfCurrentPlayers;
    /// <summary>Slot of <c>ISteamUserStats::RequestGlobalAchievementPercentages</c>.</summary>
    public IntPtr RequestGlobalAchievementPercentages;
    /// <summary>Slot of <c>ISteamUserStats::GetMostAchievedAchievementInfo</c>.</summary>
    public IntPtr GetMostAchievedAchievementInfo;
    /// <summary>Slot of <c>ISteamUserStats::GetNextMostAchievedAchievementInfo</c>.</summary>
    public IntPtr GetNextMostAchievedAchievementInfo;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementAchievedPercent</c>.</summary>
    public IntPtr GetAchievementAchievedPercent;
    /// <summary>Slot of <c>ISteamUserStats::RequestGlobalStats</c>.</summary>
    public IntPtr RequestGlobalStats;
    /// <summary>Slot of <c>ISteamUserStats::GetGlobalStatFloat</c>.</summary>
    public IntPtr GetGlobalStatFloat;
    /// <summary>Slot of <c>ISteamUserStats::GetGlobalStatInteger</c>.</summary>
    public IntPtr GetGlobalStatInteger;
    /// <summary>Slot of <c>ISteamUserStats::GetGlobalStatHistoryFloat</c>.</summary>
    public IntPtr GetGlobalStatHistoryFloat;
    /// <summary>Slot of <c>ISteamUserStats::GetGlobalStatHistoryInteger</c>.</summary>
    public IntPtr GetGlobalStatHistoryInteger;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementProgressLimitsFloat</c>.</summary>
    public IntPtr GetAchievementProgressLimitsFloat;
    /// <summary>Slot of <c>ISteamUserStats::GetAchievementProgressLimitsInteger</c>.</summary>
    public IntPtr GetAchievementProgressLimitsInteger;
}
