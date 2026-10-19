/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AchievementCaretaker.Services.Tests
{
    public sealed class LibraryCacheTests : IDisposable
    {
        private readonly string _Dir = Path.Combine(Path.GetTempPath(), "sac-test-" + Guid.NewGuid().ToString("N"));

        public LibraryCacheTests() => Directory.CreateDirectory(this._Dir);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(this._Dir, true); } catch (IOException) { }
        }

        private string Db(string name = "games.db") => Path.Combine(this._Dir, name);

        private static CachedGame Game(uint id) => new() { Id = id, Name = "Game " + id, Type = "normal" };

        [Fact]
        public void AccountsAreSeparate()
        {
            using var cache = new LibraryCache(this.Db());
            cache.Sync(1, new[] { Game(10), Game(11) });
            cache.Sync(2, new[] { Game(11), Game(12) });

            Assert.Equal(new uint[] { 10, 11 }, cache.LoadAll(1).Select(g => g.Id).Order());
            Assert.Equal(new uint[] { 11, 12 }, cache.LoadAll(2).Select(g => g.Id).Order());
            Assert.Equal(new uint[] { 10, 11, 12 }, cache.LoadAllGameIds().Order());
        }

        [Fact]
        public void PartialSyncKeepsWhatItDidNotReach()
        {
            using var cache = new LibraryCache(this.Db());
            cache.Sync(1, new[] { Game(10), Game(11) });

            cache.Sync(1, new[] { Game(10) }, removeMissing: false);
            Assert.Equal(2, cache.LoadAll(1).Count);

            cache.Sync(1, new[] { Game(10) });
            Assert.Single(cache.LoadAll(1));
        }

        [Fact]
        public void LastSteamAccountIsRemembered()
        {
            using (var cache = new LibraryCache(this.Db()))
            {
                Assert.Equal(0u, cache.GetLastSteamAccount());
                cache.SetLastSteamAccount(42);
            }

            using var again = new LibraryCache(this.Db());
            Assert.Equal(42u, again.GetLastSteamAccount());
        }

        [Fact]
        public void StoreProgressSurvivesARefreshAndIsPerAccount()
        {
            using var cache = new LibraryCache(this.Db());
            var list = new[] { new CachedStoreGame { Id = "a", Key = "a", Name = "A" } };
            cache.SyncStoreGames("gog", "u1", list);
            cache.SaveStoreProgress("gog", "u1", new[] { ("a", 3, 10) });

            cache.SyncStoreGames("gog", "u1", list);
            cache.SyncStoreGames("gog", "u2", list);

            var mine = cache.LoadStoreGames("gog", "u1").Single();
            Assert.True(mine.HasProgress);
            Assert.Equal((3, 10), (mine.AchievementsEarned, mine.AchievementsTotal));
            Assert.False(cache.LoadStoreGames("gog", "u2").Single().HasProgress);
        }

        [Fact]
        public void MoveCarriesTheDataAlong()
        {
            using var cache = new LibraryCache(this.Db());
            cache.Sync(1, new[] { Game(10) });

            var destination = Path.Combine(this._Dir, "moved", "games.db");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            cache.MoveTo(destination);

            Assert.True(File.Exists(destination));
            Assert.False(File.Exists(this.Db()));
            Assert.Single(cache.LoadAll(1));
        }

        [Fact]
        public void FailedMoveLeavesTheCacheWhereItWas()
        {
            using var cache = new LibraryCache(this.Db());
            cache.Sync(1, new[] { Game(10) });

            // A file where the destination's folder should be.
            var blocker = Path.Combine(this._Dir, "blocker");
            File.WriteAllText(blocker, "x");

            Assert.ThrowsAny<Exception>(() => cache.MoveTo(Path.Combine(blocker, "games.db")));

            Assert.True(cache.IsAvailable);
            Assert.Single(cache.LoadAll(1));
        }

        [Fact]
        public void UnavailableCacheReadsEmptyAndWritesNothing()
        {
            var blocker = Path.Combine(this._Dir, "blocker");
            File.WriteAllText(blocker, "x");

            using var cache = new LibraryCache(Path.Combine(blocker, "games.db"));

            Assert.False(cache.IsAvailable);
            Assert.False(string.IsNullOrEmpty(cache.Error));
            cache.Sync(1, new[] { Game(10) });
            Assert.Empty(cache.LoadAll(1));
            Assert.Empty(cache.LoadAllGameIds());
            Assert.Equal(0u, cache.GetLastSteamAccount());
        }
    }
}
