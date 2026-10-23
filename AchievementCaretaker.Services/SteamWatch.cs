/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Services;

/// <summary>
/// Notices that the Steam a connection was made to has gone.
/// </summary>
/// <remarks>
/// Whatever was learned from Steam stops being current when it quits, and nothing tells the
/// application so: a helper that is asked later simply finds another Steam, or none. What does say
/// so is the Steam process, so this compares the id it had at connection with the one running now.
/// A Steam that quit and came back has a new id.
/// </remarks>
public static class SteamWatch
{
    /// <summary>
    /// Completes when the Steam client with id <paramref name="processId"/> is no
    /// longer the one running. Never completes for a null id: that means it could
    /// not be told which Steam this was, and guessing would drop a good connection.
    /// Ends with <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public static async Task WaitUntilGoneAsync(
        int? processId,
        Func<int?> probe,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        if (processId == null)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return;
        }

        while (true)
        {
            await Task.Delay(interval, cancellationToken);

            // Off the UI thread: it reads the registry and asks the OS about a process.
            var running = await Task.Run(probe, cancellationToken);
            if (running != processId)
            {
                return;
            }
        }
    }
}
