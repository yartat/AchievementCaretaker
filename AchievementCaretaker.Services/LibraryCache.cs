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
    /// The <see cref="ILibraryCache"/>: one stable object in front of a SQLite database that can
    /// be moved, or fail to open.
    /// </summary>
    /// <remarks>
    /// Moving the database replaces the connection behind it (SQLite's WAL sidecars are open, so
    /// the file cannot be moved under a live connection). Callers used to hold the connection
    /// and had to be handed a way to find the new one; they hold this instead, and it does.
    /// </remarks>
    public sealed class LibraryCache : ILibraryCache
    {
        private readonly object _Gate = new();
        private GameCache _Inner;
        private string _Path;

        /// <summary>Opens the database at <paramref name="databasePath"/>, or records why it could not be.</summary>
        public LibraryCache(string databasePath)
        {
            this._Path = databasePath;
            this.Open();
        }

        public bool IsAvailable => this._Inner != null;

        public string Error { get; private set; }

        private void Open()
        {
            try
            {
                this._Inner = GameCache.Open(this._Path);
                this.Error = null;
            }
            catch (Exception e)
            {
                this._Inner = null;
                this.Error = e.Message;
            }
        }

        public uint GetLastSteamAccount() => this._Inner?.GetLastSteamAccount() ?? 0;

        public void SetLastSteamAccount(uint account) => this._Inner?.SetLastSteamAccount(account);

        public IReadOnlyList<CachedGame> LoadAll(uint account) =>
            (IReadOnlyList<CachedGame>)this._Inner?.LoadAll(account) ?? Array.Empty<CachedGame>();

        public HashSet<uint> LoadAllGameIds() => this._Inner?.LoadAllGameIds() ?? new HashSet<uint>();

        public void Sync(uint account, IReadOnlyCollection<CachedGame> games, bool removeMissing = true) =>
            this._Inner?.Sync(account, games, removeMissing);

        public IReadOnlyList<CachedStoreGame> LoadStoreGames(string provider, string account) =>
            (IReadOnlyList<CachedStoreGame>)this._Inner?.LoadStoreGames(provider, account) ?? Array.Empty<CachedStoreGame>();

        public void SyncStoreGames(string provider, string account, IReadOnlyCollection<CachedStoreGame> games) =>
            this._Inner?.SyncStoreGames(provider, account, games);

        public void SaveStoreProgress(string provider, string account, IEnumerable<(string Id, int Earned, int Total)> progress) =>
            this._Inner?.SaveStoreProgress(provider, account, progress);

        public void MoveTo(string destination)
        {
            lock (this._Gate)
            {
                var previous = this._Path;
                try
                {
                    if (this._Inner != null)
                    {
                        // Closes the old connection, moves the files, and opens the new one.
                        this._Inner = GameCache.MoveTo(this._Inner, destination);
                        this._Path = destination;
                        return;
                    }

                    // Nothing was open, so there is nothing to move: just try the new place.
                    this._Path = destination;
                    this.Open();
                    if (this._Inner == null)
                    {
                        throw new InvalidOperationException(this.Error);
                    }
                }
                catch
                {
                    // Left where it was: the old database is opened again, so the cache is not
                    // lost to a failed move.
                    this._Path = previous;
                    this.Open();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            lock (this._Gate)
            {
                this._Inner?.Dispose();
                this._Inner = null;
            }
        }
    }
}
