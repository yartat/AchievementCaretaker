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
namespace AchievementCaretaker.SteamApi;

/// <summary>The step of <see cref="Client.Initialize"/> that failed.</summary>
public enum ClientInitializeFailure : byte
{
    /// <summary>No step is named.</summary>
    Unknown = 0,

    /// <summary>Steam's install directory could not be found.</summary>
    GetInstallPath,

    /// <summary>Steam's client library could not be loaded into this process.</summary>
    Load,

    /// <summary>The library did not hand out its <c>ISteamClient</c>.</summary>
    CreateSteamClient,

    /// <summary>The pipe to the running client could not be opened: Steam is not running.</summary>
    CreateSteamPipe,

    /// <summary>Nobody is logged in to the client.</summary>
    ConnectToGlobalUser,

    /// <summary>Steam thinks this process is another app than the one it asked to be.</summary>
    AppIdMismatch,
}
