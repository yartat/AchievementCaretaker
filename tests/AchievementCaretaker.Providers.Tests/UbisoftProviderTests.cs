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
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Ubisoft;
using AchievementCaretaker.Providers.Ubisoft.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>The Ubisoft Connect provider over a launcher's files that a test lays out itself.</summary>
public sealed class UbisoftProviderTests : IAsyncDisposable
{
    private readonly UbisoftFiles _files = new();
    private readonly GameNames _names = GameNames.Parse(new[] { "920|Far Cry® 6", "4554|Anno 1800 (Uplay+Steam)", "7013|Assassin's Creed® Valhalla (Steam)" });

    public async ValueTask DisposeAsync()
    {
        await _demux.DisposeAsync();
        _files.Dispose();
    }

    private readonly FakeStore _cdn = new();

    private readonly MemorySessionStore _sessions = new();
    private readonly FakeDemuxServer _demux = new() { AcceptedTicket = "t2" };

    private UbisoftProvider Provider() =>
        new(_files.Locations, _files.IconDirectory, _files.DownloadDirectory, _cdn, _sessions, "ubisoft.json", _demux, _names);

    private const string SignedInAddress =
        "https://connect.ubisoft.com/change_domain/" +
        "%7B%22ticket%22%3A%22t1%22%2C%22sessionId%22%3A%22s1%22%2C%22userId%22%3A%22u1%22%2C%22nameOnPlatform%22%3A%22Player%22%7D%2C" +
        "%7B%22rememberMeTicket%22%3A%22r1%22%7D%2Cnull";

    private const string SessionAnswer =
        """{"ticket":"t2","sessionId":"s2","userId":"u1","nameOnPlatform":"Player","rememberMeTicket":"r2","expiration":"2026-10-27T13:00:00.000Z","serverTime":"2026-10-27T10:00:00.000Z"}""";

    /// <summary>A provider signed in to the online account, the way the sign-in window ends: with Ubisoft's page address.</summary>
    private async Task<UbisoftProvider> SignedInAsync()
    {
        _cdn.Route("/v3/profiles/sessions", _ => FakeStore.Reply(HttpStatusCode.OK, SessionAnswer), HttpMethod.Put);
        var provider = Provider();
        await provider.SignInAsync(SignedInAddress, TestContext.Current.CancellationToken);
        return provider;
    }

    private static string Yaml(string name, string? spec = null, bool game = true) =>
        "root:\n  name: " + name + "\n  thumb_image: t.jpg\n  background_image: b.jpg\n" +
        (game ? "  start_game:\n    online:\n      executables: []\n" : "") +
        (spec == null ? "" : "  uplay:\n    achievements: " + spec + "\n    achievements_sync_id: SYNC\n");

    private static byte[] Archive() => UbisoftFiles.Archive(
        new Dictionary<string, string>
        {
            ["en-US"] = "1\tFirst\tBegin\n2\tSecond\tContinue\n3\tThird\tFinish\n",
            ["ru-RU"] = "1\tПервое\tНачни\n2\tВторое\tПродолжи\n3\tТретье\tЗакончи\n",
        },
        new Dictionary<string, byte[]> { ["1.png"] = new byte[] { 1, 2, 3 }, ["3.png"] = new byte[] { 4, 5 } });

    private void LayOutLauncher()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((920, 99999), (7013, null), (4554, null), (123456, null)));
        _files.WriteArchive(920, Archive());
        _files.WriteSpool(920, UbisoftFiles.Spool((1, 1_730_000_000), (3, 1_730_100_000), (77, 1_730_200_000)));
        _files.WriteSpool(7013, UbisoftFiles.Spool((10, 1_600_000_000), (11, 1_600_000_100)));
    }

    #region Account

    [Fact]
    public void The_provider_is_a_store_and_a_local_account_that_can_also_be_signed_in_to()
    {
        var provider = Provider();

        Assert.IsAssignableFrom<IStoreProvider>(provider);
        var account = Assert.IsAssignableFrom<ILocalStoreAccount>(provider);
        Assert.Equal("ubisoft", account.Id);
        Assert.Equal("Ubisoft Connect", account.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(account.UnavailableMessage));
        Assert.False(account.IsSignedInOnline);
    }

    [Fact]
    public async Task Without_a_launcher_nobody_is_signed_in_and_nothing_can_be_read()
    {
        var provider = Provider();

        Assert.False(provider.IsSignedIn);
        Assert.Null(provider.AccountId);
        Assert.Null(provider.AccountName);
        var library = await Assert.ThrowsAsync<ProviderException>(() => provider.GetLibraryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("login_required", library.ErrorCode);
        var ensure = await Assert.ThrowsAsync<ProviderException>(() => provider.EnsureSignedInAsync(TestContext.Current.CancellationToken));
        Assert.Equal("login_required", ensure.ErrorCode);
        var achievements = await Assert.ThrowsAsync<ProviderException>(() => provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken));
        Assert.Equal("login_required", achievements.ErrorCode);
    }

    [Fact]
    public async Task With_a_cache_the_launchers_user_is_the_account_and_it_is_asked_each_time()
    {
        var provider = Provider();
        Assert.False(provider.IsSignedIn);

        _files.WriteOwnership(UbisoftFiles.Ownership((920, null)));

        Assert.True(provider.IsSignedIn);
        Assert.Equal(UbisoftFiles.UserId, provider.AccountId);
        Assert.False(string.IsNullOrEmpty(provider.AccountName));
        await provider.EnsureSignedInAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Signing_out_with_nobody_signed_in_changes_nothing()
    {
        LayOutLauncher();
        var provider = Provider();

        provider.SignOut();

        Assert.True(provider.IsSignedIn);
        Assert.False(provider.IsSignedInOnline);
        await Task.CompletedTask;
    }

    [Fact]
    public void The_end_of_the_sign_in_is_recognised_by_its_address_and_the_page_has_a_script()
    {
        var provider = Provider();

        Assert.Equal(SignInResultKind.Address, provider.GetSignInResultKind(new Uri(SignedInAddress)));
        Assert.Equal(SignInResultKind.None, provider.GetSignInResultKind(new Uri("https://connect.ubisoft.com/login?x=1")));
        Assert.Equal(SignInResultKind.None, provider.GetSignInResultKind(new Uri("https://evil.example/change_domain/x")));
        Assert.Equal(SignInResultKind.None, provider.GetSignInResultKind(null));
        Assert.StartsWith("https://connect.ubisoft.com/login", provider.SignInUrl);
        Assert.Contains("PRODloginData", provider.SignInScript);
    }

    [Fact]
    public async Task Signing_in_checks_the_session_with_ubisoft_and_keeps_what_it_answers()
    {
        var provider = await SignedInAsync();

        var request = Assert.Single(_cdn.RequestsTo("/v3/profiles/sessions"));
        Assert.Equal("Ubi_v1 t=t1", request.Authorization);
        Assert.Contains("f68a4bb5-608a-4ff2-8123-be8ef797e0a6", provider.SignInUrl);
        Assert.True(provider.IsSignedInOnline);
        Assert.True(provider.IsSignedIn);
        Assert.Equal("u1", provider.AccountId);
        Assert.Equal("Player", provider.AccountName);
        var saved = Assert.IsType<UbisoftSession>(_sessions.Load<UbisoftSession>("ubisoft.json"));
        Assert.Equal("t2", saved.Ticket);
        Assert.Equal("r2", saved.RememberMeTicket);
        Assert.True(saved.IsTicketValid);
        Assert.DoesNotContain("t2", saved.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://connect.ubisoft.com/change_domain/")]
    [InlineData("https://connect.ubisoft.com/change_domain/not json")]
    [InlineData("https://connect.ubisoft.com/change_domain/%7B%22ticket%22%3A%22t%22%7D")]
    public async Task An_address_that_carries_no_session_is_refused_and_nothing_is_asked_of_ubisoft(string address)
    {
        var provider = Provider();

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SignInAsync(address, TestContext.Current.CancellationToken));

        Assert.Equal("invalid_code", error.ErrorCode);
        Assert.False(provider.IsSignedInOnline);
        Assert.Empty(_cdn.Requests);
    }

    [Fact]
    public async Task A_ticket_ubisoft_refuses_is_replaced_through_the_remember_me_ticket()
    {
        _cdn.Route("/v3/profiles/sessions", _ => FakeStore.Reply(HttpStatusCode.Unauthorized, "{}"), HttpMethod.Put);
        _cdn.Route("/v3/profiles/sessions", _ => FakeStore.Reply(HttpStatusCode.OK, SessionAnswer), HttpMethod.Post);
        var provider = Provider();

        await provider.SignInAsync(SignedInAddress, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Ubi_v1 t=t1", "rm_v1 t=r1" }, _cdn.RequestsTo("/v3/profiles/sessions").Select(r => r.Authorization));
        Assert.True(provider.IsSignedInOnline);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_sign_in_is_kept_when_the_renewal_that_follows_it_fails_because_the_socket_service_is_what_judges_the_ticket(HttpStatusCode status)
    {
        _cdn.Route("/v3/profiles/sessions", _ => FakeStore.Reply(status, "{}"));
        var provider = Provider();

        await provider.SignInAsync(SignedInAddress, TestContext.Current.CancellationToken);

        Assert.True(provider.IsSignedInOnline);
        Assert.Equal("u1", provider.AccountId);
        var saved = Assert.IsType<UbisoftSession>(_sessions.Load<UbisoftSession>("ubisoft.json"));
        Assert.Equal("t1", saved.Ticket);
        Assert.True(saved.IsTicketValid);
    }

    [Fact]
    public async Task Signing_out_forgets_the_online_account_and_the_launchers_is_read_again()
    {
        LayOutLauncher();
        var provider = await SignedInAsync();
        Assert.Equal("u1", provider.AccountId);

        provider.SignOut();

        Assert.False(provider.IsSignedInOnline);
        Assert.True(provider.IsSignedIn);
        Assert.Equal(UbisoftFiles.UserId, provider.AccountId);
        Assert.Equal("this computer", provider.AccountName);
        Assert.False(_sessions.Has("ubisoft.json"));
    }

    #endregion

    #region Library

    [Fact]
    public async Task The_library_is_the_owned_ids_that_have_a_name_in_order_of_name()
    {
        LayOutLauncher();

        var library = await Provider().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Anno 1800", "Assassin's Creed Valhalla", "Far Cry 6" }, library.Select(g => g.Title));
        Assert.All(library, g =>
        {
            Assert.Equal("ubisoft", g.Provider);
            Assert.Equal(g.Id, g.Key);
            Assert.Null(g.ImageUrl);
        });
    }

    [Fact]
    public async Task A_game_with_an_archive_lists_its_counts_and_one_without_leaves_them_unknown()
    {
        LayOutLauncher();

        var library = (await Provider().GetLibraryAsync(TestContext.Current.CancellationToken)).ToDictionary(g => g.Id);

        // Three in the archive, two of them in the spool; the spool's third record is not in the archive.
        Assert.Equal(3, library["920"].AchievementsTotal);
        Assert.Equal(2, library["920"].AchievementsEarned);
        // A spool without an archive is not a count: there is no total to put it against.
        Assert.Null(library["7013"].AchievementsTotal);
        Assert.Null(library["7013"].AchievementsEarned);
        Assert.Null(library["4554"].AchievementsTotal);
    }

    [Fact]
    public async Task The_catalog_names_the_games_gives_their_pictures_and_leaves_out_what_is_not_a_game()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((1, null), (2, 200), (3, null), (4, null)));
        _files.WriteConfigurations(UbisoftFiles.Configurations(
            (1, null, Yaml("Alpha")),
            (2, 200, Yaml("l1") + "localizations:\n  default:\n    l1: Beta Game®\n"),
            (3, null, Yaml("Add-on", game: false)),
            (4, null, Yaml("name"))));

        var library = await Provider().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Alpha", "Beta Game" }, library.Select(g => g.Title));
        Assert.All(library, g => Assert.Equal("https://static3.cdn.ubi.com/orbit/uplay_launcher_3_0/assets/b.jpg", g.ImageUrl));
        // A game the catalog knows and names no list for has none: zero, not unknown.
        Assert.Equal(0, library[0].AchievementsTotal);
    }

    #endregion

    #region Achievements

    [Fact]
    public async Task An_archive_the_launcher_has_not_saved_is_downloaded_once_from_the_cdn_and_kept()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((920, null)));
        _files.WriteConfigurations(UbisoftFiles.Configurations((920, null, Yaml("Far Cry 6", "abc123.zip"))));
        _files.WriteSpool(920, UbisoftFiles.Spool((2, 1_730_000_000)));
        _cdn.Route("/assets/abc123.zip", _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.ByteArrayContent(Archive()),
        });
        var provider = Provider();

        var first = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        var second = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { false, true, false }, first.Select(a => a.Unlocked));
        Assert.Equal(first.Count, second.Count);
        Assert.Single(_cdn.Requests);
        Assert.True(File.Exists(Path.Combine(_files.DownloadDirectory, "920_abc123.zip")));
        // The meter is known from the kept archive without asking again.
        var library = await provider.GetLibraryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, library[0].AchievementsTotal);
        Assert.Equal(1, library[0].AchievementsEarned);
        Assert.Single(_cdn.Requests);
    }

    [Fact]
    public async Task A_game_the_catalog_gives_no_list_has_an_empty_one_and_nothing_is_asked_of_the_cdn()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((5, null)));
        _files.WriteConfigurations(UbisoftFiles.Configurations((5, null, Yaml("Plain"))));

        var achievements = await Provider().GetAchievementsAsync("5", "en", TestContext.Current.CancellationToken);

        Assert.Empty(achievements);
        Assert.Empty(_cdn.Requests);
    }

    [Fact]
    public async Task A_cdn_that_cannot_be_reached_is_a_network_error_and_leaves_no_file()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((920, null)));
        _files.WriteConfigurations(UbisoftFiles.Configurations((920, null, Yaml("Far Cry 6", "abc123.zip"))));
        _cdn.Route("/assets/", _ => throw new System.Net.Http.HttpRequestException("down"));

        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider().GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken));

        Assert.Equal("network", error.ErrorCode);
        Assert.False(Directory.Exists(_files.DownloadDirectory) && Directory.GetFiles(_files.DownloadDirectory).Length > 0);
    }

    [Fact]
    public async Task A_games_achievements_are_the_archives_with_the_dates_from_the_spool()
    {
        LayOutLauncher();

        var achievements = await Provider().GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "1", "2", "3" }, achievements.Select(a => a.Id));
        Assert.Equal(new[] { "First", "Second", "Third" }, achievements.Select(a => a.DisplayName));
        Assert.Equal("Continue", achievements[1].Description);
        Assert.Equal(new[] { true, false, true }, achievements.Select(a => a.Unlocked));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_730_000_000), achievements[0].UnlockDate);
        Assert.Null(achievements[1].UnlockDate);
        Assert.All(achievements, a =>
        {
            Assert.Null(a.XP);
            Assert.Null(a.RarityPercent);
            Assert.Null(a.Progress);
            Assert.False(a.Hidden);
            Assert.False(a.Protected);
        });
    }

    [Fact]
    public async Task The_language_asked_for_is_the_language_of_the_names()
    {
        LayOutLauncher();

        var achievements = await Provider().GetAchievementsAsync("920", "ru", TestContext.Current.CancellationToken);

        Assert.Equal("Первое", achievements[0].DisplayName);
    }

    [Fact]
    public async Task The_icons_are_taken_out_of_the_archive_into_files_whose_addresses_are_given()
    {
        LayOutLauncher();

        var achievements = await Provider().GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);

        var first = new Uri(achievements[0].IconUrl!);
        Assert.True(first.IsFile);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first.LocalPath));
        Assert.Null(achievements[1].IconUrl);
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(new Uri(achievements[2].IconUrl!).LocalPath));
        Assert.StartsWith(_files.IconDirectory, first.LocalPath);
    }

    [Fact]
    public async Task An_icon_older_than_its_archive_is_written_again_and_a_newer_one_is_left_alone()
    {
        LayOutLauncher();
        var provider = Provider();
        var first = new Uri((await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken))[0].IconUrl!).LocalPath;

        File.WriteAllBytes(first, new byte[] { 42 });
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(1));
        await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(first));

        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-10));
        await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
    }

    [Fact]
    public async Task A_game_with_no_archive_has_no_list_and_says_why_and_how_many_the_spool_has()
    {
        LayOutLauncher();

        var withSpool = await Assert.ThrowsAsync<ProviderException>(() => Provider().GetAchievementsAsync("7013", "en", TestContext.Current.CancellationToken));
        var without = await Assert.ThrowsAsync<ProviderException>(() => Provider().GetAchievementsAsync("4554", "en", TestContext.Current.CancellationToken));

        Assert.Equal("no_local_data", withSpool.ErrorCode);
        Assert.Contains("2 achievements are recorded as earned here", withSpool.Message);
        Assert.Equal("no_local_data", without.ErrorCode);
        Assert.DoesNotContain("recorded as earned", without.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("far-cry")]
    [InlineData("-1")]
    public async Task A_key_that_is_not_a_game_id_is_refused(string key)
    {
        LayOutLauncher();

        var error = await Assert.ThrowsAsync<ProviderException>(() => Provider().GetAchievementsAsync(key, "en", TestContext.Current.CancellationToken));

        Assert.Equal("invalid_game", error.ErrorCode);
    }

    #endregion

    #region Online

    [Fact]
    public async Task An_account_signed_in_to_lists_its_games_from_ubisoft_without_the_launcher()
    {
        _demux.Products.Add(new FakeProduct(920, Configuration: Yaml("Far Cry 6", "abc123.zip")));
        _demux.Products.Add(new FakeProduct(5, Type: 1, Configuration: Yaml("An add-on")));
        _demux.Products.Add(new FakeProduct(6, Owned: false, Configuration: Yaml("Not mine")));
        _demux.Products.Add(new FakeProduct(7));
        _demux.Blocks[7] = Yaml("Asked For");
        var provider = await SignedInAsync();

        var library = await provider.GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Asked For", "Far Cry 6" }, library.Select(g => g.Title));
        Assert.All(library, g => Assert.Equal("https://static3.cdn.ubi.com/orbit/uplay_launcher_3_0/assets/b.jpg", g.ImageUrl));
        Assert.Equal(new[] { "7", "920" }.OrderBy(k => k).ToArray(), library.Select(g => g.Key).OrderBy(k => k).ToArray());
        // Nothing is known of what is earned until a game is opened, but a game the catalog gives no list for has none.
        Assert.Equal(0, library.Single(g => g.Key == "7").AchievementsTotal);
        Assert.Null(library.Single(g => g.Key == "920").AchievementsTotal);
        Assert.Equal(1, _demux.Connections);
    }

    [Fact]
    public async Task What_ubisoft_has_recorded_as_earned_is_what_the_list_shows_and_the_connection_is_kept()
    {
        _demux.Products.Add(new FakeProduct(920, Configuration: Yaml("Far Cry 6", "abc123.zip")));
        _demux.Blocks[920] = Yaml("Far Cry 6", "abc123.zip");
        _demux.Earned["SYNC"] = new[] { (1u, 1_730_000_000UL), (3u, 1_730_100_000UL) };
        _cdn.Route("/assets/abc123.zip", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) });
        var provider = await SignedInAsync();

        var first = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        var second = await provider.GetAchievementsAsync("920", "ru", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { true, false, true }, first.Select(a => a.Unlocked));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_730_000_000), first[0].UnlockDate);
        Assert.Equal("Первое", second[0].DisplayName);
        Assert.Equal("u1", _demux.LastAchievementUser);
        Assert.Equal("SYNC", _demux.LastAchievementProduct);
        Assert.Equal("PC", _demux.LastAchievementPlatform);
        Assert.Equal(1, _demux.Connections);
    }

    [Fact]
    public async Task The_spool_of_this_computer_is_not_mixed_into_an_online_account()
    {
        _files.WriteOwnership(UbisoftFiles.Ownership((920, null)));
        _files.WriteSpool(920, UbisoftFiles.Spool((2, 1_730_000_000)));
        _demux.Blocks[920] = Yaml("Far Cry 6", "abc123.zip");
        _cdn.Route("/assets/abc123.zip", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) });
        var provider = await SignedInAsync();

        var achievements = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);

        Assert.All(achievements, a => Assert.False(a.Unlocked));
    }

    private async Task<UbisoftProvider> WritableAsync()
    {
        _demux.Products.Add(new FakeProduct(920, Configuration: Yaml("Far Cry 6", "abc123.zip")));
        _demux.Blocks[920] = Yaml("Far Cry 6", "abc123.zip");
        _demux.Earned["SYNC"] = new[] { (1u, 1_730_000_000UL) };
        _cdn.Route("/assets/abc123.zip", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) });
        return await SignedInAsync();
    }

    private static KeyValuePair<string, bool>[] Unlock(params string[] ids) => ids.Select(id => new KeyValuePair<string, bool>(id, true)).ToArray();

    [Fact]
    public async Task Unlocking_sends_the_whole_earned_list_with_the_old_times_and_the_new_one_stamped_now()
    {
        var provider = await WritableAsync();
        Assert.True(provider.CanWrite);
        Assert.False(provider.CanRelock);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await provider.SetAchievementsAsync("920", Unlock("2"), TestContext.Current.CancellationToken);

        Assert.Equal(1, _demux.WriteCalls);
        Assert.Equal((1u, 1_730_000_000UL), _demux.LastWrite[0]);
        Assert.Equal(2u, _demux.LastWrite[1].Id);
        Assert.InRange((long)_demux.LastWrite[1].Time, before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var list = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { true, true, false }, list.Select(a => a.Unlocked));
    }

    [Fact]
    public async Task What_is_already_earned_is_not_written_again_and_is_shown_as_fixed()
    {
        var provider = await WritableAsync();

        await provider.SetAchievementsAsync("920", Unlock("1"), TestContext.Current.CancellationToken);

        Assert.Equal(0, _demux.WriteCalls);
        var list = await provider.GetAchievementsAsync("920", "en", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { true, false, false }, list.Select(a => a.Protected));
    }

    [Fact]
    public async Task A_lock_is_refused_before_anything_is_sent()
    {
        var provider = await WritableAsync();

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SetAchievementsAsync(
            "920", new[] { new KeyValuePair<string, bool>("2", true), new KeyValuePair<string, bool>("1", false) }, TestContext.Current.CancellationToken));

        Assert.Equal("cannot_relock", error.ErrorCode);
        Assert.Equal(0, _demux.WriteCalls);
    }

    [Fact]
    public async Task An_achievement_the_game_does_not_have_is_refused_before_anything_is_sent()
    {
        var provider = await WritableAsync();

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SetAchievementsAsync("920", Unlock("999"), TestContext.Current.CancellationToken));

        Assert.Equal("invalid_achievement", error.ErrorCode);
        Assert.Equal(0, _demux.WriteCalls);
    }

    [Fact]
    public async Task A_write_the_service_does_not_keep_is_reported_and_not_taken_for_done()
    {
        var provider = await WritableAsync();
        _demux.IgnoreWrites = true;

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SetAchievementsAsync("920", Unlock("2"), TestContext.Current.CancellationToken));

        Assert.Equal("not_recorded", error.ErrorCode);
    }

    [Fact]
    public async Task Without_a_sign_in_nothing_can_be_written_and_nothing_is_offered()
    {
        LayOutLauncher();
        var provider = Provider();

        Assert.False(provider.CanWrite);
        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.SetAchievementsAsync("920", Unlock("2"), TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
        Assert.Equal(0, _demux.WriteCalls);
    }

    [Fact]
    public async Task A_ticket_the_socket_service_refuses_asks_for_a_new_sign_in()
    {
        _demux.AcceptedTicket = "another";
        var provider = await SignedInAsync();

        var error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetLibraryAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
    }

    #endregion

    #region Registration

    [Fact]
    public void On_windows_the_provider_is_registered_as_a_store_and_as_an_account_and_it_is_the_same_object()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Ubisoft Connect is read only on Windows.");

        using var services = new ServiceCollection()
            .AddUbisoftProvider(_files.IconDirectory)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var store = Assert.Single(services.GetServices<IStoreProvider>());
        var account = Assert.Single(services.GetServices<IStoreAccount>());
        Assert.Same(store, account);
        Assert.Equal("ubisoft", store.Id);
    }

    [Fact]
    public void Off_windows_nothing_is_registered_so_there_is_no_such_source()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Only where Ubisoft Connect does not run.");

        using var services = new ServiceCollection()
            .AddUbisoftProvider(_files.IconDirectory)
            .BuildServiceProvider();

        Assert.Empty(services.GetServices<IStoreProvider>());
        Assert.Empty(services.GetServices<IStoreAccount>());
    }

    #endregion
}
