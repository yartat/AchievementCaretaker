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

namespace AchievementCaretaker.Providers.Steam.Models;

/// <summary>One achievement as Steam's schema describes it.</summary>
/// <param name="Id">The achievement's API name, which it is set by.</param>
/// <param name="Name">The name in the game's language, falling back to English and then to <paramref name="Id"/>.</param>
/// <param name="Description">What it takes to earn it, in the same language; empty if the schema gives none.</param>
/// <param name="IconNormal">The file name of its icon once earned.</param>
/// <param name="IconLocked">The file name of its grey icon while locked; empty if the schema has none.</param>
/// <param name="Hidden">Steam hides its description until it is earned.</param>
/// <param name="Permission">Steam's permission bits; any of the low two means it cannot be changed from a client.</param>
public sealed record SteamAchievementDefinition(
    string Id, string Name, string Description, string IconNormal, string IconLocked, bool Hidden, int Permission);
