/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * Written from the endpoints that the open-source launchers Legendary
 * (https://github.com/derrod/legendary) and Heroic document for Epic Games' services;
 * see docs/EPIC-RESEARCH.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static AchievementCaretaker.Providers.JsonHelpers;

namespace AchievementCaretaker.Providers.Epic
{
    /// <summary>
    /// Read-only access to an Epic Games account: sign-in, the owned library
    /// and each game's achievements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None of these endpoints is a published API. They are the ones the Epic
    /// Games Launcher itself uses, as documented by the open-source launchers
    /// Legendary and Heroic, and they can change without notice. See
    /// <c>docs/EPIC-RESEARCH.md</c>.
    /// </para>
    /// <para>
    /// Nothing here writes to the account. Unlocking an Epic achievement goes
    /// through the game's own EOS credentials and is deliberately out of scope.
    /// </para>
    /// </remarks>
    public sealed class EpicProvider : IStoreProvider
    {
        private const string OAuthHost = "https://account-public-service-prod03.ol.epicgames.com";
        private const string LibraryHost = "https://library-service.live.use1a.on.epicgames.com";
        private const string CatalogHost = "https://catalog-public-service-prod06.ol.epicgames.com";
        private const string StoreGraphQl = "https://launcher.store.epicgames.com/graphql";

        // The Epic Games Launcher's own public OAuth client, the same one
        // Legendary and Heroic sign in with. The authorization-code flow is
        // tied to it: the redirect page below only issues codes for this id.
        private const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";

        private const string LauncherUserAgent = "UELauncher/11.0.1-14907503+++Portal+Release-Live Windows/10.0.19041.1.256.64bit";
        private const string StoreUserAgent = "EpicGamesLauncher/14.0.8-22004686+++Portal+Release-Live";

        /// <summary>
        /// Catalog lookups are one request per namespace, and a library has
        /// hundreds. This keeps them from arriving all at once.
        /// </summary>
        private const int CatalogConcurrency = 8;

        private readonly HttpClient _Http;
        private readonly string _SessionPath;

        public EpicSession Session { get; private set; }

        public string Id => "epic";

        public string DisplayName => "Epic Games";

        /// <summary>
        /// After signing in, the page shows a small JSON document carrying
        /// <c>authorizationCode</c>; that document, or the code alone, goes to
        /// <see cref="SignInAsync"/>.
        /// </summary>
        public string SignInUrl =>
            "https://www.epicgames.com/id/login?redirectUrl=" +
            Uri.EscapeDataString(
                $"https://www.epicgames.com/id/api/redirect?clientId={LauncherClientId}&responseType=code");

        public string SignInHint => "the page that shows \"authorizationCode\" (or just the code)";

        public bool IsSignedIn => this.Session?.IsRefreshTokenValid == true;

        public string AccountName => this.Session?.DisplayName;

        public EpicProvider(string sessionPath)
        {
            this._SessionPath = sessionPath;
            this._Http = new() { Timeout = TimeSpan.FromSeconds(30) };
            this._Http.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherUserAgent);
            this.Session = EpicSession.Load(sessionPath);
        }

        public void Dispose()
        {
            this._Http.Dispose();
        }

        #region Sign-in

        /// <summary>
        /// Accepts either the bare authorization code or the whole JSON the
        /// redirect page shows, since pasting the page is what people will do.
        /// </summary>
        public async Task SignInAsync(string pasted, CancellationToken cancellationToken = default)
        {
            var match = Regex.Match(pasted ?? "", "\"authorizationCode\"\\s*:\\s*\"([0-9a-fA-F]+)\"");
            var code = match.Success == true ? match.Groups[1].Value : (pasted ?? "").Trim();
            if (Regex.IsMatch(code, "^[0-9a-fA-F]{16,}$") == false)
            {
                throw new ProviderException("invalid_code", "That does not look like an Epic authorization code.");
            }

            await this.RequestTokenAsync(
                new()
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["token_type"] = "eg1",
                },
                cancellationToken);
        }

        public async Task EnsureSignedInAsync(CancellationToken cancellationToken = default)
        {
            if (this.Session?.IsAccessTokenValid == true)
            {
                return;
            }

            if (this.Session?.IsRefreshTokenValid != true)
            {
                throw new ProviderException("login_required", "Not signed in to Epic Games.");
            }

            try
            {
                await this.RequestTokenAsync(
                    new()
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = this.Session.RefreshToken,
                        ["token_type"] = "eg1",
                    },
                    cancellationToken);
            }
            catch (ProviderException e) when (e.ErrorCode.StartsWith("http_", StringComparison.Ordinal) == false)
            {
                // Epic answered with an error code of its own: the refresh token was
                // refused, which to the caller is the same as not being signed in. A
                // bare HTTP failure is the server being unwell, and is left as it is.
                throw new ProviderException("login_required", "Your Epic Games session has expired.");
            }
        }

        public void SignOut()
        {
            this.Session = null;
            SessionFile.Delete(this._SessionPath);
        }

        private async Task RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{OAuthHost}/account/api/oauth/token")
            {
                Content = new FormUrlEncodedContent(form),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{LauncherClientId}:{LauncherClientSecret}")));

            using var response = await this._Http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ThrowIfEpicError(response, body);

            this.Session = JsonSerializer.Deserialize<EpicSession>(body);
            this.Session.Save(this._SessionPath);
        }

        #endregion

        #region Library

        /// <summary>
        /// Every owned game, DLC left out. Unreal Engine marketplace and Fab
        /// assets live in the same library and are skipped too.
        /// </summary>
        public async Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            List<(string Namespace, string CatalogItemId, string AppName)> records = new();
            string cursor = null;
            do
            {
                var url = $"{LibraryHost}/library/api/public/items?includeMetadata=true";
                if (cursor != null)
                {
                    url += "&cursor=" + Uri.EscapeDataString(cursor);
                }

                using var page = await this.GetJsonAsync(url, cancellationToken);
                foreach (var record in page.RootElement.GetProperty("records").EnumerateArray())
                {
                    var ns = GetString(record, "namespace");
                    var itemId = GetString(record, "catalogItemId");
                    if (string.IsNullOrEmpty(ns) == true ||
                        string.IsNullOrEmpty(itemId) == true ||
                        IsMarketplaceAsset(record) == true)
                    {
                        continue;
                    }
                    records.Add((ns, itemId, GetString(record, "appName")));
                }

                cursor = page.RootElement.TryGetProperty("responseMetadata", out var metadata) == true
                    ? GetString(metadata, "nextCursor")
                    : null;
            }
            while (string.IsNullOrEmpty(cursor) == false);

            using var throttle = new SemaphoreSlim(CatalogConcurrency);
            var lookups = records
                .DistinctBy(r => (r.Namespace, r.CatalogItemId))
                .GroupBy(r => r.Namespace)
                .Select(async group =>
                {
                    await throttle.WaitAsync(cancellationToken);
                    try
                    {
                        return await this.DescribeAsync(group.Key, group.ToList(), cancellationToken);
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
        /// The library holds everything the account owns, and for a developer
        /// account that is mostly assets: one real library came to about 9,500
        /// records of which about 8,700 were Unreal Engine marketplace or Fab
        /// listings. They are not games and have no achievements. Fab sells
        /// under one shared namespace and is told apart by its sandbox name.
        /// </summary>
        private static bool IsMarketplaceAsset(JsonElement record)
        {
            return GetString(record, "namespace") == "ue" ||
                   GetString(record, "sandboxName") is "UE Marketplace" or "fab-listing-live";
        }

        /// <summary>
        /// The ids travel in the query string, one <c>id=</c> per item, and a
        /// publisher that keeps its whole catalogue in one namespace can own
        /// hundreds of items. Sent together they overflow the URL limit (HTTP
        /// 414), so they go in batches.
        /// </summary>
        private const int CatalogBatchSize = 25;

        private async Task<List<StoreGame>> DescribeAsync(
            string ns,
            List<(string Namespace, string CatalogItemId, string AppName)> items,
            CancellationToken cancellationToken)
        {
            List<StoreGame> games = new();
            foreach (var batch in items.Chunk(CatalogBatchSize))
            {
                var ids = string.Join("&", batch.Select(i => "id=" + Uri.EscapeDataString(i.CatalogItemId)));

                // includeMainGameDetails has to be on: without it the catalog
                // leaves mainGameItem out and DLC cannot be told from games.
                using var catalog = await this.GetJsonAsync(
                    $"{CatalogHost}/catalog/api/shared/namespace/{Uri.EscapeDataString(ns)}/bulk/items?{ids}" +
                    "&includeDLCDetails=false&includeMainGameDetails=true&country=US&locale=en",
                    cancellationToken);

                foreach (var (_, itemId, appName) in batch)
                {
                    if (catalog.RootElement.TryGetProperty(itemId, out var item) == false)
                    {
                        continue;
                    }

                    // A DLC names the game it belongs to; its achievements are that
                    // game's, so listing it separately would only repeat them.
                    if (item.TryGetProperty("mainGameItem", out var main) == true &&
                        main.ValueKind == JsonValueKind.Object)
                    {
                        continue;
                    }

                    games.Add(new(this.Id, itemId, ns, GetString(item, "title") ?? appName, PickImage(item)));
                }
            }
            return games;
        }

        private static string PickImage(JsonElement item)
        {
            if (item.TryGetProperty("keyImages", out var images) == false ||
                images.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var wanted in new[] { "DieselGameBoxWide", "DieselGameBox", "Thumbnail", "DieselGameBoxTall" })
            {
                foreach (var image in images.EnumerateArray())
                {
                    if (GetString(image, "type") == wanted)
                    {
                        return GetString(image, "url");
                    }
                }
            }
            return null;
        }

        #endregion

        #region Achievements

        private const string AchievementQuery = """
            query Achievement($sandboxId: String!, $locale: String!) {
              Achievement {
                productAchievementsRecordBySandbox(sandboxId: $sandboxId, locale: $locale) {
                  achievements {
                    achievement {
                      name hidden XP
                      unlockedDisplayName lockedDisplayName
                      unlockedDescription lockedDescription
                      unlockedIconLink lockedIconLink
                      rarity { percent }
                    }
                  }
                }
              }
            }
            """;

        private const string PlayerAchievementQuery = """
            query PlayerAchievement($epicAccountId: String!, $sandboxId: String!) {
              PlayerAchievement {
                playerAchievementGameRecordsBySandbox(epicAccountId: $epicAccountId, sandboxId: $sandboxId) {
                  records {
                    playerAchievements {
                      playerAchievement { achievementName unlocked progress unlockDate }
                    }
                  }
                }
              }
            }
            """;

        /// <param name="gameKey">The game's namespace, which GraphQL calls its sandbox id.</param>
        public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey,
            string locale = "en",
            CancellationToken cancellationToken = default)
        {
            await this.EnsureSignedInAsync(cancellationToken);

            using var definitions = await this.QueryAsync(
                AchievementQuery,
                new { sandboxId = gameKey, locale },
                cancellationToken);
            using var player = await this.QueryAsync(
                PlayerAchievementQuery,
                new { epicAccountId = this.Session.AccountId, sandboxId = gameKey },
                cancellationToken);

            Dictionary<string, (bool Unlocked, double Progress, DateTimeOffset? Date)> states = new();
            if (TryWalk(player.RootElement, out var records,
                    "data", "PlayerAchievement", "playerAchievementGameRecordsBySandbox", "records") == true &&
                records.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in records.EnumerateArray())
                {
                    if (TryWalk(record, out var list, "playerAchievements") == false ||
                        list.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var entry in list.EnumerateArray())
                    {
                        if (TryWalk(entry, out var state, "playerAchievement") == false ||
                            state.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var name = GetString(state, "achievementName");
                        if (name != null)
                        {
                            states[name] = (
                                GetBool(state, "unlocked"),
                                GetDouble(state, "progress") ?? 0,
                                GetDate(state, "unlockDate"));
                        }
                    }
                }
            }

            List<StoreAchievement> achievements = new();
            if (TryWalk(definitions.RootElement, out var definitionList,
                    "data", "Achievement", "productAchievementsRecordBySandbox", "achievements") == false ||
                definitionList.ValueKind != JsonValueKind.Array)
            {
                return achievements;
            }

            foreach (var entry in definitionList.EnumerateArray())
            {
                if (TryWalk(entry, out var definition, "achievement") == false ||
                    definition.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = GetString(definition, "name");
                states.TryGetValue(name ?? "", out var state);

                // Locked achievements show their locked text, which for a
                // hidden one is the placeholder Epic wants shown instead.
                var prefix = state.Unlocked == true ? "unlocked" : "locked";
                var other = state.Unlocked == true ? "locked" : "unlocked";

                achievements.Add(new(
                    name,
                    FirstOf(definition, prefix + "DisplayName", other + "DisplayName") ?? name,
                    FirstOf(definition, prefix + "Description", other + "Description"),
                    FirstOf(definition, prefix + "IconLink", other + "IconLink"),
                    GetBool(definition, "hidden"),
                    (int?)GetDouble(definition, "XP"),
                    TryWalk(definition, out var rarity, "rarity", "percent") == true &&
                        rarity.ValueKind == JsonValueKind.Number ? rarity.GetDouble() : null,
                    state.Unlocked,
                    state.Progress,
                    state.Date));
            }
            return achievements;
        }

        private async Task<JsonDocument> QueryAsync(string query, object variables, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, StoreGraphQl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { query, variables }),
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(StoreUserAgent);
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", this.Session.AccessToken);

            using var response = await this._Http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ThrowIfEpicError(response, body);

            var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) == true &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0 &&
                (document.RootElement.TryGetProperty("data", out var data) == false ||
                 data.ValueKind == JsonValueKind.Null))
            {
                var message = GetString(errors[0], "message") ?? "GraphQL query failed.";
                document.Dispose();
                throw new ProviderException("graphql_error", message);
            }
            return document;
        }

        #endregion

        #region HTTP helpers

        private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            await this.EnsureSignedInAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", this.Session.AccessToken);

            using var response = await this._Http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ThrowIfEpicError(response, body);
            return JsonDocument.Parse(body);
        }

        /// <summary>
        /// Epic reports failures as a JSON body with <c>errorCode</c>, usually
        /// with a 4xx status. The body is only parsed for that; it is never
        /// echoed, because a token response is among the things that can fail.
        /// </summary>
        private static void ThrowIfEpicError(HttpResponseMessage response, string body)
        {
            string code = null;
            string message = null;
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    code = GetString(document.RootElement, "errorCode");
                    message = GetString(document.RootElement, "errorMessage");
                    if (code == "errors.com.epicgames.oauth.corrective_action_required")
                    {
                        message += " Open " + GetString(document.RootElement, "continuationUrl") + " to resolve it.";
                    }
                }
            }
            catch (JsonException)
            {
            }

            if (code != null)
            {
                throw new ProviderException(code, message ?? code);
            }
            if (response.IsSuccessStatusCode == false)
            {
                throw new ProviderException(
                    "http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    $"Epic returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        #endregion
    }
}
