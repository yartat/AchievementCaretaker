/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on GOG Achievements Manager by Timendum
 * https://github.com/timendum/gog_achievements
 * https://github.com/timendum/gog-achievements-gui
 *
 * It reimplements in C# the calls those tools make to GOG's services: listing, and
 * unlocking and re-locking. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static AchievementCaretaker.Providers.JsonHelpers;
using AchievementCaretaker.Providers.Gog.Models;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers.Gog;

/// <summary>
/// Access to a GOG account: sign-in, the owned library, each game's achievements,
/// and unlocking and re-locking them.
/// </summary>
/// <remarks>
/// <para>
/// The achievement endpoints are not a published API. They are the ones GOG
/// Galaxy itself uses, as documented by the open-source tools
/// <c>timendum/gog_achievements</c> and Heroic, and they can change without
/// notice. See <c>docs/GOG-RESEARCH.md</c>.
/// </para>
/// <para>
/// Changing an achievement needs a token issued to the <em>game's</em> client, which
/// needs that game's client secret. The secret is read from GOGDB's build record
/// only when something is written, never for listing, and it is never logged or put
/// in a message.
/// </para>
/// </remarks>
public sealed class GogProvider : IWritableStoreProvider, IStoreAccount
{
    private const string AuthHost = "https://auth.gog.com";
    private const string EmbedHost = "https://embed.gog.com";
    private const string ApiHost = "https://api.gog.com";
    private const string GameplayHost = "https://gameplay.gog.com";
    private const string GogDbHost = "https://www.gogdb.org";

    // GOG Galaxy's own public OAuth client, the same one Heroic and the
    // reference tools sign in with. The authorization-code flow is tied to
    // it: the redirect below only issues codes for this id.
    private const string GalaxyClientId = "46899977096215655";
    private const string GalaxyClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";

    // How many ids one catalog request carries, and how many run at once.
    // The limit is not documented: 50 ids were accepted and 60 were refused
    // with HTTP 400 when this was written, so there is headroom, and a batch
    // that is refused anyway is split, see FetchProductsAsync.
    private const int ProductBatchSize = 40;
    private const int ProductConcurrency = 4;

    /// <summary>
    /// The name of the <c>HttpClient</c> this provider asks the factory for. A client is
    /// asked for per request and never kept: a long-lived one holds on to its connections
    /// and to the DNS answer it started with.
    /// </summary>
    public const string HttpClientName = "gog";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ISessionStore _sessions;
    private readonly string _sessionPath;

    /// <summary>
    /// Product id to the client id its achievements are addressed by; empty
    /// when the game has none. Resolving one costs two requests to a
    /// community-run site, so each is asked for once per process.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _clientIds = new();

    /// <summary>Product id to the game's client secret, read only by a write.</summary>
    private readonly ConcurrentDictionary<string, string> _clientSecrets = new();

    /// <summary>The saved sign-in, or null when signed out.</summary>
    public GogSession? Session { get; private set; }

    /// <inheritdoc />
    public string Id => "gog";

    /// <inheritdoc />
    public string DisplayName => "GOG";

    /// <summary>
    /// After signing in, the browser ends on an <c>embed.gog.com</c> page whose
    /// address carries <c>code=</c>; that address, or the code alone, goes to
    /// <see cref="SignInAsync"/>. The page itself may well be blank.
    /// </summary>
    public string SignInUrl =>
        $"{AuthHost}/auth?client_id={GalaxyClientId}" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&layout=galaxy";

    /// <inheritdoc />
    public string SignInHint => "the address the browser ends on (it contains \"code=\"), or just the code";

    /// <inheritdoc />
    public SignInResultKind GetSignInResultKind(Uri? address) =>
        address != null &&
        address.Host.Equals("embed.gog.com", StringComparison.OrdinalIgnoreCase) &&
        address.AbsolutePath.StartsWith("/on_login_success", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(address.Query, @"[?&]code=[A-Za-z0-9_\-]+")
            ? SignInResultKind.Address
            : SignInResultKind.None;

    /// <inheritdoc />
    public bool IsSignedIn => Session?.HasRefreshToken == true;

    /// <inheritdoc />
    public string? AccountName => Session?.DisplayName;

    /// <inheritdoc />
    public string? AccountId => IsSignedIn == true ? Session?.UserId : null;

    /// <summary>Makes the provider, reading the saved session if there is one.</summary>
    /// <param name="httpFactory">Where the named client is asked for, per request.</param>
    /// <param name="sessions">Where the session is kept between runs.</param>
    /// <param name="sessionPath">The session file, <c>gog.json</c> in the home directory.</param>
    public GogProvider(IHttpClientFactory httpFactory, ISessionStore sessions, string sessionPath)
    {
        _httpFactory = httpFactory;
        _sessions = sessions;
        _sessionPath = sessionPath;
        Session = sessions.Load<GogSession>(sessionPath);
    }

    /// <summary>What every request of the named client starts from; see <see cref="HttpClientName"/>.</summary>
    internal static void ConfigureHttpClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "AchievementCaretaker (+https://github.com/yartat/AchievementCaretaker)");
    }

    private HttpClient CreateClient() => _httpFactory.CreateClient(HttpClientName);

    private static ProviderException NotSignedIn() => new("login_required", "Not signed in to GOG.");

    #region Sign-in

    /// <inheritdoc />
    public async Task SignInAsync(string pasted, CancellationToken cancellationToken = default)
    {
        var match = Regex.Match(pasted ?? "", @"[?&]code=([A-Za-z0-9_\-]+)");
        var code = match.Success == true ? match.Groups[1].Value : (pasted ?? "").Trim();
        if (Regex.IsMatch(code, @"^[A-Za-z0-9_\-]{16,}$") == false)
        {
            throw new ProviderException("invalid_code", "That does not look like a GOG authorization code.");
        }

        var session = await RequestTokenAsync(
            new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
            },
            cancellationToken);

        // The name is only for display, so a failure to fetch it is not a
        // failure to sign in.
        try
        {
            using var data = await GetJsonAsync($"{EmbedHost}/userData.json", true, cancellationToken);
            session.DisplayName = GetString(data.RootElement, "username");
            _sessions.Save(_sessionPath, session);
        }
        catch (Exception e) when (e is ProviderException or HttpRequestException)
        {
        }
    }

    /// <inheritdoc />
    public async Task EnsureSignedInAsync(CancellationToken cancellationToken = default)
    {
        if (Session?.IsAccessTokenValid == true)
        {
            return;
        }

        if (Session?.RefreshToken is not { Length: > 0 } refreshToken)
        {
            throw NotSignedIn();
        }

        await RequestTokenAsync(
            new()
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void SignOut()
    {
        Session = null;
        _sessions.Delete(_sessionPath);
    }

    private async Task<GogSession> RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        parameters["client_id"] = GalaxyClientId;
        parameters["client_secret"] = GalaxyClientSecret;
        var query = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        // GOG's token endpoint takes its parameters in the query string. The
        // URL is never put in an error message, since it carries the code or
        // the refresh token.
        using var response = await CreateClient().GetAsync($"{AuthHost}/token?{query}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfError(response, body);

        var session = JsonSerializer.Deserialize<GogSession>(body);
        if (string.IsNullOrEmpty(session?.AccessToken) == true)
        {
            throw new ProviderException("bad_response", "GOG did not return an access token.");
        }

        session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn);
        if (session.HasRefreshToken == false)
        {
            session.RefreshToken = Session?.RefreshToken;
        }
        session.DisplayName = Session?.DisplayName;

        Session = session;
        _sessions.Save(_sessionPath, session);
        return session;
    }

    #endregion

    #region Library

    /// <summary>
    /// Every owned game. The account's list also holds DLC and bundles; the
    /// catalog says which is which, and only games are kept.
    /// </summary>
    public async Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        using var owned = await GetJsonAsync($"{EmbedHost}/user/data/games", true, cancellationToken);
        List<long> ids = new();
        if (TryWalk(owned.RootElement, out var list, "owned") == true && list.ValueKind == JsonValueKind.Array)
        {
            ids.AddRange(list.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt64())
                .Distinct());
        }

        using var throttle = new SemaphoreSlim(ProductConcurrency);
        var lookups = ids.Chunk(ProductBatchSize).Select(async batch =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                return await FetchProductsAsync(batch, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        return (await Task.WhenAll(lookups))
            .SelectMany(games => games)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Titles and art for a batch of product ids, from GOG's own catalog, which
    /// needs no sign-in. Ids it does not know are left out of the answer. A
    /// request over its size limit fails with HTTP 400, so if the limit ever
    /// drops below <see cref="ProductBatchSize"/> the batch is halved until
    /// the catalog accepts it.
    /// </summary>
    private async Task<List<StoreGame>> FetchProductsAsync(long[] ids, CancellationToken cancellationToken)
    {
        var list = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

        JsonDocument products;
        try
        {
            products = await GetJsonAsync($"{ApiHost}/products?ids={list}", false, cancellationToken);
        }
        catch (ProviderException e) when (e.ErrorCode == "http_400")
        {
            if (ids.Length == 1)
            {
                return new();
            }

            var half = ids.Length / 2;
            var first = await FetchProductsAsync(ids[..half], cancellationToken);
            first.AddRange(await FetchProductsAsync(ids[half..], cancellationToken));
            return first;
        }

        List<StoreGame> games = new();
        using (products)
        {
            if (products.RootElement.ValueKind != JsonValueKind.Array)
            {
                return games;
            }

            foreach (var product in products.RootElement.EnumerateArray())
            {
                if (GetString(product, "game_type") != "game" ||
                    product.TryGetProperty("id", out var idElement) == false ||
                    idElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var id = idElement.GetInt64().ToString(CultureInfo.InvariantCulture);
                var title = GetString(product, "title") ?? id;
                TryWalk(product, out var images, "images");
                var image = FirstOf(images, "logo2x", "logo");

                // Images come protocol-relative: "//images-1.gog-statics.com/…".
                games.Add(new(Id, id, id, title, image?.StartsWith("//") == true ? "https:" + image : image));
            }
        }
        return games;
    }

    #endregion

    #region Achievements

    /// <summary>A game's achievements with the signed-in player's progress.</summary>
    /// <param name="gameKey">The game's product id.</param>
    /// <param name="locale">The language the names and descriptions are asked for in.</param>
    /// <param name="cancellationToken">The token to cancel the request.</param>
    public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
        string gameKey,
        string locale = "en",
        CancellationToken cancellationToken = default)
    {
        await EnsureSignedInAsync(cancellationToken);

        List<StoreAchievement> achievements = new();
        var clientId = await GetClientIdAsync(gameKey, cancellationToken);
        if (string.IsNullOrEmpty(clientId) == true)
        {
            return achievements;
        }

        // Reading takes the account's own token. Only changing an achievement
        // needs a token issued to the game's client, see SetAchievementsAsync.
        using var document = await GetJsonOrNullAsync(
            $"{GameplayHost}/clients/{Uri.EscapeDataString(clientId)}" +
            $"/users/{Uri.EscapeDataString(AccountId ?? throw NotSignedIn())}/achievements",
            true,
            cancellationToken,
            locale);

        if (document == null ||
            TryWalk(document.RootElement, out var items, "items") == false ||
            items.ValueKind != JsonValueKind.Array)
        {
            return achievements;
        }

        foreach (var item in items.EnumerateArray())
        {
            // The unlock date is the only record of state: it is null for a
            // locked achievement.
            var date = GetDate(item, "date_unlocked");
            var unlocked = date != null;
            var id = GetString(item, "achievement_id") ?? GetString(item, "achievement_key");
            if (id == null)
            {
                continue;
            }

            achievements.Add(new(
                id,
                GetString(item, "name") ?? id,
                GetString(item, "description") ?? "",
                unlocked == true
                    ? FirstOf(item, "image_url_unlocked", "image_url_locked")
                    : FirstOf(item, "image_url_locked", "image_url_unlocked"),
                item.TryGetProperty("visible", out var visible) == true &&
                    visible.ValueKind == JsonValueKind.False,
                null,
                GetDouble(item, "rarity"),
                unlocked,
                null,
                date));
        }
        return achievements;
    }

    /// <summary>
    /// A game's achievements are addressed by its client id, which GOG does not
    /// hand out directly. GOGDB, a community-run archive of Galaxy's build
    /// metadata, records it per build, so the newest listed build is looked up
    /// there. Returns null when the game has no builds, and so no achievements.
    /// </summary>
    /// <remarks>
    /// The build record also carries the game's client <em>secret</em>. Listing does
    /// not read it: only a write does, in <see cref="GetClientSecretAsync"/>.
    /// </remarks>
    private async Task<string> GetClientIdAsync(string productId, CancellationToken cancellationToken)
    {
        if (_clientIds.TryGetValue(productId, out var cached) == true)
        {
            return cached;
        }

        using var build = await GetNewestBuildAsync(productId, cancellationToken);
        var clientId = build == null ? "" : GetString(build.RootElement, "clientId") ?? "";

        _clientIds[productId] = clientId;
        return clientId;
    }

    /// <summary>The newest build's record from GOGDB, or null when the game has none.</summary>
    private async Task<JsonDocument?> GetNewestBuildAsync(string productId, CancellationToken cancellationToken)
    {
        var path = $"{GogDbHost}/data/products/{Uri.EscapeDataString(productId)}";
        using var product = await GetJsonOrNullAsync($"{path}/product.json", false, cancellationToken);
        var buildId = product == null ? null : NewestBuildId(product.RootElement);
        return buildId == null
            ? null
            : await GetJsonOrNullAsync($"{path}/builds/{buildId}.json", false, cancellationToken);
    }

    /// <summary>
    /// The newest published build, preferring ones GOG still lists. The id can
    /// exceed what a double holds exactly, so it is kept as the raw text.
    /// </summary>
    private static string? NewestBuildId(JsonElement product)
    {
        if (TryWalk(product, out var builds, "builds") == false || builds.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return builds.EnumerateArray()
            .Where(b => string.IsNullOrEmpty(GetString(b, "date_published")) == false &&
                        b.TryGetProperty("id", out var id) == true &&
                        id.ValueKind == JsonValueKind.Number)
            .OrderByDescending(b => GetBool(b, "listed"))
            .ThenByDescending(b => GetString(b, "date_published"), StringComparer.Ordinal)
            .Select(b => b.GetProperty("id").GetRawText())
            .FirstOrDefault();
    }

    #endregion

    #region Changing achievements

    /// <summary>
    /// Unlocks or re-locks achievements as the signed-in player. Not part of
    /// listing: it is the one place the game's client secret is read.
    /// </summary>
    /// <param name="gameKey">The game's product id.</param>
    /// <param name="changes">Achievement id and the state it should have.</param>
    /// <param name="cancellationToken">The token to cancel the writes.</param>
    public async Task SetAchievementsAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0)
        {
            return;
        }

        await EnsureSignedInAsync(cancellationToken);
        var clientId = await GetClientIdAsync(gameKey, cancellationToken);
        var secret = await GetClientSecretAsync(gameKey, cancellationToken);
        if (string.IsNullOrEmpty(clientId) == true || string.IsNullOrEmpty(secret) == true)
        {
            throw new ProviderException("no_client", "GOG has no build of this game that achievements can be changed through.");
        }

        var userId = AccountId ?? throw NotSignedIn();
        var gameToken = await RequestGameTokenAsync(clientId, secret, cancellationToken);

        // GOG refuses a date in the future, and a clock a little ahead of GOG's is
        // enough for that: the reference tools back it off by a few seconds.
        var unlockedAt = DateTimeOffset.UtcNow.AddSeconds(-3)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'+0000'", CultureInfo.InvariantCulture);

        await Parallel.ForEachAsync(
            changes,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (change, token) =>
            {
                var body = change.Value == true
                    ? JsonSerializer.Serialize(new { date_unlocked = unlockedAt })
                    : "{\"date_unlocked\":null}";
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{GameplayHost}/clients/{Uri.EscapeDataString(clientId)}" +
                    $"/users/{Uri.EscapeDataString(userId)}" +
                    $"/achievements/{Uri.EscapeDataString(change.Key)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gameToken);
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

                using var response = await CreateClient().SendAsync(request, token);
                ThrowIfError(response, await response.Content.ReadAsStringAsync(token));
            });
    }

    private async Task<string> GetClientSecretAsync(string productId, CancellationToken cancellationToken)
    {
        if (_clientSecrets.TryGetValue(productId, out var cached) == true)
        {
            return cached;
        }

        using var build = await GetNewestBuildAsync(productId, cancellationToken);
        var secret = build == null ? "" : GetString(build.RootElement, "clientSecret") ?? "";
        _clientSecrets[productId] = secret;
        return secret;
    }

    /// <summary>
    /// A token issued to one game's client for the signed-in player. It is used for
    /// the writes and thrown away; the account's own session is left as it is.
    /// </summary>
    private async Task<string> RequestGameTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        Dictionary<string, string> parameters = new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = Session?.RefreshToken ?? throw NotSignedIn(),
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["without_new_session"] = "1",
        };
        var query = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        // As with the account token, the URL carries secrets and is never put in
        // an error message.
        using var response = await CreateClient().GetAsync($"{AuthHost}/token?{query}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfError(response, body);

        using var document = JsonDocument.Parse(body);
        var token = GetString(document.RootElement, "access_token");
        if (string.IsNullOrEmpty(token) == true)
        {
            throw new ProviderException("bad_response", "GOG did not return a token for this game.");
        }
        return token;
    }

    #endregion

    #region HTTP helpers

    /// <summary>
    /// A GET that returns the parsed body. <paramref name="signedIn"/> sends the
    /// account's token, refreshing it first if needed; the catalog and GOGDB
    /// are called without one.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(
        string url,
        bool signedIn,
        CancellationToken cancellationToken,
        string? locale = null)
    {
        using var response = await GetAsync(url, signedIn, locale, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    /// <summary>As <see cref="GetJsonAsync"/>, but an HTTP 404 is an answer: null.</summary>
    private async Task<JsonDocument?> GetJsonOrNullAsync(
        string url,
        bool signedIn,
        CancellationToken cancellationToken,
        string? locale = null)
    {
        using var response = await GetAsync(url, signedIn, locale, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(
        string url,
        bool signedIn,
        string? locale,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (locale != null)
        {
            request.Headers.Add("X-Gog-Lc", locale);
        }
        if (signedIn == true)
        {
            await EnsureSignedInAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session?.AccessToken);
        }

        return await CreateClient().SendAsync(request, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfError(response, body);
        return JsonDocument.Parse(body);
    }

    /// <summary>
    /// GOG's OAuth endpoint reports failures as <c>error</c> and
    /// <c>error_description</c>. The body is only parsed for those; it is never
    /// echoed, because a token response is among the things that can fail.
    /// An <c>invalid_grant</c> means the code or refresh token is spent, which
    /// to the caller is the same as not being signed in.
    /// </summary>
    private static void ThrowIfError(HttpResponseMessage response, string body)
    {
        string? code = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            code = GetString(document.RootElement, "error");
            message = GetString(document.RootElement, "error_description");
        }
        catch (JsonException)
        {
        }

        if (code != null)
        {
            throw new ProviderException(code == "invalid_grant" ? "login_required" : code, message ?? code);
        }
        if (response.IsSuccessStatusCode == false)
        {
            throw new ProviderException(
                "http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                $"GOG returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    #endregion
}
