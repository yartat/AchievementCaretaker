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

/// <summary>How Steam answered a request for a user's items.</summary>
public enum ItemRequestResult : int
{
    /// <summary>The value was not one Steam defines.</summary>
    InvalidValue = -1,

    /// <summary>The request succeeded.</summary>
    OK = 0,

    /// <summary>The request was refused.</summary>
    Denied = 1,

    /// <summary>Steam's servers failed.</summary>
    ServerError = 2,

    /// <summary>Steam's servers did not answer in time.</summary>
    Timeout = 3,

    /// <summary>The request was not valid.</summary>
    Invalid = 4,

    /// <summary>Nothing matched.</summary>
    NoMatch = 5,

    /// <summary>Something went wrong that Steam does not say.</summary>
    UnknownError = 6,

    /// <summary>Nobody is logged on.</summary>
    NotLoggedOn = 7,
}
