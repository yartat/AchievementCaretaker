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

namespace AchievementCaretaker.App.Library
{
    /// <summary>
    /// The user's own like/dislike for a game.
    /// </summary>
    /// <remarks>
    /// This is <em>this application's own</em> rating, stored locally — it is not Steam's
    /// review recommendation. Steam keeps that server-side and exposes it only
    /// through the Web API, which needs an API key this application does not have and does
    /// not ask for. Nothing here is sent to Steam.
    /// </remarks>
    internal enum OwnRating
    {
        None = 0,
        Like = 1,
        Dislike = 2,
    }

    internal enum GameSortField
    {
        Name = 0,
        ReleaseDate = 1,
        LastPlayed = 2,
        SteamRating = 3,
        OwnRating = 4,
        Completion = 5,
    }

    /// <summary>
    /// The Library rail's mutually exclusive collections. Distinct from the
    /// Show games/demos/mods/junk toggles, which are inclusive and combine.
    /// </summary>
    internal enum GameCollection
    {
        All = 0,
        RecentlyPlayed = 1,
        WithAchievements = 2,
        Perfect = 3,
        Liked = 4,
    }
}
