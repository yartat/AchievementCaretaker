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

namespace AchievementCaretaker.SteamApi.Types;

/// <summary>The kind of a Steam account, as Valve's <c>EAccountType</c> numbers them.</summary>
public enum AccountType : int
{
    /// <summary>Not a valid account.</summary>
    Invalid = 0,

    /// <summary>An ordinary person's account.</summary>
    Individual = 1,

    /// <summary>A multiseat account, such as an internet cafe's.</summary>
    Multiset = 2,

    /// <summary>A game server that logs in.</summary>
    GameServer = 3,

    /// <summary>A game server that does not log in.</summary>
    AnonGameServer = 4,

    /// <summary>An account that is not complete yet.</summary>
    Pending = 5,

    /// <summary>A content server.</summary>
    ContentServer = 6,

    /// <summary>A Steam group.</summary>
    Clan = 7,

    /// <summary>A chat room.</summary>
    Chat = 8,

    /// <summary>A peer-to-peer super seeder.</summary>
    P2PSuperSeeder = 9,
}
