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
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Gog;
using AchievementCaretaker.Providers.Gog.Models;
using AchievementCaretaker.Providers.Models;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>GOG's provider against a played-out GOG: its token endpoint, catalog, GOGDB and gameplay service.</summary>
public sealed class GogProviderTests
{
    private const string Path = "gog.json";
    private const string Code = "abcdefABCDEF0123456789_-xyz";

    private readonly FakeStore _http = new();
    private readonly MemorySessionStore _sessions = new();

    private GogProvider Provider() => new(_http, _sessions, Path);

    private static string Token(string access = "ACCESS", string refresh = "REFRESH", string user = "user-1", int expiresIn = 3600) =>
        $$"""{"access_token":"{{access}}","refresh_token":"{{refresh}}","user_id":"{{user}}","expires_in":{{expiresIn}}}""";

    /// <summary>A provider that already holds a live session, as one started with a saved sign-in does.</summary>
    private GogProvider SignedIn(bool accessExpired = false)
    {
        _sessions.Save(Path, new GogSession
        {
            AccessToken = "ACCESS",
            RefreshToken = "REFRESH",
            UserId = "user-1",
            DisplayName = "Ann",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(accessExpired ? -10 : 30),
        });
        return Provider();
    }

    #region Sign-in

    [Theory]
    [InlineData("https://embed.gog.com/on_login_success?origin=client&code=abc123", SignInResultKind.Address)]
    [InlineData("https://EMBED.GOG.COM/On_Login_Success?code=abc", SignInResultKind.Address)]
    [InlineData("https://embed.gog.com/on_login_success?origin=client", SignInResultKind.None)]
    [InlineData("https://embed.gog.com/other?code=abc", SignInResultKind.None)]
    [InlineData("https://evil.example/on_login_success?code=abc", SignInResultKind.None)]
    public void The_end_of_the_sign_in_is_recognised_by_its_address(string address, SignInResultKind expected)
    {
        Assert.Equal(expected, Provider().GetSignInResultKind(new Uri(address)));
    }

    [Fact]
    public void A_null_address_is_just_a_page_on_the_way()
    {
        Assert.Equal(SignInResultKind.None, Provider().GetSignInResultKind(null));
    }

    [Fact]
    public void Without_a_saved_session_the_provider_is_signed_out()
    {
        var provider = Provider();

        Assert.False(provider.IsSignedIn);
        Assert.Null(provider.AccountId);
        Assert.Null(provider.AccountName);
    }

    [Fact]
    public void A_saved_session_is_picked_up_at_construction()
    {
        var provider = SignedIn();

        Assert.True(provider.IsSignedIn);
        Assert.Equal("user-1", provider.AccountId);
        Assert.Equal("Ann", provider.AccountName);
    }

    [Fact]
    public async Task A_code_pasted_with_the_whole_address_signs_in_and_is_saved()
    {
        _http
            .Json("auth.gog.com/token", Token())
            .Json("userData.json", """{"username":"Ann"}""");
        var provider = Provider();

        await provider.SignInAsync(
            $"https://embed.gog.com/on_login_success?origin=client&code={Code}", TestContext.Current.CancellationToken);

        Assert.True(provider.IsSignedIn);
        Assert.Equal("user-1", provider.AccountId);
        Assert.Equal("Ann", provider.AccountName);
        var token = _http.RequestsTo("auth.gog.com/token").Single();
        Assert.Contains("grant_type=authorization_code", token.Url);
        Assert.Contains($"code={Code}", token.Url);
        Assert.True(_sessions.Has(Path));
        Assert.Equal("REFRESH", _sessions.Load<GogSession>(Path)!.RefreshToken);
    }

    [Fact]
    public async Task A_bare_code_works_too()
    {
        _http.Json("auth.gog.com/token", Token()).Json("userData.json", "{}");

        await Provider().SignInAsync("  " + Code + "  ", TestContext.Current.CancellationToken);

        Assert.Contains($"code={Code}", _http.RequestsTo("auth.gog.com/token").Single().Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it but is long enough")]
    public async Task Something_that_is_not_a_code_is_refused_without_asking_gog(string pasted)
    {
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(pasted, TestContext.Current.CancellationToken));

        Assert.Equal("invalid_code", error.ErrorCode);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Failing_to_fetch_the_name_does_not_fail_the_sign_in()
    {
        _http
            .Json("auth.gog.com/token", Token())
            .Json("userData.json", "{}", HttpStatusCode.InternalServerError);
        var provider = Provider();

        await provider.SignInAsync(Code, TestContext.Current.CancellationToken);

        Assert.True(provider.IsSignedIn);
        Assert.Null(provider.AccountName);
    }

    [Fact]
    public async Task A_spent_code_is_a_login_required_and_the_message_does_not_carry_it()
    {
        _http.Json(
            "auth.gog.com/token", """{"error":"invalid_grant","error_description":"Code expired"}""", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(Code, TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
        Assert.DoesNotContain(Code, error.Message);
    }

    [Fact]
    public async Task A_response_without_an_access_token_is_refused()
    {
        _http.Json("auth.gog.com/token", """{"refresh_token":"R"}""");

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(Code, TestContext.Current.CancellationToken));

        Assert.Equal("bad_response", error.ErrorCode);
    }

    [Fact]
    public void Signing_out_forgets_the_session_and_deletes_the_file()
    {
        var provider = SignedIn();

        provider.SignOut();

        Assert.False(provider.IsSignedIn);
        Assert.False(_sessions.Has(Path));
        Assert.Equal(1, _sessions.Deletes);
    }

    #endregion

    #region Keeping the session alive

    [Fact]
    public async Task A_live_token_is_used_without_a_request()
    {
        await SignedIn().EnsureSignedInAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task An_expired_token_is_refreshed_and_the_rotated_refresh_token_is_saved()
    {
        _http.Json("auth.gog.com/token", Token(access: "NEW-ACCESS", refresh: "NEW-REFRESH"));
        var provider = SignedIn(accessExpired: true);

        await provider.EnsureSignedInAsync(TestContext.Current.CancellationToken);

        var request = _http.RequestsTo("auth.gog.com/token").Single();
        Assert.Contains("grant_type=refresh_token", request.Url);
        Assert.Contains("refresh_token=REFRESH", request.Url);
        var saved = _sessions.Load<GogSession>(Path);
        Assert.Equal("NEW-REFRESH", saved!.RefreshToken);
        Assert.Equal("NEW-ACCESS", saved.AccessToken);
        // The name is kept across a refresh: the token response has none.
        Assert.Equal("Ann", provider.AccountName);
    }

    [Fact]
    public async Task A_refresh_that_returns_no_new_refresh_token_keeps_the_old_one()
    {
        _http.Json(
            "auth.gog.com/token", """{"access_token":"N","user_id":"user-1","expires_in":3600}""");
        var provider = SignedIn(accessExpired: true);

        await provider.EnsureSignedInAsync(TestContext.Current.CancellationToken);

        Assert.Equal("REFRESH", _sessions.Load<GogSession>(Path)!.RefreshToken);
    }

    [Fact]
    public async Task A_refused_refresh_token_is_login_required()
    {
        _http.Json(
            "auth.gog.com/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => SignedIn(accessExpired: true).EnsureSignedInAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
    }

    [Fact]
    public async Task Without_a_session_every_call_says_login_required()
    {
        var provider = Provider();

        await Assert.ThrowsAsync<ProviderException>(() => provider.EnsureSignedInAsync(TestContext.Current.CancellationToken));
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => provider.GetAchievementsAsync("1", "en", TestContext.Current.CancellationToken));
        Assert.Equal("login_required", error.ErrorCode);
    }

    #endregion

    #region Library

    private static string Product(long id, string title, string type = "game", string logo = "//images.gog/x_logo2x.png") =>
        "{\"id\":" + id + ",\"title\":\"" + title + "\",\"game_type\":\"" + type + "\",\"images\":{\"logo2x\":\"" + logo + "\"}}";

    [Fact]
    public async Task The_library_is_the_owned_games_with_their_titles_and_art_in_order()
    {
        _http
            .Json("embed.gog.com/user/data/games", """{"owned":[3,1,2]}""")
            .Json("api.gog.com/products", "[" + Product(1, "Beta") + "," + Product(2, "alpha") + "," + Product(3, "Gamma") + "]");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "alpha", "Beta", "Gamma" }, library.Select(g => g.Title));
        var beta = library.Single(g => g.Title == "Beta");
        Assert.Equal(new StoreGame("gog", "1", "1", "Beta", "https://images.gog/x_logo2x.png"), beta);
    }

    [Fact]
    public async Task Dlc_and_bundles_are_left_out_and_duplicate_ids_count_once()
    {
        _http
            .Json("embed.gog.com/user/data/games", """{"owned":[1,1,2,3]}""")
            .Json("api.gog.com/products", "[" + Product(1, "Game") + "," + Product(2, "Expansion", "dlc") + "," + Product(3, "Pack", "pack") + "]");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Game", Assert.Single(library).Title);
        Assert.Contains("ids=1,2,3", _http.RequestsTo("api.gog.com/products").Single().Url);
    }

    [Fact]
    public async Task A_game_without_a_title_or_art_is_listed_by_its_id()
    {
        _http
            .Json("embed.gog.com/user/data/games", """{"owned":[7]}""")
            .Json("api.gog.com/products", """[{"id":7,"game_type":"game"}]""");

        var game = Assert.Single(await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken));

        Assert.Equal("7", game.Title);
        Assert.Null(game.ImageUrl);
    }

    [Fact]
    public async Task A_large_library_is_asked_about_in_batches()
    {
        var ids = Enumerable.Range(1, 90).ToList();
        _http
            .Json("embed.gog.com/user/data/games", $$"""{"owned":[{{string.Join(",", ids)}}]}""")
            .Route("api.gog.com/products", request =>
            {
                var asked = request.Url.Split("ids=")[1].Split(',');
                return FakeStore.Reply(HttpStatusCode.OK, "[" + string.Join(",", asked.Select(id => Product(long.Parse(id), "G" + id))) + "]");
            });

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(90, library.Count);
        Assert.Equal(3, _http.RequestsTo("api.gog.com/products").Count());
        Assert.All(_http.RequestsTo("api.gog.com/products"), r => Assert.True(r.Url.Split("ids=")[1].Split(',').Length <= 40));
    }

    [Fact]
    public async Task A_batch_the_catalog_refuses_is_halved_until_it_is_taken()
    {
        _http
            .Json("embed.gog.com/user/data/games", """{"owned":[1,2,3,4]}""")
            .Route("api.gog.com/products", request =>
            {
                var asked = request.Url.Split("ids=")[1].Split(',');
                return asked.Length > 2
                    ? FakeStore.Reply(HttpStatusCode.BadRequest, "{}")
                    : FakeStore.Reply(HttpStatusCode.OK, "[" + string.Join(",", asked.Select(id => Product(long.Parse(id), "G" + id))) + "]");
            });

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "G1", "G2", "G3", "G4" }, library.Select(g => g.Title));
    }

    [Fact]
    public async Task An_empty_account_has_an_empty_library_and_no_catalog_call()
    {
        _http.Json("embed.gog.com/user/data/games", """{"owned":[]}""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Empty(library);
        Assert.Empty(_http.RequestsTo("api.gog.com"));
    }

    [Fact]
    public async Task The_library_request_carries_the_account_token_and_the_catalog_does_not()
    {
        _http
            .Json("embed.gog.com/user/data/games", """{"owned":[1]}""")
            .Json("api.gog.com/products", "[" + Product(1, "G") + "]");

        await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Bearer ACCESS", _http.RequestsTo("embed.gog.com").Single().Authorization);
        Assert.Null(_http.RequestsTo("api.gog.com").Single().Authorization);
    }

    #endregion

    #region Achievements

    private const string Build = """{"builds":[{"id":111,"date_published":"2020-01-01","listed":true},{"id":222,"date_published":"2021-01-01","listed":true},{"id":333,"date_published":"2022-01-01","listed":false}]}""";

    private FakeStore WithBuild(string clientId = "CLIENT", string secret = "SECRET")
    {
        // The newest listed build is 222; 333 is newer but no longer listed.
        return _http
            .Json("gogdb.org/data/products/10/product.json", Build)
            .Json("gogdb.org/data/products/10/builds/222.json", $$"""{"clientId":"{{clientId}}","clientSecret":"{{secret}}"}""");
    }

    private const string Items = """
        {"items":[
          {"achievement_id":"A1","name":"First","description":"Win once","visible":true,"image_url_unlocked":"https://i/u1","image_url_locked":"https://i/l1","date_unlocked":"2024-03-05T10:20:30+0000","rarity":12.5},
          {"achievement_id":"A2","name":"Secret","description":"Shh","visible":false,"image_url_unlocked":"https://i/u2","image_url_locked":"https://i/l2","date_unlocked":null,"rarity":1.0}
        ]}
        """;

    [Fact]
    public async Task Achievements_come_from_the_games_client_with_unlock_state_from_the_date()
    {
        WithBuild().Json("gameplay.gog.com/clients/CLIENT/users/user-1/achievements", Items);

        var list = await SignedIn().GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
        var first = list[0];
        Assert.Equal("A1", first.Id);
        Assert.Equal("First", first.DisplayName);
        Assert.Equal("Win once", first.Description);
        Assert.True(first.Unlocked);
        Assert.Equal(new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.Zero), first.UnlockDate);
        Assert.Equal("https://i/u1", first.IconUrl);
        Assert.Equal(12.5, first.RarityPercent);
        Assert.False(first.Hidden);
        Assert.Null(first.XP);
        Assert.Null(first.Progress);

        var second = list[1];
        Assert.False(second.Unlocked);
        Assert.Null(second.UnlockDate);
        Assert.True(second.Hidden);
        Assert.Equal("https://i/l2", second.IconUrl);
    }

    [Fact]
    public async Task The_achievements_request_carries_the_account_token()
    {
        WithBuild().Json("gameplay.gog.com/clients/CLIENT/users/user-1/achievements", Items);

        await SignedIn().GetAchievementsAsync("10", "de", TestContext.Current.CancellationToken);

        var request = _http.RequestsTo("gameplay.gog.com").Single();
        Assert.Equal("Bearer ACCESS", request.Authorization);
    }

    [Fact]
    public async Task The_newest_listed_build_is_the_one_whose_client_is_used()
    {
        WithBuild(clientId: "FROM-222").Json("gameplay.gog.com/clients/FROM-222/", Items);

        await SignedIn().GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Single(_http.RequestsTo("builds/222.json"));
        Assert.Empty(_http.RequestsTo("builds/333.json"));
    }

    [Fact]
    public async Task A_game_with_no_builds_has_no_achievements()
    {
        _http.Json("gogdb.org/data/products/10/product.json", "{}", HttpStatusCode.NotFound);

        var list = await SignedIn().GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Empty(list);
        Assert.Empty(_http.RequestsTo("gameplay.gog.com"));
    }

    [Fact]
    public async Task A_game_the_gameplay_service_does_not_know_has_no_achievements()
    {
        WithBuild().Json("gameplay.gog.com/clients/CLIENT/", "{}", HttpStatusCode.NotFound);

        var list = await SignedIn().GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Empty(list);
    }

    [Fact]
    public async Task The_client_id_is_looked_up_once_per_game()
    {
        WithBuild().Json("gameplay.gog.com/clients/CLIENT/", Items);
        var provider = SignedIn();

        await provider.GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);
        await provider.GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Single(_http.RequestsTo("gogdb.org/data/products/10/product.json"));
        Assert.Equal(2, _http.RequestsTo("gameplay.gog.com").Count());
    }

    [Fact]
    public async Task Reading_never_asks_for_a_token_for_the_game()
    {
        WithBuild().Json("gameplay.gog.com/clients/CLIENT/", Items);

        await SignedIn().GetAchievementsAsync("10", "en", TestContext.Current.CancellationToken);

        Assert.Empty(_http.RequestsTo("auth.gog.com"));
        Assert.DoesNotContain(_http.Requests, r => r.Url.Contains("SECRET", StringComparison.Ordinal));
    }

    #endregion

    #region Changing achievements

    [Fact]
    public async Task Unlocking_and_locking_post_to_the_game_with_a_token_issued_to_its_client()
    {
        WithBuild()
            .Json("auth.gog.com/token", """{"access_token":"GAME-TOKEN"}""")
            .Json("gameplay.gog.com/clients/CLIENT/users/user-1/achievements/", "{}", method: System.Net.Http.HttpMethod.Post);

        await SignedIn().SetAchievementsAsync(
            "10",
            new[] { new KeyValuePair<string, bool>("A1", true), new KeyValuePair<string, bool>("A2", false) },
            TestContext.Current.CancellationToken);

        var token = _http.RequestsTo("auth.gog.com/token").Single();
        Assert.Contains("client_id=CLIENT", token.Url);
        Assert.Contains("client_secret=SECRET", token.Url);
        Assert.Contains("without_new_session=1", token.Url);
        Assert.Contains("refresh_token=REFRESH", token.Url);

        var posts = _http.RequestsTo("/achievements/").Where(r => r.Method == System.Net.Http.HttpMethod.Post).ToList();
        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal("Bearer GAME-TOKEN", p.Authorization));
        var unlock = posts.Single(p => p.Url.EndsWith("/A1", StringComparison.Ordinal));
        var relock = posts.Single(p => p.Url.EndsWith("/A2", StringComparison.Ordinal));
        Assert.Matches("^\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\d\\+0000$", DateUnlocked(unlock.Body!));
        Assert.Equal("{\"date_unlocked\":null}", relock.Body);
    }

    /// <summary>The <c>date_unlocked</c> the provider sent; the JSON writer escapes the plus sign, so the body is read, not matched.</summary>
    private static string? DateUnlocked(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.GetProperty("date_unlocked").GetString();
    }

    [Fact]
    public async Task The_unlock_time_is_a_few_seconds_in_the_past()
    {
        WithBuild()
            .Json("auth.gog.com/token", """{"access_token":"G"}""")
            .Json("/achievements/", "{}", method: System.Net.Http.HttpMethod.Post);

        await SignedIn().SetAchievementsAsync(
            "10", new[] { new KeyValuePair<string, bool>("A1", true) }, TestContext.Current.CancellationToken);

        var stamp = DateUnlocked(_http.RequestsTo("/achievements/").Single().Body!);
        var when = DateTimeOffset.ParseExact(stamp!, "yyyy-MM-dd'T'HH:mm:ss'+0000'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.True(when < DateTimeOffset.UtcNow, "GOG refuses a date in the future");
    }

    [Fact]
    public async Task Nothing_to_change_makes_no_request()
    {
        await SignedIn().SetAchievementsAsync(
            "10", Array.Empty<KeyValuePair<string, bool>>(), TestContext.Current.CancellationToken);

        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task A_game_without_a_client_secret_cannot_be_changed()
    {
        WithBuild(secret: "");

        var error = await Assert.ThrowsAsync<ProviderException>(() => SignedIn().SetAchievementsAsync(
            "10", new[] { new KeyValuePair<string, bool>("A1", true) }, TestContext.Current.CancellationToken));

        Assert.Equal("no_client", error.ErrorCode);
        Assert.Empty(_http.RequestsTo("auth.gog.com"));
    }

    [Fact]
    public async Task A_refused_write_is_reported_and_the_secrets_are_not_in_the_message()
    {
        WithBuild()
            .Json("auth.gog.com/token", """{"access_token":"GAME-TOKEN"}""")
            .Json("/achievements/", """{"error":"forbidden","error_description":"Nope"}""", HttpStatusCode.Forbidden,
                System.Net.Http.HttpMethod.Post);

        var error = await Assert.ThrowsAsync<ProviderException>(() => SignedIn().SetAchievementsAsync(
            "10", new[] { new KeyValuePair<string, bool>("A1", true) }, TestContext.Current.CancellationToken));

        Assert.Equal("forbidden", error.ErrorCode);
        Assert.DoesNotContain("SECRET", error.Message);
        Assert.DoesNotContain("GAME-TOKEN", error.Message);
    }

    [Fact]
    public async Task The_accounts_own_session_is_not_replaced_by_the_game_token()
    {
        WithBuild()
            .Json("auth.gog.com/token", """{"access_token":"GAME-TOKEN","refresh_token":"GAME-REFRESH"}""")
            .Json("/achievements/", "{}", method: System.Net.Http.HttpMethod.Post);
        var provider = SignedIn();

        await provider.SetAchievementsAsync(
            "10", new[] { new KeyValuePair<string, bool>("A1", true) }, TestContext.Current.CancellationToken);

        Assert.Equal("ACCESS", provider.Session!.AccessToken);
        Assert.Equal("REFRESH", provider.Session.RefreshToken);
    }

    #endregion
}
