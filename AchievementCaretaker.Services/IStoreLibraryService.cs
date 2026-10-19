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

namespace AchievementCaretaker.Services
{
    /// <summary>What re-reading a store's owned list came to.</summary>
    /// <param name="Library">The owned list, or null when the read did not work.</param>
    /// <param name="SessionExpired">The store refused the session: the account must sign in again.</param>
    /// <param name="Failure">A sentence for the user when the read did not work for another reason.</param>
    public sealed record StoreLibraryRefresh(IReadOnlyList<StoreGame> Library, bool SessionExpired, string Failure);

    /// <summary>How a progress run ended.</summary>
    /// <param name="Done">Games whose achievements were read.</param>
    /// <param name="Failed">Games that could not be read (the run goes on past them).</param>
    /// <param name="SessionExpired">The run stopped because the store refused the session.</param>
    /// <param name="Cancelled">The run stopped because it was cancelled.</param>
    public sealed record StoreProgressRun(int Done, int Failed, bool SessionExpired, bool Cancelled);

    /// <summary>
    /// A store's library: what is saved for the signed-in account, re-reading the owned
    /// list, and reading every game's achievement counts. The window keeps the games on
    /// screen and merges what comes back.
    /// </summary>
    /// <remarks>
    /// Callbacks run where the awaiting caller's synchronization context puts them: on the UI
    /// thread when called from a view model, on a pool thread in a plain test. They are
    /// invoked one at a time.
    /// </remarks>
    public interface IStoreLibraryService
    {
        /// <summary>The account's saved games, empty when nobody is signed in or the cache cannot be read.</summary>
        IReadOnlyList<CachedStoreGame> LoadCached(IStoreProvider provider);

        /// <summary>
        /// Re-reads the owned list. Never throws for a store failure; cancellation throws.
        /// </summary>
        Task<StoreLibraryRefresh> RefreshAsync(IStoreProvider provider, CancellationToken cancellationToken);

        /// <summary>
        /// Saves the owned list under the signed-in account, keeping the counts of games that
        /// are still owned. Returns null, or a sentence saying why it could not.
        /// </summary>
        Task<string> SaveLibraryAsync(IStoreProvider provider, IReadOnlyCollection<CachedStoreGame> games);

        /// <summary>Saves counts read for the signed-in account. A cache that cannot be written costs only a reload.</summary>
        void SaveProgress(IStoreProvider provider, IReadOnlyCollection<(string Id, int Earned, int Total)> results);

        /// <summary>
        /// Reads the achievements of each game, a few at a time, and saves the counts as it
        /// goes, so a cancelled run keeps what it has.
        /// </summary>
        /// <param name="games">Game id and key.</param>
        /// <param name="gameRead">Told each game's id, earned and total.</param>
        /// <param name="stepped">Told how many games are finished (read or failed) so far.</param>
        Task<StoreProgressRun> LoadProgressAsync(
            IStoreProvider provider,
            IReadOnlyCollection<(string Id, string Key)> games,
            Action<string, int, int> gameRead,
            Action<int> stepped,
            CancellationToken cancellationToken);
    }
}
