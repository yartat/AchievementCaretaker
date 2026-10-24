/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services;

/// <summary>
/// What the machine says about Steam without asking Steam: whether this processor has a client at
/// all, whether it is installed, and which process is running. All of it is cheap to ask and none of
/// it starts a helper, which is why <see cref="SteamConnector"/> asks it before it does.
/// </summary>
public interface ISteamEnvironment
{
    /// <summary>Why Steam can never work here (Valve ships no client for this processor), or null when it can.</summary>
    string UnsupportedReason { get; }

    /// <summary>Whether Steam is installed.</summary>
    bool IsInstalled();

    /// <summary>The process id of the running Steam client, or null when none is running or it cannot be told.</summary>
    int? GetRunningProcessId();
}
