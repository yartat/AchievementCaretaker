/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using Xunit;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services.Tests;

public sealed class StoreSessionServiceTests
{
    private sealed class FakeProvider : IStoreAccount
    {
        public Exception? SignInError { get; set; }
        public string? Pasted { get; private set; }
        public bool SignedOut { get; private set; }
        public string? AccountNameValue { get; set; }

        public string Id => "fake";
        public string DisplayName => "Fake";
        public string SignInUrl => "";
        public string SignInHint => "the code";
        public SignInResultKind GetSignInResultKind(Uri? address) => SignInResultKind.None;
        public bool IsSignedIn => true;
        public string? AccountName => AccountNameValue;
        public string AccountId => "a";

        public Task SignInAsync(string pasted, CancellationToken cancellationToken = default)
        {
            if (SignInError != null)
            {
                throw SignInError;
            }
            Pasted = pasted;
            return Task.CompletedTask;
        }

        public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignOut() => SignedOut = true;
    }

    private readonly StoreSessionService _service = new();

    [Fact]
    public async Task Blank_paste_is_refused_without_asking_the_store()
    {
        var provider = new FakeProvider();

        var result = await _service.SignInAsync(provider, "  ", TestContext.Current.CancellationToken);

        Assert.False(result.SignedIn);
        Assert.Equal("Paste the code first.", result.Error);
        Assert.Null(provider.Pasted);
    }

    [Fact]
    public async Task Sign_in_passes_the_text_on()
    {
        var provider = new FakeProvider();

        var result = await _service.SignInAsync(provider, "abc", TestContext.Current.CancellationToken);

        Assert.True(result.SignedIn);
        Assert.Equal("abc", provider.Pasted);
    }

    [Fact]
    public async Task Sign_in_failures_come_back_as_text()
    {
        var store = await _service.SignInAsync(
            new FakeProvider { SignInError = new ProviderException("bad", "Wrong code") }, "x", TestContext.Current.CancellationToken);
        var net = await _service.SignInAsync(
            new FakeProvider { SignInError = new InvalidOperationException("offline") }, "x", TestContext.Current.CancellationToken);

        Assert.Equal("Wrong code", store.Error);
        Assert.Equal("Could not reach Fake: offline", net.Error);
    }

    [Fact]
    public async Task Sign_in_lets_cancellation_through()
    {
        var provider = new FakeProvider { SignInError = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.SignInAsync(provider, "x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Sign_out_drops_the_session()
    {
        var provider = new FakeProvider();

        _service.SignOut(provider);

        Assert.True(provider.SignedOut);
    }

    [Fact]
    public void Confirmations_name_the_account_when_there_is_one()
    {
        var named = _service.ConfirmSignOut(new FakeProvider { AccountNameValue = "Ann" });
        var anonymous = _service.ConfirmSignOut(new FakeProvider());

        Assert.Contains("as Ann", named.Message);
        Assert.DoesNotContain(" as ", anonymous.Message);
        Assert.Contains("Ann's", _service.ConfirmSwitchAccount(new FakeProvider { AccountNameValue = "Ann" }).Message);
    }
}
