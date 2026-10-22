/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>Where a running helper can be reached, and how to know it is the one started.</summary>
    /// <param name="Port">A TCP port on 127.0.0.1.</param>
    /// <param name="Thumbprint">The thumbprint of the certificate it serves.</param>
    /// <param name="Token">The token every call must carry.</param>
    public sealed record SteamWorkerEndpoint(int Port, string Thumbprint, string Token);

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
}
