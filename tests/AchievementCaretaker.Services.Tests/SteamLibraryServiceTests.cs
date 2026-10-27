/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class SteamLibraryServiceTests
{
    private sealed class FakeScanProvider : ILibraryScanProvider
    {
        public IReadOnlyCollection<LibraryCandidate>? Scanned { get; private set; }
        public LibraryCandidate? Found { get; private set; }

        public string Id => "steam";
        public string DisplayName => "Steam";
        public string SignInUrl => "";
        public string SignInHint => "";
        public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;
        public bool IsSignedIn => true;
        public string AccountName => "n";
        public string AccountId => "1";
        public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignOut() { }

        public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey, string locale = "en", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LibraryScan> ScanLibraryAsync(
            IReadOnlyCollection<LibraryCandidate> candidates, IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken = default)
        {
            Scanned = candidates;
            return Task.FromResult(new LibraryScan("42", Array.Empty<StoreGame>(), true));
        }

        public Task<string> GetAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult("42");

        public Task<StoreGame?> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken = default)
        {
            Found = candidate;
            return Task.FromResult<StoreGame?>(null);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Respond { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Respond());
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, false);
    }

    private static byte[] Xml(string body) => Encoding.UTF8.GetBytes(body);

    private static SteamLibraryService Service(FakeScanProvider provider, Func<HttpResponseMessage> respond) =>
        new(provider, new FakeFactory(new FakeHandler { Respond = respond }));

    [Fact]
    public void The_published_list_is_read_with_its_types_and_defaults_to_normal()
    {
        var list = SteamLibraryService.ParseGameList(Xml(
            "<games><game type=\"demo\">480</game><game>105600</game><game type=\"\">7</game></games>"));

        Assert.Equal(
            new[] { new LibraryCandidate("480", "demo"), new LibraryCandidate("105600", "normal"), new LibraryCandidate("7", "normal") },
            list);
    }

    [Fact]
    public void An_empty_list_is_empty_and_not_an_error()
    {
        Assert.Empty(SteamLibraryService.ParseGameList(Xml("<games />")));
    }

    [Fact]
    public async Task The_list_is_downloaded_and_parsed()
    {
        var service = Service(new FakeScanProvider(), () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Xml("<games><game>480</game></games>")),
        });

        var list = await service.GetPublishedGamesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new LibraryCandidate("480", "normal"), Assert.Single(list));
    }

    [Fact]
    public async Task A_list_that_cannot_be_had_is_a_provider_error_with_a_reason()
    {
        var service = Service(new FakeScanProvider(), () => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => service.GetPublishedGamesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("list_unavailable", error.ErrorCode);
    }

    [Fact]
    public async Task A_list_that_is_not_xml_is_a_provider_error_too()
    {
        var service = Service(new FakeScanProvider(), () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Xml("not xml")),
        });

        await Assert.ThrowsAsync<ProviderException>(() => service.GetPublishedGamesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Scanning_and_looking_up_go_to_the_provider()
    {
        var provider = new FakeScanProvider();
        var service = Service(provider, () => throw new InvalidOperationException("no network here"));
        var candidates = new[] { new LibraryCandidate("480", "normal") };

        var scan = await service.ScanAsync(candidates, null, TestContext.Current.CancellationToken);
        var found = await service.FindOwnedAsync(480, TestContext.Current.CancellationToken);

        Assert.Equal("42", scan.Account);
        Assert.Equal(candidates, provider.Scanned!.ToArray());
        Assert.Null(found);
        Assert.Equal(new LibraryCandidate("480", "normal"), provider.Found);
        Assert.Equal("42", await service.GetAccountAsync(TestContext.Current.CancellationToken));
    }
}
