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
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using Xunit;

namespace AchievementCaretaker.Services.Tests
{
    public sealed class StoreLibraryServiceTests : IDisposable
    {
        private sealed class FakeProvider : IStoreProvider
        {
            public string AccountIdValue { get; set; } = "acct";
            public Func<string, IReadOnlyList<StoreAchievement>> Achievements { get; set; }
            public Exception LibraryError { get; set; }
            public IReadOnlyList<StoreGame> Library { get; set; } = Array.Empty<StoreGame>();

            public string Id => "fake";
            public string DisplayName => "Fake";
            public string SignInUrl => "";
            public string SignInHint => "";
            public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;
            public bool IsSignedIn => this.AccountIdValue != null;
            public string AccountName => "n";
            public string AccountId => this.AccountIdValue;
            public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void SignOut() { }

            public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default)
            {
                if (this.LibraryError != null)
                {
                    throw this.LibraryError;
                }
                return Task.FromResult(this.Library);
            }

            public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
                string gameKey, string locale = "en", CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(this.Achievements(gameKey));
            }
        }

        private static StoreAchievement Ach(bool unlocked) =>
            new("x", "n", "d", null, false, null, null, unlocked, null, null);

        private readonly string _Dir = Path.Combine(Path.GetTempPath(), "sac-test-" + Guid.NewGuid().ToString("N"));
        private readonly LibraryCache _Cache;
        private readonly StoreLibraryService _Service;

        public StoreLibraryServiceTests()
        {
            Directory.CreateDirectory(this._Dir);
            this._Cache = new LibraryCache(Path.Combine(this._Dir, "games.db"));
            this._Service = new StoreLibraryService(this._Cache);
        }

        public void Dispose()
        {
            this._Cache.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(this._Dir, true); } catch (IOException) { }
        }

        [Fact]
        public async Task Saved_library_is_loaded_back_for_the_same_account_only()
        {
            var provider = new FakeProvider();
            var rows = new[] { new CachedStoreGame { Id = "1", Key = "k1", Name = "One" } };

            Assert.Null(await this._Service.SaveLibraryAsync(provider, rows));

            Assert.Equal("One", Assert.Single(this._Service.LoadCached(provider)).Name);
            provider.AccountIdValue = "other";
            Assert.Empty(this._Service.LoadCached(provider));
            provider.AccountIdValue = null;
            Assert.Empty(this._Service.LoadCached(provider));
        }

        [Fact]
        public async Task Refresh_returns_the_library()
        {
            var provider = new FakeProvider { Library = new[] { new StoreGame("fake", "1", "k1", "One", null) } };

            var refresh = await this._Service.RefreshAsync(provider, TestContext.Current.CancellationToken);

            Assert.Single(refresh.Library);
            Assert.False(refresh.SessionExpired);
            Assert.Null(refresh.Failure);
        }

        [Fact]
        public async Task Refresh_reports_an_expired_session_and_other_failures()
        {
            var expired = await this._Service.RefreshAsync(
                new FakeProvider { LibraryError = new ProviderException("login_required", "x") },
                TestContext.Current.CancellationToken);
            var down = await this._Service.RefreshAsync(
                new FakeProvider { LibraryError = new InvalidOperationException("offline") },
                TestContext.Current.CancellationToken);

            Assert.True(expired.SessionExpired);
            Assert.Null(expired.Library);
            Assert.Equal("Could not reach Fake: offline", down.Failure);
        }

        [Fact]
        public async Task Progress_run_reads_every_game_saves_counts_and_reports_them()
        {
            var provider = new FakeProvider
            {
                Achievements = key => key == "k2" ? new[] { Ach(true), Ach(false), Ach(true) } : new[] { Ach(false) },
            };
            var games = new[] { ("1", "k1"), ("2", "k2") };
            this._Cache.SyncStoreGames("fake", "acct", new[]
            {
                new CachedStoreGame { Id = "1", Key = "k1", Name = "One" },
                new CachedStoreGame { Id = "2", Key = "k2", Name = "Two" },
            });
            Dictionary<string, (int, int)> seen = new();

            var run = await this._Service.LoadProgressAsync(
                provider, games, (id, earned, total) => seen[id] = (earned, total), _ => { },
                TestContext.Current.CancellationToken);

            Assert.Equal(2, run.Done);
            Assert.Equal(0, run.Failed);
            Assert.Equal((2, 3), seen["2"]);
            var saved = this._Service.LoadCached(provider).Single(g => g.Id == "2");
            Assert.True(saved.HasProgress);
            Assert.Equal((2, 3), (saved.AchievementsEarned, saved.AchievementsTotal));
        }

        [Fact]
        public async Task Progress_run_counts_a_failing_game_and_goes_on()
        {
            var provider = new FakeProvider
            {
                Achievements = key => key == "bad" ? throw new InvalidOperationException() : new[] { Ach(true) },
            };

            var run = await this._Service.LoadProgressAsync(
                provider, new[] { ("1", "ok"), ("2", "bad") }, null, null, TestContext.Current.CancellationToken);

            Assert.Equal(2, run.Done);
            Assert.Equal(1, run.Failed);
            Assert.False(run.SessionExpired);
        }

        [Fact]
        public async Task Progress_run_stops_when_the_session_expires()
        {
            var provider = new FakeProvider
            {
                Achievements = _ => throw new ProviderException("login_required", "x"),
            };
            var games = Enumerable.Range(0, 20).Select(i => (i.ToString(), "k" + i)).ToList();

            var run = await this._Service.LoadProgressAsync(provider, games, null, null, TestContext.Current.CancellationToken);

            Assert.True(run.SessionExpired);
            Assert.False(run.Cancelled);
            Assert.True(run.Done < games.Count);
        }

        [Fact]
        public async Task Progress_run_that_is_cancelled_says_so()
        {
            var provider = new FakeProvider { Achievements = _ => new[] { Ach(true) } };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var run = await this._Service.LoadProgressAsync(
                provider, new[] { ("1", "k1") }, null, null, cts.Token);

            Assert.True(run.Cancelled);
            Assert.Equal(0, run.Done);
        }
    }
}
