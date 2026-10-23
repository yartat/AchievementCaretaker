/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services;

/// <summary>
/// The Steam library: which games the logged-in account owns. Steam has no call that lists them,
/// so the published list of every app is downloaded and each entry is asked about, by the hidden
/// helper (<see cref="ILibraryScanProvider"/>). Failures to reach Steam are
/// <see cref="ProviderException"/>s whose message says why.
/// </summary>
public interface ISteamLibraryService
{
    /// <summary>The account Steam is logged in as. Doubles as "is Steam there?".</summary>
    Task<string> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Every app on the published list, with its type. Throws when the list cannot be had.</summary>
    Task<IReadOnlyList<LibraryCandidate>> GetPublishedGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the candidates against the account. Only a <see cref="LibraryScan.Complete"/> scan says anything about what is not owned.</summary>
    Task<LibraryScan> ScanAsync(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>One app as an owned game, or null when the account does not own it.</summary>
    Task<StoreGame> FindOwnedAsync(uint id, CancellationToken cancellationToken = default);
}
