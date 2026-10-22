/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using Xunit;

namespace AchievementCaretaker.Services.Tests
{
    public sealed class StatValueParserTests
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static StoreStat Int(double value = 5, double min = 0, double max = 100, bool isProtected = false) =>
            new("S", "Stat", StoreStatKind.Integer, value, min, max, 0, false, 0, isProtected);

        private static StoreStat Float(double value = 1.5, bool isProtected = false) =>
            new("F", "Float", StoreStatKind.Float, value, -10, 10, 0, false, 0, isProtected);

        [Fact]
        public void A_valid_integer_is_taken()
        {
            Assert.True(StatValueParser.TryParse(Int(), "42", Invariant, out var value, out var error));
            Assert.Equal(42, value);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1.5")]
        [InlineData("99999999999")]
        public void Something_that_is_not_an_integer_is_refused(string text)
        {
            Assert.False(StatValueParser.TryParse(Int(), text, Invariant, out _, out var error));
            Assert.Equal("Invalid value", error);
        }

        [Fact]
        public void A_float_uses_the_cultures_decimal_separator()
        {
            Assert.True(StatValueParser.TryParse(Float(), "2,5", new CultureInfo("de-DE"), out var german, out _));
            Assert.Equal(2.5, german);
            Assert.True(StatValueParser.TryParse(Float(), "2.5", Invariant, out var plain, out _));
            Assert.Equal(2.5, plain);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void A_float_that_is_not_a_number_is_refused(string text)
        {
            Assert.False(StatValueParser.TryParse(Float(), text, Invariant, out _, out var error));
            Assert.Equal("Invalid value", error);
        }

        [Fact]
        public void A_value_outside_the_limits_is_refused_and_the_limits_are_named()
        {
            Assert.False(StatValueParser.TryParse(Int(), "101", Invariant, out _, out var high));
            Assert.False(StatValueParser.TryParse(Int(), "-1", Invariant, out _, out var low));

            Assert.Equal("Value must be between 0 and 100", high);
            Assert.Equal(high, low);
        }

        [Fact]
        public void The_limits_themselves_are_allowed()
        {
            Assert.True(StatValueParser.TryParse(Int(), "0", Invariant, out _, out _));
            Assert.True(StatValueParser.TryParse(Int(), "100", Invariant, out _, out _));
        }

        [Fact]
        public void A_protected_statistic_can_be_retyped_as_it_is_but_not_changed()
        {
            var stat = Int(value: 5, isProtected: true);

            Assert.True(StatValueParser.TryParse(stat, "5", Invariant, out _, out _));
            Assert.False(StatValueParser.TryParse(stat, "6", Invariant, out _, out var error));
            Assert.Contains("protected", error);
        }
    }

    public sealed class StatsServiceTests
    {
        private sealed class FakeStatsProvider : IStatsStoreProvider
        {
            public Exception Failure { get; set; }
            public List<KeyValuePair<string, double>> StatsSent { get; } = new();
            public List<KeyValuePair<string, bool>> AchievementsSent { get; } = new();
            public bool? ResetAchievements { get; private set; }

            public string Id => "fake";
            public string DisplayName => "Fake";
            public string SignInUrl => "";
            public string SignInHint => "";
            public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;
            public bool IsSignedIn => true;
            public string AccountName => null;
            public string AccountId => null;
            public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void SignOut() { }
            public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
                string gameKey, string locale = "en", CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task SetAchievementsAsync(
                string gameKey, IReadOnlyCollection<KeyValuePair<string, bool>> changes, CancellationToken cancellationToken = default) =>
                this.CommitAsync(gameKey, changes, Array.Empty<KeyValuePair<string, double>>(), cancellationToken);

            public Task<IReadOnlyList<StoreStat>> GetStatsAsync(string gameKey, CancellationToken cancellationToken = default)
            {
                if (this.Failure != null)
                {
                    throw this.Failure;
                }
                IReadOnlyList<StoreStat> list = new[]
                {
                    new StoreStat("S", "Stat", StoreStatKind.Integer, 1, 0, 9, 0, false, 0, false),
                };
                return Task.FromResult(list);
            }

            public Task CommitAsync(
                string gameKey,
                IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
                IReadOnlyCollection<KeyValuePair<string, double>> statistics,
                CancellationToken cancellationToken = default)
            {
                if (this.Failure != null)
                {
                    throw this.Failure;
                }
                this.AchievementsSent.AddRange(achievements);
                this.StatsSent.AddRange(statistics);
                return Task.CompletedTask;
            }

            public Task ResetAsync(string gameKey, bool includeAchievements, CancellationToken cancellationToken = default)
            {
                if (this.Failure != null)
                {
                    throw this.Failure;
                }
                this.ResetAchievements = includeAchievements;
                return Task.CompletedTask;
            }
        }

        private readonly StoreAchievementsService _Service = new();

        [Fact]
        public void Only_a_stats_provider_has_statistics()
        {
            Assert.True(this._Service.HasStatistics(new FakeStatsProvider()));
        }

        [Fact]
        public async Task Statistics_are_read()
        {
            var result = await this._Service.LoadStatsAsync(new FakeStatsProvider(), "g", TestContext.Current.CancellationToken);

            Assert.Null(result.Failure);
            Assert.Equal("S", Assert.Single(result.Stats).Id);
        }

        [Fact]
        public async Task A_statistics_failure_comes_back_as_text()
        {
            var provider = new FakeStatsProvider { Failure = new ProviderException("protected", "S is protected") };

            var result = await this._Service.LoadStatsAsync(provider, "g", TestContext.Current.CancellationToken);

            Assert.Empty(result.Stats);
            Assert.Equal("S is protected", result.Failure);
        }

        [Fact]
        public async Task Achievements_and_statistics_are_sent_in_one_commit()
        {
            var provider = new FakeStatsProvider();

            var failure = await this._Service.CommitAsync(
                provider,
                "g",
                new[] { new KeyValuePair<string, bool>("A", true) },
                new[] { new KeyValuePair<string, double>("S", 7) },
                TestContext.Current.CancellationToken);

            Assert.Null(failure);
            Assert.Equal(new[] { new KeyValuePair<string, bool>("A", true) }, provider.AchievementsSent);
            Assert.Equal(new[] { new KeyValuePair<string, double>("S", 7) }, provider.StatsSent);
        }

        [Fact]
        public async Task Statistics_cannot_be_sent_to_a_provider_that_has_none()
        {
            var failure = await this._Service.CommitAsync(
                new NoStatsProvider(),
                "g",
                Array.Empty<KeyValuePair<string, bool>>(),
                new[] { new KeyValuePair<string, double>("S", 7) },
                TestContext.Current.CancellationToken);

            Assert.Contains("statistics cannot be changed", failure);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_reset_passes_the_choice_on(bool achievementsToo)
        {
            var provider = new FakeStatsProvider();

            var failure = await this._Service.ResetAsync(provider, "g", achievementsToo, TestContext.Current.CancellationToken);

            Assert.Null(failure);
            Assert.Equal(achievementsToo, provider.ResetAchievements);
        }

        [Fact]
        public async Task A_reset_failure_comes_back_as_text()
        {
            var provider = new FakeStatsProvider { Failure = new InvalidOperationException("offline") };

            var failure = await this._Service.ResetAsync(provider, "g", false, TestContext.Current.CancellationToken);

            Assert.Equal("Could not reach Fake: offline", failure);
        }

        private sealed class NoStatsProvider : IStoreProvider
        {
            public string Id => "plain";
            public string DisplayName => "Plain";
            public string SignInUrl => "";
            public string SignInHint => "";
            public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;
            public bool IsSignedIn => true;
            public string AccountName => null;
            public string AccountId => null;
            public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void SignOut() { }
            public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
                string gameKey, string locale = "en", CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }
}
