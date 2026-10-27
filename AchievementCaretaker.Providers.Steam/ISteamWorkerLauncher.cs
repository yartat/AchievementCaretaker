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
    /// <summary>Where the helper is listening and what a caller needs to trust and be trusted by it.</summary>
    SteamWorkerEndpoint Endpoint { get; }
}

/// <summary>Starts the helper for one app id. The provider asks; how is up to the launcher.</summary>
public interface ISteamWorkerLauncher
{
    /// <summary>Starts a helper and waits until it is serving.</summary>
    /// <param name="appId">The app id the helper acts as; 0 for the library as a whole.</param>
    /// <param name="cancellationToken">The token to cancel the start.</param>
    /// <returns>The running helper.</returns>
    Task<ISteamWorker> StartAsync(long appId, CancellationToken cancellationToken);
}
