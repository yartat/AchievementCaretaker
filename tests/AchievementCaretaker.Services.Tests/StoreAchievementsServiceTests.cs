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
using Xunit;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services.Tests;

public sealed class StoreAchievementsServiceTests
{
    private sealed class FakeProvider : IWritableStoreProvider
    {
        public Exception? Throw { get; set; }
        public bool CanWrite { get; set; } = true;
        public bool CanRelock { get; set; } = true;
        public List<KeyValuePair<string, bool>> Written { get; } = new();

        public string Id => "fake";
        public string DisplayName => "Fake";
        public string SignInUrl => "";
        public string SignInHint => "";
        public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;
        public bool IsSignedIn => true;
        public string AccountName => "n";
        public string AccountId => "a";
        public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignOut() { }
        public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey, string locale = "en", CancellationToken cancellationToken = default)
        {
            if (Throw != null)
            {
                throw Throw;
            }
            IReadOnlyList<StoreAchievement> list = new[]
            {
                new StoreAchievement("a1", "One", "d", null, false, null, null, true, null, null),
            };
            return Task.FromResult(list);
        }

        public Task SetAchievementsAsync(
            string gameKey,
            IReadOnlyCollection<KeyValuePair<string, bool>> changes,
            CancellationToken cancellationToken = default)
        {
            if (Throw != null)
            {
                throw Throw;
            }
            Written.AddRange(changes);
            return Task.CompletedTask;
        }
    }

    private readonly StoreAchievementsService _service = new();

    [Fact]
    public async Task Load_returns_the_list()
    {
        var result = await _service.LoadAsync(new FakeProvider(), "g", TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Single(result.Achievements);
    }

    [Fact]
    public async Task Load_turns_login_required_into_a_sign_in_message()
    {
        var provider = new FakeProvider { Throw = new ProviderException("login_required", "x") };

        var result = await _service.LoadAsync(provider, "g", TestContext.Current.CancellationToken);

        Assert.Empty(result.Achievements);
        Assert.Contains("signed out of Fake", result.Failure);
    }

    [Fact]
    public async Task Load_keeps_a_provider_message_and_wraps_other_errors()
    {
        var own = await _service.LoadAsync(
            new FakeProvider { Throw = new ProviderException("other", "Boom") }, "g", TestContext.Current.CancellationToken);
        var net = await _service.LoadAsync(
            new FakeProvider { Throw = new InvalidOperationException("offline") }, "g", TestContext.Current.CancellationToken);

        Assert.Equal("Boom", own.Failure);
        Assert.Equal("Could not reach Fake: offline", net.Failure);
    }

    [Fact]
    public async Task Load_lets_cancellation_through()
    {
        var provider = new FakeProvider { Throw = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.LoadAsync(provider, "g", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_sends_the_changes_and_reports_success_as_null()
    {
        var provider = new FakeProvider();
        var changes = new[] { new KeyValuePair<string, bool>("a1", false) };

        var failure = await _service.CommitAsync(provider, "g", changes, TestContext.Current.CancellationToken);

        Assert.Null(failure);
        Assert.Equal(changes, provider.Written);
    }

    [Fact]
    public async Task Commit_reports_a_failure_as_text()
    {
        var provider = new FakeProvider { Throw = new ProviderException("login_required", "x") };

        var failure = await _service.CommitAsync(
            provider, "g", new[] { new KeyValuePair<string, bool>("a1", true) }, TestContext.Current.CancellationToken);

        Assert.Contains("signed out of Fake", failure);
    }

    [Fact]
    public void Only_a_writable_provider_can_modify()
    {
        Assert.True(_service.CanModify(new FakeProvider()));
    }

    [Fact]
    public void A_provider_that_cannot_write_now_or_cannot_relock_says_so()
    {
        Assert.True(_service.CanRelock(new FakeProvider()));
        Assert.False(_service.CanModify(new FakeProvider { CanWrite = false }));
        Assert.True(_service.CanModify(new FakeProvider { CanRelock = false }));
        Assert.False(_service.CanRelock(new FakeProvider { CanRelock = false }));
    }

    [Fact]
    public async Task A_commit_to_a_provider_that_cannot_write_now_is_refused_without_calling_it()
    {
        var provider = new FakeProvider { CanWrite = false };

        var failure = await _service.CommitAsync(
            provider, "g", new[] { new KeyValuePair<string, bool>("a", true) }, TestContext.Current.CancellationToken);

        Assert.NotNull(failure);
        Assert.Empty(provider.Written);
    }
}
