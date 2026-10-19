/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;

namespace AchievementCaretaker.Services
{
    /// <summary>
    /// The saved libraries: what the window paints before any network or Steam work has
    /// started, and what a refresh writes back. One per process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every library is saved under its account, so signing out or switching account deletes
    /// nothing. A Steam library is keyed by the 32-bit Steam account id, a store's by its
    /// provider id and the store's own account id.
    /// </para>
    /// <para>
    /// A cache that cannot be opened must not stop the application, it only means every start is
    /// a cold one. That case is a cache whose <see cref="IsAvailable"/> is false: reads are
    /// empty, writes do nothing, and <see cref="Error"/> says why.
    /// </para>
    /// </remarks>
    public interface ILibraryCache : IDisposable
    {
        bool IsAvailable { get; }

        /// <summary>Why the cache could not be opened, or null.</summary>
        string Error { get; }

        /// <summary>The Steam account the library window showed last, or 0 if none yet.</summary>
        uint GetLastSteamAccount();

        void SetLastSteamAccount(uint account);

        IReadOnlyList<CachedGame> LoadAll(uint account);

        /// <summary>
        /// Every app id any account has cached. What the icon cache must keep: art that belongs
        /// to a game one account owns is not stale just because another does not.
        /// </summary>
        HashSet<uint> LoadAllGameIds();

        /// <summary>
        /// Makes one account's rows match <paramref name="games"/>. With
        /// <paramref name="removeMissing"/> false it only adds and updates: a synchronization
        /// that did not finish learned about the games it reached and nothing about the rest.
        /// </summary>
        void Sync(uint account, IReadOnlyCollection<CachedGame> games, bool removeMissing = true);

        IReadOnlyList<CachedStoreGame> LoadStoreGames(string provider, string account);

        /// <summary>
        /// Makes one account's rows at one provider match <paramref name="games"/>. The
        /// achievement counts of a game that is still owned are kept: a refreshed list says
        /// nothing about progress, and finding it out again costs requests for every game.
        /// </summary>
        void SyncStoreGames(string provider, string account, IReadOnlyCollection<CachedStoreGame> games);

        void SaveStoreProgress(string provider, string account, IEnumerable<(string Id, int Earned, int Total)> progress);

        /// <summary>
        /// Moves the database to <paramref name="destination"/> and carries on there. If the
        /// move fails the cache is reopened where it was, and the failure is thrown.
        /// </summary>
        void MoveTo(string destination);
    }
}
