/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using AchievementCaretaker.Services;
using AchievementCaretaker.SteamApi;

namespace AchievementCaretaker.App.Services;

/// <summary>The <see cref="ISteamEnvironment"/> of this machine, from <see cref="SteamPlatform"/>.</summary>
internal sealed class SteamPlatformEnvironment : ISteamEnvironment
{
    public string? UnsupportedReason =>
        SteamPlatform.IsArchitectureSupported == true ? null : SteamPlatform.DescribeUnsupportedArchitecture();

    public bool IsInstalled() => SteamPlatform.GetInstallPath() != null;

    public int? GetRunningProcessId() => SteamPlatform.GetRunningClientProcessId();
}
