/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>A running helper. Disposing it asks it to exit and makes sure it has.</summary>
public interface ISteamWorker : IAsyncDisposable
{
    SteamWorkerEndpoint Endpoint { get; }
}

/// <summary>Starts the helper for one app id. The provider asks; how is up to the launcher.</summary>
public interface ISteamWorkerLauncher
{
    Task<ISteamWorker> StartAsync(long appId, CancellationToken cancellationToken);
}
