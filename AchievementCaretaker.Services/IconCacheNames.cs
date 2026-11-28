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

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AchievementCaretaker.Services;

/// <summary>Tells the files the icon cache wrote from everything else in its directory.</summary>
/// <remarks>
/// The directory is chosen by the user and may hold their own files, which <c>Prune</c> and
/// <c>MoveTo</c> must never touch. So only the two exact shapes the cache has written count: a
/// capsule, <c>&lt;appid&gt;.img</c>, and an achievement icon, <c>&lt;appid&gt;_&lt;40 hex digits&gt;.jpg</c>,
/// which is how Steam names them. Erring on the strict side leaves a stale file behind; erring
/// the other way deletes someone's file.
/// </remarks>
public static partial class IconCacheNames
{
    /// <summary>Recovers the app id a cached file belongs to.</summary>
    /// <param name="fileName">A file name or path.</param>
    /// <param name="appId">The app id, when the file is one of the cache's.</param>
    /// <returns>False for anything the cache did not write.</returns>
    public static bool TryGetAppId(string fileName, out uint appId)
    {
        appId = 0;
        var match = CacheFileName().Match(Path.GetFileName(fileName));
        return match.Success == true &&
            uint.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out appId);
    }

    [GeneratedRegex(@"^([0-9]{1,10})(?:\.img|_[0-9a-fA-F]{40}\.jpg)\z")]
    private static partial Regex CacheFileName();
}
