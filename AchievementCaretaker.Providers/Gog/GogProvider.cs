/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on GOG Achievements Manager by Timendum
 * https://github.com/timendum/gog_achievements
 * https://github.com/timendum/gog-achievements-gui
 *
 * It reimplements in C# the calls those tools make to GOG's services, for reading
 * only. See docs/ATTRIBUTION.md.
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

namespace AchievementCaretaker.Providers.Gog
{
    /// <summary>
    /// Read-only access to a GOG account: sign-in, the owned library and each
    /// game's achievements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The achievement endpoints are not a published API. They are the ones GOG
    /// Galaxy itself uses, as documented by the open-source tools
    /// <c>timendum/gog_achievements</c> and Heroic, and they can change without
    /// notice. See <c>docs/GOG-RESEARCH.md</c>.
    /// </para>
    /// <para>
    /// Nothing here writes to the account. Those tools also unlock and re-lock
    /// achievements, which needs the game's own client secret; this provider never
    /// reads that secret and is deliberately read-only.
    /// </para>
    /// </remarks>
    public sealed class GogProvider : IStoreProvider
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

        private readonly HttpClient _Http;
        private readonly string _SessionPath;

        /// <summary>
        /// Product id to the client id its achievements are addressed by; empty
        /// when the game has none. Resolving one costs two requests to a
        /// community-run site, so each is asked for once per process.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _ClientIds = new();

        public GogSession Session { get; private set; }

        public string Id => "gog";

        public string DisplayName => "GOG";

        /// <summary>
        /// After signing in, the browser ends on an <c>embed.gog.com</c> page whose
        /// address carries <c>code=</c>; that address, or the code alone, goes to
        /// <see cref="SignInAsync"/>. The page itself may well be blank.
        /// </summary>
        public string SignInUrl =>
            $"{AuthHost}/auth?client_id={GalaxyClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&layout=galaxy";

        public string SignInHint => "the address the browser ends on (it contains \"code=\"), or just the code";

        public bool IsSignedIn => this.Session?.HasRefreshToken == true;

        public string AccountName => this.Session?.DisplayName;

        public GogProvider(string sessionPath)
        {
            this._SessionPath = sessionPath;
            this._Http = new() { Timeout = TimeSpan.FromSeconds(30) };
            this._Http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "AchievementCaretaker (+https://github.com/yartat/AchievementCaretaker)");
            this.Session = SessionFile.Load<GogSession>(sessionPath);
        }

        public void Dispose()
        {
            this._Http.Dispose();
        }

        #region Sign-in

        public async Task SignInAsync(string pasted, CancellationToken cancellationToken = default)
        {
            var match = Regex.Match(pasted ?? "", @"[?&]code=([A-Za-z0-9_\-]+)");
            var code = match.Success == true ? match.Groups[1].Value : (pasted ?? "").Trim();
            if (Regex.IsMatch(code, @"^[A-Za-z0-9_\-]{16,}$") == false)
            {
                throw new ProviderException("invalid_code", "That does not look like a GOG authorization code.");
            }

            await this.RequestTokenAsync(
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
                using var data = await this.GetJsonAsync($"{EmbedHost}/userData.json", true, cancellationToken);
                this.Session.DisplayName = GetString(data.RootElement, "username");
                SessionFile.Save(this._SessionPath, this.Session);
            }
            catch (Exception e) when (e is ProviderException or HttpRequestException)
            {
            }
        }

        public async Task EnsureSignedInAsync(CancellationToken cancellationToken = default)
        {
            if (this.Session?.IsAccessTokenValid == true)
            {
                return;
            }

            if (this.Session?.HasRefreshToken != true)
            {
                throw new ProviderException("login_required", "Not signed in to GOG.");
            }

            await this.RequestTokenAsync(
                new()
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = this.Session.RefreshToken,
                },
                cancellationToken);
        }

        public void SignOut()
        {
            this.Session = null;
            SessionFile.Delete(this._SessionPath);
        }

        private async Task RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            parameters["client_id"] = GalaxyClientId;
            parameters["client_secret"] = GalaxyClientSecret;
            var query = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

            // GOG's token endpoint takes its parameters in the query string. The
            // URL is never put in an error message, since it carries the code or
            // the refresh token.
            using var response = await this._Http.GetAsync($"{AuthHost}/token?{query}", cancellationToken);
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
                session.RefreshToken = this.Session?.RefreshToken;
            }
            session.DisplayName = this.Session?.DisplayName;

            this.Session = session;
            SessionFile.Save(this._SessionPath, session);
        }

        #endregion

        #region Library

        /// <summary>
        /// Every owned game. The account's list also holds DLC and bundles; the
        /// catalog says which is which, and only games are kept.
        /// </summary>
        public async Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            using var owned = await this.GetJsonAsync($"{EmbedHost}/user/data/games", true, cancellationToken);
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
                    return await this.FetchProductsAsync(batch, cancellationToken);
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
                products = await this.GetJsonAsync($"{ApiHost}/products?ids={list}", false, cancellationToken);
            }
            catch (ProviderException e) when (e.ErrorCode == "http_400")
            {
                if (ids.Length == 1)
                {
                    return new();
                }

                var half = ids.Length / 2;
                var first = await this.FetchProductsAsync(ids[..half], cancellationToken);
                first.AddRange(await this.FetchProductsAsync(ids[half..], cancellationToken));
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
                    games.Add(new(this.Id, id, id, title, image?.StartsWith("//") == true ? "https:" + image : image));
                }
            }
            return games;
        }

        #endregion

        #region Achievements

        /// <param name="gameKey">The game's product id.</param>
        public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey,
            string locale = "en",
            CancellationToken cancellationToken = default)
        {
            await this.EnsureSignedInAsync(cancellationToken);

            List<StoreAchievement> achievements = new();
            var clientId = await this.GetClientIdAsync(gameKey, cancellationToken);
            if (string.IsNullOrEmpty(clientId) == true)
            {
                return achievements;
            }

            // Reading takes the account's own token. Only changing an achievement
            // needs a token issued to the game's client, which is the part this
            // provider does not do.
            using var document = await this.GetJsonAsync(
                $"{GameplayHost}/clients/{Uri.EscapeDataString(clientId)}" +
                $"/users/{Uri.EscapeDataString(this.Session.UserId)}/achievements",
                true,
                cancellationToken,
                locale,
                nullOnNotFound: true);

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

                achievements.Add(new(
                    id,
                    GetString(item, "name") ?? id,
                    GetString(item, "description"),
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
        /// The build record also carries the game's client <em>secret</em>. It is not
        /// read: only a write needs it.
        /// </remarks>
        private async Task<string> GetClientIdAsync(string productId, CancellationToken cancellationToken)
        {
            if (this._ClientIds.TryGetValue(productId, out var cached) == true)
            {
                return cached;
            }

            var clientId = "";
            var path = $"{GogDbHost}/data/products/{Uri.EscapeDataString(productId)}";
            using (var product = await this.GetJsonAsync(
                $"{path}/product.json", false, cancellationToken, nullOnNotFound: true))
            {
                var buildId = product == null ? null : NewestBuildId(product.RootElement);
                if (buildId != null)
                {
                    using var build = await this.GetJsonAsync(
                        $"{path}/builds/{buildId}.json", false, cancellationToken, nullOnNotFound: true);
                    clientId = build == null ? "" : GetString(build.RootElement, "clientId") ?? "";
                }
            }

            this._ClientIds[productId] = clientId;
            return clientId;
        }

        /// <summary>
        /// The newest published build, preferring ones GOG still lists. The id can
        /// exceed what a double holds exactly, so it is kept as the raw text.
        /// </summary>
        private static string NewestBuildId(JsonElement product)
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
            string locale = null,
            bool nullOnNotFound = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            if (locale != null)
            {
                request.Headers.Add("X-Gog-Lc", locale);
            }
            if (signedIn == true)
            {
                await this.EnsureSignedInAsync(cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.Session.AccessToken);
            }

            using var response = await this._Http.SendAsync(request, cancellationToken);
            if (nullOnNotFound == true && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

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
            string code = null;
            string message = null;
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
}
