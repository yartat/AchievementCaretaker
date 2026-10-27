/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Epic.Models;
using AchievementCaretaker.Providers.Models;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>Epic's provider against a played-out Epic: its OAuth service, library, catalog and GraphQL store.</summary>
public sealed class EpicProviderTests
{
    private const string Path = "epic.json";
    private const string Code = "0123456789abcdef0123456789abcdef";

    private readonly FakeStore _http = new();
    private readonly MemorySessionStore _sessions = new();

    private EpicProvider Provider() => new(_http, _sessions, Path);

    private static string Token(string access = "ACCESS", string refresh = "REFRESH", string account = "acct-1", int accessHours = 2, int refreshDays = 30)
    {
        var now = DateTimeOffset.UtcNow;
        return JsonSerializer.Serialize(new
        {
            access_token = access,
            expires_at = now.AddHours(accessHours),
            refresh_token = refresh,
            refresh_expires_at = now.AddDays(refreshDays),
            account_id = account,
            displayName = "Ann",
        });
    }

    private EpicProvider SignedIn(bool accessExpired = false, bool refreshExpired = false)
    {
        _sessions.Save(Path, new EpicSession
        {
            AccessToken = "ACCESS",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(accessExpired ? -10 : 60),
            RefreshToken = "REFRESH",
            RefreshExpiresAt = DateTimeOffset.UtcNow.AddMinutes(refreshExpired ? -10 : 600),
            AccountId = "acct-1",
            DisplayName = "Ann",
        });
        return Provider();
    }

    #region Sign-in

    [Theory]
    [InlineData("https://www.epicgames.com/id/api/redirect?clientId=x&responseType=code", SignInResultKind.PageText)]
    [InlineData("https://WWW.EPICGAMES.COM/id/api/Redirect", SignInResultKind.PageText)]
    [InlineData("https://www.epicgames.com/id/login", SignInResultKind.None)]
    [InlineData("https://evil.example/id/api/redirect", SignInResultKind.None)]
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
    public void The_provider_is_read_only()
    {
        Assert.IsNotAssignableFrom<IWritableStoreProvider>(Provider());
    }

    [Fact]
    public void A_saved_session_is_picked_up_and_a_missing_one_is_signed_out()
    {
        Assert.False(Provider().IsSignedIn);

        var provider = SignedIn();

        Assert.True(provider.IsSignedIn);
        Assert.Equal("acct-1", provider.AccountId);
        Assert.Equal("Ann", provider.AccountName);
    }

    [Fact]
    public void A_session_whose_refresh_token_has_run_out_is_signed_out_and_has_no_account()
    {
        var provider = SignedIn(refreshExpired: true);

        Assert.False(provider.IsSignedIn);
        Assert.Null(provider.AccountId);
    }

    [Fact]
    public async Task The_pasted_page_signs_in_with_a_basic_authenticated_form_post()
    {
        _http.Json("oauth/token", Token());
        var provider = Provider();

        await provider.SignInAsync(
            $$"""{"redirectUrl":"x","authorizationCode":"{{Code}}","sid":null}""", TestContext.Current.CancellationToken);

        Assert.True(provider.IsSignedIn);
        Assert.Equal("acct-1", provider.AccountId);
        var request = _http.RequestsTo("oauth/token").Single();
        Assert.Equal(System.Net.Http.HttpMethod.Post, request.Method);
        Assert.StartsWith("Basic ", request.Authorization);
        Assert.Equal(
            "34a02cf8f4414e29b15921876da36f9a:daafbccc737745039dffe53d94fc76cf",
            Encoding.ASCII.GetString(Convert.FromBase64String(request.Authorization!["Basic ".Length..])));
        Assert.Contains("grant_type=authorization_code", request.Body);
        Assert.Contains($"code={Code}", request.Body);
        Assert.True(_sessions.Has(Path));
        Assert.Equal("REFRESH", _sessions.Load<EpicSession>(Path)!.RefreshToken);
    }

    [Fact]
    public async Task A_bare_code_works_too()
    {
        _http.Json("oauth/token", Token());

        await Provider().SignInAsync("  " + Code + "\n", TestContext.Current.CancellationToken);

        Assert.Contains($"code={Code}", _http.RequestsTo("oauth/token").Single().Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not hex at all, and long enough")]
    [InlineData("abc")]
    public async Task Something_that_is_not_a_code_is_refused_without_asking_epic(string pasted)
    {
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(pasted, TestContext.Current.CancellationToken));

        Assert.Equal("invalid_code", error.ErrorCode);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task A_code_epic_refuses_comes_back_with_epics_error_and_without_the_code()
    {
        _http.Json(
            "oauth/token",
            """{"errorCode":"errors.com.epicgames.account.oauth.authorization_code_not_found","errorMessage":"Code not found"}""",
            HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(Code, TestContext.Current.CancellationToken));

        Assert.Equal("errors.com.epicgames.account.oauth.authorization_code_not_found", error.ErrorCode);
        Assert.DoesNotContain(Code, error.Message);
    }

    [Fact]
    public async Task A_corrective_action_error_tells_the_user_where_to_go()
    {
        _http.Json(
            "oauth/token",
            """{"errorCode":"errors.com.epicgames.oauth.corrective_action_required","errorMessage":"Act.","continuationUrl":"https://epic/fix"}""",
            HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(Code, TestContext.Current.CancellationToken));

        Assert.Contains("https://epic/fix", error.Message);
    }

    [Fact]
    public async Task A_failure_with_no_error_body_is_reported_by_its_http_status_without_the_body()
    {
        _http.Json("oauth/token", "<html>secret body</html>", HttpStatusCode.BadGateway);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().SignInAsync(Code, TestContext.Current.CancellationToken));

        Assert.Equal("http_502", error.ErrorCode);
        Assert.DoesNotContain("secret body", error.Message);
    }

    [Fact]
    public void Signing_out_forgets_the_session_and_deletes_the_file()
    {
        var provider = SignedIn();

        provider.SignOut();

        Assert.False(provider.IsSignedIn);
        Assert.False(_sessions.Has(Path));
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
    public async Task An_expired_access_token_is_refreshed_and_the_new_session_saved()
    {
        _http.Json("oauth/token", Token(access: "NEW", refresh: "NEW-REFRESH"));

        var provider = SignedIn(accessExpired: true);
        await provider.EnsureSignedInAsync(TestContext.Current.CancellationToken);

        var request = _http.RequestsTo("oauth/token").Single();
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=REFRESH", request.Body);
        Assert.Equal("NEW-REFRESH", _sessions.Load<EpicSession>(Path)!.RefreshToken);
        Assert.Equal("NEW", provider.Session!.AccessToken);
    }

    [Fact]
    public async Task A_refresh_token_epic_refuses_means_login_required()
    {
        _http.Json(
            "oauth/token",
            """{"errorCode":"errors.com.epicgames.account.auth_token.invalid_refresh_token","errorMessage":"Bad"}""",
            HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => SignedIn(accessExpired: true).EnsureSignedInAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
    }

    [Fact]
    public async Task A_server_that_is_unwell_is_not_taken_for_a_signed_out_account()
    {
        _http.Json("oauth/token", "oops", HttpStatusCode.ServiceUnavailable);

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => SignedIn(accessExpired: true).EnsureSignedInAsync(TestContext.Current.CancellationToken));

        Assert.Equal("http_503", error.ErrorCode);
    }

    [Fact]
    public async Task Without_a_usable_session_every_call_says_login_required()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => SignedIn(accessExpired: true, refreshExpired: true).GetLibraryAsync(TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
        Assert.Empty(_http.Requests);
    }

    #endregion

    #region Library

    private static string Record(string ns, string item, string app = "app", string sandbox = "live") =>
        JsonSerializer.Serialize(new { @namespace = ns, catalogItemId = item, appName = app, sandboxName = sandbox });

    private static string Page(string[] records, string? next = null) =>
        JsonSerializer.Serialize(new
        {
            records = records.Select(r => JsonDocument.Parse(r).RootElement).ToArray(),
            responseMetadata = next == null ? null : new { nextCursor = next },
        });

    private static string CatalogItem(string title, string? image = null, bool dlc = false) =>
        JsonSerializer.Serialize(new
        {
            title,
            keyImages = image == null ? Array.Empty<object>() : new object[] { new { type = "DieselGameBoxWide", url = image } },
            mainGameItem = dlc ? new { id = "main" } : null,
        });

    [Fact]
    public async Task The_library_is_the_owned_games_with_title_and_art_in_order()
    {
        _http
            .Json("library/api/public/items", Page(new[] { Record("nsB", "b"), Record("nsA", "a") }))
            .Json("namespace/nsA/bulk/items", $$"""{"a":{{CatalogItem("alpha", "https://img/a")}} }""")
            .Json("namespace/nsB/bulk/items", $$"""{"b":{{CatalogItem("Beta")}} }""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "alpha", "Beta" }, library.Select(g => g.Title));
        // Id is the catalog item, Key the namespace: achievements are addressed by the latter.
        Assert.Equal(new StoreGame("epic", "a", "nsA", "alpha", "https://img/a"), library[0]);
        Assert.Null(library[1].ImageUrl);
    }

    [Fact]
    public async Task Every_page_of_the_library_is_read_until_there_is_no_cursor()
    {
        _http
            .Route("library/api/public/items", request => request.Url.Contains("cursor=NEXT")
                ? FakeStore.Reply(HttpStatusCode.OK, Page(new[] { Record("ns2", "b") }))
                : FakeStore.Reply(HttpStatusCode.OK, Page(new[] { Record("ns1", "a") }, "NEXT")))
            .Json("namespace/ns1/bulk/items", $$"""{"a":{{CatalogItem("A")}} }""")
            .Json("namespace/ns2/bulk/items", $$"""{"b":{{CatalogItem("B")}} }""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "A", "B" }, library.Select(g => g.Title));
        Assert.Equal(2, _http.RequestsTo("library/api/public/items").Count());
    }

    [Fact]
    public async Task Marketplace_assets_are_skipped_before_the_catalog_is_asked()
    {
        _http
            .Json("library/api/public/items", Page(new[]
            {
                Record("ue", "asset1"),
                Record("fabns", "asset2", sandbox: "fab-listing-live"),
                Record("unreal", "asset3", sandbox: "UE Marketplace"),
                Record("real", "game"),
            }))
            .Json("namespace/real/bulk/items", $$"""{"game":{{CatalogItem("Real game")}} }""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Real game", Assert.Single(library).Title);
        Assert.Single(_http.RequestsTo("bulk/items"));
    }

    [Fact]
    public async Task A_dlc_is_left_out_because_it_names_its_main_game()
    {
        _http
            .Json("library/api/public/items", Page(new[] { Record("ns", "game"), Record("ns", "dlc") }))
            .Json("namespace/ns/bulk/items", $$"""{"game":{{CatalogItem("Game")}},"dlc":{{CatalogItem("Pack", dlc: true)}} }""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Game", Assert.Single(library).Title);
    }

    [Fact]
    public async Task A_game_the_catalog_does_not_know_is_left_out_and_a_nameless_one_takes_the_app_name()
    {
        _http
            .Json("library/api/public/items", Page(new[] { Record("ns", "known", app: "KnownApp"), Record("ns", "unknown") }))
            .Json("namespace/ns/bulk/items", """{"known":{"keyImages":[]}}""");

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("KnownApp", Assert.Single(library).Title);
    }

    [Fact]
    public async Task A_namespace_with_many_items_is_asked_about_in_batches_so_the_address_stays_short()
    {
        var records = Enumerable.Range(1, 60).Select(i => Record("big", "item" + i)).ToArray();
        _http
            .Json("library/api/public/items", Page(records))
            .Route("namespace/big/bulk/items", request =>
            {
                var ids = request.Url.Split('?')[1].Split('&').Where(p => p.StartsWith("id=", StringComparison.Ordinal))
                    .Select(p => p["id=".Length..]).ToList();
                return FakeStore.Reply(
                    HttpStatusCode.OK,
                    "{" + string.Join(",", ids.Select(id => $"\"{id}\":{CatalogItem("T " + id)}")) + "}");
            });

        var library = await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(60, library.Count);
        Assert.Equal(3, _http.RequestsTo("bulk/items").Count());
        Assert.All(_http.RequestsTo("bulk/items"), r => Assert.True(r.Url.Split("id=").Length - 1 <= 25));
    }

    [Fact]
    public async Task The_catalog_is_asked_with_main_game_details_so_dlc_can_be_told_apart()
    {
        _http
            .Json("library/api/public/items", Page(new[] { Record("ns", "a") }))
            .Json("namespace/ns/bulk/items", $$"""{"a":{{CatalogItem("A")}} }""");

        await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Contains("includeMainGameDetails=true", _http.RequestsTo("bulk/items").Single().Url);
    }

    [Fact]
    public async Task Library_requests_carry_the_bearer_token()
    {
        _http
            .Json("library/api/public/items", Page(Array.Empty<string>()));

        await SignedIn().GetLibraryAsync(TestContext.Current.CancellationToken);

        Assert.Equal("bearer ACCESS", _http.RequestsTo("library/api/public/items").Single().Authorization);
    }

    #endregion

    #region Achievements

    private static string Definitions(string items) =>
        "{\"data\":{\"Achievement\":{\"productAchievementsRecordBySandbox\":{\"achievements\":[" + items + "]}}}}";

    private static string Definition(
        string name, bool hidden = false, int xp = 10, double rarity = 5.5) =>
        JsonSerializer.Serialize(new
        {
            achievement = new
            {
                name,
                hidden,
                XP = xp,
                unlockedDisplayName = name + " unlocked",
                lockedDisplayName = name + " locked",
                unlockedDescription = "does " + name,
                lockedDescription = hidden ? "hidden" : "do " + name,
                unlockedIconLink = "https://i/" + name + "-u",
                lockedIconLink = "https://i/" + name + "-l",
                rarity = new { percent = rarity },
            },
        });

    private static string Player(params (string Name, bool Unlocked, double Progress, string? Date)[] states) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                PlayerAchievement = new
                {
                    playerAchievementGameRecordsBySandbox = new
                    {
                        records = new[]
                        {
                            new
                            {
                                playerAchievements = states.Select(s => new
                                {
                                    playerAchievement = new { achievementName = s.Name, unlocked = s.Unlocked, progress = s.Progress, unlockDate = s.Date },
                                }).ToArray(),
                            },
                        },
                    },
                },
            },
        });

    /// <summary>Plays the GraphQL store: the answer depends on which of the two queries came.</summary>
    private FakeStore Graph(string definitions, string player) => _http.Route(
        "launcher.store.epicgames.com/graphql",
        request => FakeStore.Reply(HttpStatusCode.OK, request.Body!.Contains("productAchievementsRecordBySandbox") ? definitions : player));

    [Fact]
    public async Task Achievements_merge_the_definitions_with_the_players_state()
    {
        Graph(
            Definitions(Definition("A1") + "," + Definition("A2", hidden: true, xp: 50, rarity: 1.25)),
            Player(("A1", true, 1.0, "2024-03-05T10:20:30.000Z"), ("A2", false, 0.4, null)));

        var list = await SignedIn().GetAchievementsAsync("sandbox", "en", TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
        var first = list[0];
        Assert.Equal("A1", first.Id);
        Assert.True(first.Unlocked);
        // An unlocked achievement shows its unlocked text and icon.
        Assert.Equal("A1 unlocked", first.DisplayName);
        Assert.Equal("does A1", first.Description);
        Assert.Equal("https://i/A1-u", first.IconUrl);
        Assert.Equal(10, first.XP);
        Assert.Equal(5.5, first.RarityPercent);
        Assert.Equal(1.0, first.Progress);
        Assert.Equal(new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.Zero), first.UnlockDate);
        Assert.False(first.Hidden);

        var second = list[1];
        Assert.False(second.Unlocked);
        // A locked one shows the locked text, which for a hidden one is Epic's placeholder.
        Assert.Equal("A2 locked", second.DisplayName);
        Assert.Equal("hidden", second.Description);
        Assert.Equal("https://i/A2-l", second.IconUrl);
        Assert.True(second.Hidden);
        Assert.Equal(50, second.XP);
        Assert.Equal(0.4, second.Progress);
        Assert.Null(second.UnlockDate);
    }

    [Fact]
    public async Task An_achievement_the_player_has_no_record_of_is_locked_with_no_progress()
    {
        Graph(Definitions(Definition("A1")), Player());

        var achievement = Assert.Single(
            await SignedIn().GetAchievementsAsync("sandbox", "en", TestContext.Current.CancellationToken));

        Assert.False(achievement.Unlocked);
        Assert.Equal(0.0, achievement.Progress);
        Assert.Null(achievement.UnlockDate);
    }

    [Fact]
    public async Task The_sandbox_account_and_locale_go_to_the_store_as_variables()
    {
        Graph(Definitions(Definition("A1")), Player());

        await SignedIn().GetAchievementsAsync("my-sandbox", "de", TestContext.Current.CancellationToken);

        var bodies = _http.RequestsTo("graphql").Select(r => r.Body).ToList();
        Assert.Equal(2, bodies.Count);
        var definitions = bodies.Single(b => b!.Contains("productAchievementsRecordBySandbox"));
        Assert.Contains("\"sandboxId\":\"my-sandbox\"", definitions);
        Assert.Contains("\"locale\":\"de\"", definitions);
        var player = bodies.Single(b => b!.Contains("playerAchievementGameRecordsBySandbox"));
        Assert.Contains("\"epicAccountId\":\"acct-1\"", player);
        Assert.Contains("\"sandboxId\":\"my-sandbox\"", player);
    }

    [Fact]
    public async Task The_store_is_spoken_to_as_the_launcher_with_the_bearer_token()
    {
        Graph(Definitions(Definition("A1")), Player());

        await SignedIn().GetAchievementsAsync("s", "en", TestContext.Current.CancellationToken);

        var request = _http.RequestsTo("graphql").First();
        Assert.Equal("bearer ACCESS", request.Authorization);
        Assert.StartsWith("EpicGamesLauncher/", request.UserAgent);
    }

    [Fact]
    public async Task A_game_with_no_achievements_has_an_empty_list()
    {
        Graph("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":null}} }""", Player());

        var list = await SignedIn().GetAchievementsAsync("s", "en", TestContext.Current.CancellationToken);

        Assert.Empty(list);
    }

    [Fact]
    public async Task A_graphql_error_with_no_data_is_reported()
    {
        _http.Json("graphql", """{"errors":[{"message":"Sandbox not found"}],"data":null}""");

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => SignedIn().GetAchievementsAsync("s", "en", TestContext.Current.CancellationToken));

        Assert.Equal("graphql_error", error.ErrorCode);
        Assert.Equal("Sandbox not found", error.Message);
    }

    [Fact]
    public async Task A_signed_out_account_cannot_read_achievements()
    {
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => Provider().GetAchievementsAsync("s", "en", TestContext.Current.CancellationToken));

        Assert.Equal("login_required", error.ErrorCode);
        Assert.Empty(_http.Requests);
    }

    #endregion
}
