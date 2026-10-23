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
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers;

/// <summary>
/// A store whose library is found by checking a published list of games against the account,
/// rather than by asking the store for what the account owns. Only Steam is: its client answers
/// "is this app owned?" one app at a time, and nothing lists the account's games.
/// </summary>
public interface ILibraryScanProvider : IStoreProvider
{
    /// <summary>
    /// Checks each candidate against the signed-in account, and returns the ones it owns with
    /// everything the store knows about them. Takes a while over a long list: progress is reported
    /// as it goes. A failure to reach the store throws <see cref="ProviderException"/>.
    /// </summary>
    Task<LibraryScan> ScanLibraryAsync(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The account the store's client is logged in as (as <see cref="IStoreProvider.AccountId"/> would say
    /// it), which is also the answer to "is the store there?": it throws <see cref="ProviderException"/>,
    /// whose message says why, when the store is not installed, not running, or has nobody logged in.
    /// </summary>
    Task<string> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>The candidate as an owned game, or null when the account does not own it.</summary>
    Task<StoreGame> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken = default);
}
