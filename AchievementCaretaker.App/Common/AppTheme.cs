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

using Avalonia.Styling;

namespace AchievementCaretaker.App.Common;

/// <summary>
/// Which palette the windows use. <see cref="System"/> is the default and
/// follows the operating system, which is what the application did before
/// this was configurable.
/// </summary>
internal enum AppTheme
{
    System,
    Light,
    Dark,
}

internal static class AppThemes
{
    /// <summary>
    /// <see cref="ThemeVariant.Default"/> is not a third palette — it is
    /// what makes Avalonia track the system setting, so the variant can
    /// still change while a window is open. That is why every themed brush
    /// has to be a <c>{DynamicResource}</c>.
    /// </summary>
    public static ThemeVariant ToVariant(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static string GetDisplayName(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark",
            _ => "Follow system",
        };
    }
}
