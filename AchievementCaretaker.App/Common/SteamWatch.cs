/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.App.Common;

/// <summary>
/// Notices that the Steam a connection was made to has gone.
/// </summary>
/// <remarks>
/// A pipe to a Steam that quit does not say so: reads just stop returning anything,
/// which looks the same as a quiet Steam. What does say so is the Steam process, so
/// this compares the id it had at connection with the one running now. A Steam that
/// quit and came back has a new id, and its old pipe is just as dead.
/// </remarks>
internal static class SteamWatch
{
    /// <summary>
    /// Completes when the Steam client with id <paramref name="processId"/> is no
    /// longer the one running. Never completes for a null id: that means it could
    /// not be told which Steam this was, and guessing would drop a good connection.
    /// Ends with <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static async Task WaitUntilGoneAsync(
        int? processId,
        CancellationToken cancellationToken,
        Func<int?> probe = null,
        TimeSpan? interval = null)
    {
        if (processId == null)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return;
        }

        probe ??= SteamApi.SteamPlatform.GetRunningClientProcessId;
        var wait = interval ?? SteamConnector.DefaultInterval;
        while (true)
        {
            await Task.Delay(wait, cancellationToken);

            // Off the UI thread: it reads the registry and asks the OS about a process.
            var running = await Task.Run(probe, cancellationToken);
            if (running != processId)
            {
                return;
            }
        }
    }
}
