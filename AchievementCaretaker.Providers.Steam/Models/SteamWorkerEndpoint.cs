/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Steam.Models;

/// <summary>Where a running helper can be reached, and how to know it is the one started.</summary>
/// <param name="Port">A TCP port on 127.0.0.1.</param>
/// <param name="Thumbprint">The thumbprint of the certificate it serves.</param>
/// <param name="Token">The token every call must carry.</param>
public sealed record SteamWorkerEndpoint(int Port, string Thumbprint, string Token);
