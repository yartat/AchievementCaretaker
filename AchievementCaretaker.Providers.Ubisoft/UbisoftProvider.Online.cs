/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 * and on galaxy-integration-uplay by melcom (MIT)
 * https://github.com/melcom-creations/galaxy-integration-uplay
 * and on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 *
 * The sign-in and the session are the ones galaxy-integration-uplay uses; what the socket service is asked
 * is what UplayKit asks. See docs/ATTRIBUTION.md.
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
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// The online half of the Ubisoft provider: signing in with Ubisoft's own page, keeping the session, and,
/// for an account signed in to, asking Ubisoft's socket service what the account owns and has earned. The
/// launcher is then not needed. Without a sign-in the provider reads the launcher's files, as before.
/// </summary>
/// <remarks>
/// The sign-in page is shown in the application's own browser; it keeps the session in its local storage, and
/// <see cref="SignInScript"/> moves the page to an address that carries it, which is how the window gets it
/// without reading the page's fields. Caretaker never sees the password.
/// </remarks>
public sealed partial class UbisoftProvider
{
    private readonly object _sessionGate = new();
    private readonly SemaphoreSlim _renewing = new(1, 1);
    private readonly SemaphoreSlim _connecting = new(1, 1);
    private readonly ConcurrentDictionary<uint, string> _blocks = new();
    private UbisoftSession? _session;
    private bool _sessionLoaded;
    private UbisoftOnline? _connection;
    private string? _connectionTicket;
    private CancellationTokenSource? _idle;

    /// <summary>
    /// How long the connection is kept after its last use. Ubisoft lets an account be signed in to its socket service
    /// once at a time, so a connection left open would turn the launcher (or another Caretaker) away, and be turned away
    /// itself ("Ubisoft closed the service"): it is held only for as long as one run of calls takes.
    /// </summary>
    private static readonly TimeSpan _idleLimit = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public string SignInUrl =>
        $"https://connect.ubisoft.com/login?appId={AppId}&genomeId={GenomeId}&lang=en-US&nextUrl=https:%2F%2Fconnect.ubisoft.com%2F";

    /// <inheritdoc />
    public string SignInHint => "";

    /// <inheritdoc />
    public string? SignInScript => WatchScript;

    /// <inheritdoc />
    public SignInResultKind GetSignInResultKind(Uri? address) =>
        address != null &&
        address.Host.Equals("connect.ubisoft.com", StringComparison.OrdinalIgnoreCase) &&
        address.AbsolutePath.StartsWith("/change_domain/", StringComparison.OrdinalIgnoreCase)
            ? SignInResultKind.Address
            : SignInResultKind.None;

    /// <summary>The saved sign-in, or null. Read from the file once and then kept.</summary>
    private UbisoftSession? Session
    {
        get
        {
            lock (_sessionGate)
            {
                if (_sessionLoaded == false)
                {
                    _session = _sessions.Load<UbisoftSession>(_sessionPath);
                    _sessionLoaded = true;
                }
                return _session;
            }
        }
    }

    /// <summary>Whether Caretaker is signed in to a Ubisoft account, so that the account is read from Ubisoft, not from the launcher.</summary>
    public bool IsSignedInOnline => Session is { CanRenew: true, UserId.Length: > 0 };

    /// <summary>
    /// Whether an account can be read: one signed in to here, or the launcher's on this computer. The launcher
    /// is asked of the file system each time, since it is installed and logged in to outside of this application.
    /// </summary>
    public bool IsSignedIn => IsSignedInOnline == true || OwnershipFile.Find(_locations) != null;

    /// <inheritdoc />
    public string? AccountName => IsSignedInOnline == true
        ? Session?.UserName is { Length: > 0 } name ? name : "Ubisoft account"
        : OwnershipFile.Find(_locations) != null ? "this computer" : null;

    /// <summary>The account's id (the launcher's user id is the same value): what a library is saved under; null when there is none.</summary>
    public string? AccountId => IsSignedInOnline == true ? Session?.UserId : LocalUserId;

    /// <summary>Completes the sign-in from the address Ubisoft's page ended on.</summary>
    /// <param name="pasted">The address (<c>https://connect.ubisoft.com/change_domain/...</c>).</param>
    /// <param name="cancellationToken">The token to cancel the sign-in.</param>
    /// <exception cref="ProviderException">The address carries no session, or Ubisoft refuses it.</exception>
    public async Task SignInAsync(string pasted, CancellationToken cancellationToken = default)
    {
        var session = ParseSignInResult(pasted)
            ?? throw new ProviderException("invalid_code", "That is not the end of a Ubisoft sign-in. Sign in again in the window that opens.");

        // The page has only just made this ticket, so it is kept as it is. Ubisoft's answer to a renewal gives its real
        // expiry, but its refusal or silence is not a reason to throw the sign-in away: the socket service, which is
        // where the ticket is used, says if it is no good.
        session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        lock (_sessionGate)
        {
            _session = session;
            _sessionLoaded = true;
            _sessions.Save(_sessionPath, session);
        }

        try
        {
            await RenewAsync(session, cancellationToken, forgetWhenRefused: false, force: true).ConfigureAwait(false);
        }
        catch (ProviderException e) when (e.ErrorCode is "network" or "login_required")
        {
            // Kept, with the hour it was given.
        }
        await DropConnectionAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnsureSignedInAsync(CancellationToken cancellationToken = default)
    {
        var session = Session;
        if (session != null && session.CanRenew == true)
        {
            if (session.IsTicketValid == false)
            {
                await RenewAsync(session, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (OwnershipFile.Find(_locations) == null)
        {
            throw NotFound();
        }
    }

    /// <summary>Forgets the account Caretaker signed in to; the launcher's own is not ours to touch, and is read again.</summary>
    public void SignOut()
    {
        lock (_sessionGate)
        {
            _session = null;
            _sessionLoaded = true;
            _sessions.Delete(_sessionPath);
        }
        _blocks.Clear();
        DropConnectionAsync().GetAwaiter().GetResult();
    }

    /// <summary>Closes the connection to Ubisoft's socket service, if one is open.</summary>
    public ValueTask DisposeAsync() => new(DropConnectionAsync());

    /// <summary>Closes the connection to Ubisoft's socket service, if one is open.</summary>
    public void Dispose() => DropConnectionAsync().GetAwaiter().GetResult();

    #region Sign-in and session

    /// <summary>
    /// The session in the address the sign-in page ended on: the page's login data, its remember-me data and its
    /// last profile, as JSON, one after the other, after <c>change_domain/</c>. Null when there is no usable session in it.
    /// </summary>
    private static UbisoftSession? ParseSignInResult(string pasted)
    {
        var start = pasted.IndexOf("/change_domain/", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var parts = JsonDocument.Parse("[" + Uri.UnescapeDataString(pasted[(start + "/change_domain/".Length)..].Trim()) + "]");
            var items = parts.RootElement.EnumerateArray().ToList();
            if (items.Count == 0 || items[0].ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var login = items[0];
            UbisoftSession session = new()
            {
                Ticket = Text(login, "ticket"),
                SessionId = Text(login, "sessionId"),
                UserId = Text(login, "userId"),
                UserName = Text(login, "nameOnPlatform") ?? Text(login, "username"),
                RememberMeTicket = Text(login, "rememberMeTicket"),
            };
            if (items.Count > 1 && items[1].ValueKind == JsonValueKind.Object)
            {
                session.RememberMeTicket ??= Text(items[1], "rememberMeTicket");
            }

            return string.IsNullOrEmpty(session.Ticket) == false &&
                   string.IsNullOrEmpty(session.SessionId) == false &&
                   string.IsNullOrEmpty(session.UserId) == false
                ? session
                : null;
        }
        catch (Exception e) when (e is JsonException or UriFormatException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Makes the session a fresh one: with its ticket if that still works, else with the remember-me ticket, which is
    /// what lets a sign-in outlive its ticket. Saves the result. Ubisoft rotates the remember-me ticket, so the new
    /// one replaces the old.
    /// </summary>
    private async Task RenewAsync(UbisoftSession session, CancellationToken cancellationToken, bool forgetWhenRefused = true, bool force = false)
    {
        await _renewing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have renewed it while this one waited.
            if (force == false && ReferenceEquals(Session, session) == true && session.IsTicketValid == true)
            {
                return;
            }

            JsonElement? answer = null;
            if (string.IsNullOrEmpty(session.Ticket) == false)
            {
                answer = await SessionRequestAsync(HttpMethod.Put, $"Ubi_v1 t={session.Ticket}", session, cancellationToken).ConfigureAwait(false);
            }
            if (answer == null && string.IsNullOrEmpty(session.RememberMeTicket) == false)
            {
                answer = await SessionRequestAsync(HttpMethod.Post, $"rm_v1 t={session.RememberMeTicket}", session, cancellationToken).ConfigureAwait(false);
            }
            if (answer is not { } renewed)
            {
                if (forgetWhenRefused == true)
                {
                    ForgetSession();
                }
                throw new ProviderException("login_required", "Ubisoft no longer accepts the saved sign-in. Sign in again.");
            }

            UbisoftSession next = new()
            {
                Ticket = Text(renewed, "ticket") ?? session.Ticket,
                SessionId = Text(renewed, "sessionId") ?? session.SessionId,
                UserId = Text(renewed, "userId") ?? session.UserId,
                UserName = Text(renewed, "nameOnPlatform") ?? Text(renewed, "username") ?? session.UserName,
                RememberMeTicket = Text(renewed, "rememberMeTicket") ?? session.RememberMeTicket,
                ExpiresAt = ExpiryOf(renewed),
            };

            lock (_sessionGate)
            {
                _session = next;
                _sessionLoaded = true;
                _sessions.Save(_sessionPath, next);
            }
        }
        finally
        {
            _renewing.Release();
        }
    }

    /// <summary>
    /// One request to Ubisoft's session service; null when it refuses the credential (so the next one may be tried).
    /// The service is the one the sign-in page's own script calls, and it expects what that script sends.
    /// </summary>
    private async Task<JsonElement?> SessionRequestAsync(HttpMethod method, string authorization, UbisoftSession session, CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using HttpRequestMessage request = new(method, SessionsUrl);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("Ubi-AppId", AppId);
            if (string.IsNullOrEmpty(session.SessionId) == false)
            {
                request.Headers.TryAddWithoutValidation("Ubi-SessionId", session.SessionId);
            }
            request.Headers.TryAddWithoutValidation("Origin", WebOrigin);
            request.Headers.TryAddWithoutValidation("Referer", WebOrigin);
            request.Headers.UserAgent.Clear();
            request.Headers.TryAddWithoutValidation("User-Agent", WebUserAgent);
            // The service wants a JSON content type on both (a PUT without one is answered 415).
            request.Content = method == HttpMethod.Post
                ? JsonContent.Create(new { rememberMe = true })
                : new ByteArrayContent(Array.Empty<byte>()) { Headers = { ContentType = new("application/json") } };

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException("network", $"Ubisoft's sign-in service answered {(int)response.StatusCode}.");
            }

            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            return body.RootElement.Clone();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            // The credential is not at fault when the service cannot be reached: the sign-in is kept.
            throw new ProviderException("network", "Ubisoft's sign-in service could not be reached.");
        }
    }

    private static DateTimeOffset ExpiryOf(JsonElement answer)
    {
        // The service gives its own clock's "expiration" and "serverTime": the difference is how long the ticket lasts.
        if (DateTimeOffset.TryParse(Text(answer, "expiration"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiration) &&
            DateTimeOffset.TryParse(Text(answer, "serverTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var serverTime))
        {
            return DateTimeOffset.UtcNow + (expiration - serverTime);
        }
        return DateTimeOffset.UtcNow.AddHours(3);
    }

    private void ForgetSession()
    {
        lock (_sessionGate)
        {
            _session = null;
            _sessionLoaded = true;
            _sessions.Delete(_sessionPath);
        }
    }

    #endregion

    #region Reading the account from Ubisoft

    /// <summary>The account's games from Ubisoft's ownership service, with names, pictures and achievement counts as the local listing has.</summary>
    private async Task<IReadOnlyList<StoreGame>> ReadOnlineLibraryAsync(CancellationToken cancellationToken)
    {
        var products = await WithOnlineAsync(online => online.GetProductsAsync(cancellationToken), cancellationToken).ConfigureAwait(false);

        List<StoreGame> games = new();
        // Every state counts: the launcher's own list has games it can no longer download too (state 0 or 4).
        foreach (var product in products.Where(p => p is { Owned: true, ProductType: 0, LockedBySubscription: false }))
        {
            var yaml = await BlockOfAsync(product, cancellationToken).ConfigureAwait(false);
            var configuration = yaml == null ? null : GameConfigurations.Parse(product.ProductId, product.ProductId, yaml);
            if (configuration is { IsGame: false })
            {
                continue;
            }

            var name = configuration?.Name;
            if (name == null && _names.TryGetName(product.ProductId, out var known) == true)
            {
                name = known;
            }
            if (name == null)
            {
                continue;
            }

            // The server's counts are read per game, on request; what is known without it is a game with no list at all.
            int? total = null;
            int? earned = null;
            if (configuration != null && configuration.AchievementsArchive == null)
            {
                total = 0;
                earned = 0;
            }

            var picture = configuration?.BackgroundImage ?? configuration?.ThumbImage;
            var key = product.ProductId.ToString(CultureInfo.InvariantCulture);
            games.Add(new StoreGame(
                ProviderId,
                key,
                key,
                name,
                picture == null ? null : AssetsUrl + picture,
                AchievementsTotal: total,
                AchievementsEarned: earned));
        }

        return games.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<StoreAchievement>> ReadOnlineAchievementsAsync(uint gameId, string locale, CancellationToken cancellationToken)
    {
        var yaml = await BlockOfAsync(new OnlineProduct(gameId, true, 0, 3, false, null), cancellationToken).ConfigureAwait(false);
        var game = yaml == null ? null : GameConfigurations.Parse(gameId, gameId, yaml);

        using var archive = await FindOrDownloadArchiveAsync(gameId, game, cancellationToken).ConfigureAwait(false);
        if (archive == null && game is { AchievementsArchive: null })
        {
            return Array.Empty<StoreAchievement>();
        }
        if (archive == null)
        {
            throw new ProviderException("no_list", "Ubisoft has no achievement list for this game that Caretaker can read.");
        }

        // The service knows a game by the block's sync id; a block without one is asked by product id, as a last try.
        var syncId = game?.AchievementsSyncId ?? gameId.ToString(CultureInfo.InvariantCulture);
        var earned = await WithOnlineAsync(online => online.GetAchievementsAsync(syncId, cancellationToken), cancellationToken).ConfigureAwait(false);
        return ToAchievements(
            archive,
            gameId,
            locale,
            earned.GroupBy(a => a.Id).ToDictionary(group => group.Key, group => group.Min(a => a.EarnedAt)),
            lockEarned: true);
    }

    /// <summary>Changes can be made when Caretaker is signed in to the account: the launcher's files are only read.</summary>
    public bool CanWrite => IsSignedInOnline;

    /// <summary>An achievement recorded as earned cannot be taken back: Ubisoft's service takes only what is earned.</summary>
    public bool CanRelock => false;

    /// <summary>
    /// Records achievements as earned on the signed-in account. The service takes a game's whole list of earned
    /// achievements, so what is already earned is read first and sent back with its times, and only the new ones
    /// are added, stamped with now. The list is read again afterwards, since the service answers a write with an
    /// empty message whether it kept the achievements or not. Not undoable, and only an unlock: a change to
    /// "locked" is refused.
    /// </summary>
    /// <param name="gameKey">The game's product id.</param>
    /// <param name="changes">Achievement id and the state it should have; each must be true.</param>
    /// <param name="cancellationToken">The token to cancel the write.</param>
    /// <exception cref="ProviderException">Not signed in, an achievement the game does not have, a lock, or a write the service did not keep.</exception>
    public async Task SetAchievementsAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken = default)
    {
        if (changes.Count == 0)
        {
            return;
        }

        if (!IsSignedInOnline)
        {
            throw new ProviderException("login_required", "Sign in to your Ubisoft account to change achievements: the launcher's files are only read.");
        }

        if (changes.Any(change => change.Value == false))
        {
            throw new ProviderException("cannot_relock", "An achievement recorded on a Ubisoft account cannot be locked again.");
        }

        if (!uint.TryParse(gameKey, NumberStyles.None, CultureInfo.InvariantCulture, out var gameId))
        {
            throw new ProviderException("invalid_game", $"'{gameKey}' is not a Ubisoft game id.");
        }

        var yaml = await BlockOfAsync(new OnlineProduct(gameId, true, 0, 3, false, null), cancellationToken).ConfigureAwait(false);
        var game = yaml == null ? null : GameConfigurations.Parse(gameId, gameId, yaml);
        if (game?.AchievementsSyncId is not { } syncId)
        {
            throw new ProviderException("no_sync_id", "Ubisoft's achievements service does not know this game by a key Caretaker can find.");
        }

        // Only achievements the game has: the service would take any number.
        using var archive = await FindOrDownloadArchiveAsync(gameId, game, cancellationToken).ConfigureAwait(false)
            ?? throw new ProviderException("no_list", "Ubisoft has no achievement list for this game that Caretaker can read.");
        var known = archive.ReadAchievements("en").Select(a => a.Id).ToHashSet();
        List<uint> wanted = new();
        foreach (var change in changes)
        {
            if (!uint.TryParse(change.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || !known.Contains(id))
            {
                throw new ProviderException("invalid_achievement", $"This game has no achievement '{change.Key}'.");
            }

            wanted.Add(id);
        }

        var current = await WithOnlineAsync(online => online.GetAchievementsAsync(syncId, cancellationToken), cancellationToken).ConfigureAwait(false);
        var added = wanted.Distinct().Where(id => current.Any(c => c.Id == id) == false).ToList();
        if (added.Count == 0)
        {
            return;
        }

        // The service's own unit: the times it sends are seconds, unless a record in the list is in milliseconds.
        var now = current.Any(c => c.RawTime >= 10_000_000_000)
            ? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            : (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var everything = current.Select(c => (c.Id, c.RawTime)).Concat(added.Select(id => (id, now))).ToList();

        try
        {
            await WithOnlineAsync(
                async online =>
                {
                    await online.WriteAchievementsAsync(syncId, everything, cancellationToken).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException e) when (e.ErrorCode == "network" && e.Message.Contains("did not accept the write", StringComparison.Ordinal))
        {
            throw new ProviderException("not_recorded", "Ubisoft did not accept the change.");
        }

        var after = await WithOnlineAsync(online => online.GetAchievementsAsync(syncId, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!added.Any(id => after.Any(a => a.Id == id)))
        {
            throw new ProviderException("not_recorded", "Ubisoft answered, but did not keep the change. Nothing was recorded.");
        }
    }

    /// <summary>A product's catalog block: the list's, else one asked for. Kept in memory for the run.</summary>
    private async Task<string?> BlockOfAsync(OnlineProduct product, CancellationToken cancellationToken)
    {
        if (product.Configuration is { Length: > 0 } given)
        {
            _blocks[product.ProductId] = given;
            return given;
        }
        if (_blocks.TryGetValue(product.ProductId, out var known) == true)
        {
            return known;
        }

        var asked = await WithOnlineAsync(online => online.GetConfigurationAsync(product.ProductId, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (asked != null)
        {
            _blocks[product.ProductId] = asked;
        }
        return asked;
    }

    /// <summary>
    /// Runs something on the one connection to Ubisoft's socket service, opening it (or opening it again, once, if it
    /// has dropped since it was last used) with a ticket that works. Calls take turns: the service's replies carry
    /// nothing that would tell two apart.
    /// </summary>
    private async Task<T> WithOnlineAsync<T>(Func<UbisoftOnline, Task<T>> action, CancellationToken cancellationToken)
    {
        await _connecting.WaitAsync(cancellationToken).ConfigureAwait(false);
        _idle?.Cancel();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnsureSignedInAsync(cancellationToken).ConfigureAwait(false);
                var session = Session ?? throw NotFound();

                try
                {
                    if (_connection == null || _connection.IsAlive == false || _connectionTicket != session.Ticket)
                    {
                        await DropConnectionAsync().ConfigureAwait(false);
                        _connection = await UbisoftOnline.ConnectAsync(_connector, session, cancellationToken).ConfigureAwait(false);
                        _connectionTicket = session.Ticket;
                    }
                    var result = await action(_connection).ConfigureAwait(false);
                    DropWhenIdle();
                    return result;
                }
                catch (DemuxException e)
                {
                    await DropConnectionAsync().ConfigureAwait(false);
                    if (attempt == 1)
                    {
                        throw new ProviderException("network", $"Ubisoft's service could not be used: {e.Message}");
                    }
                }
                catch (Exception e) when (e is System.IO.IOException or System.Net.Sockets.SocketException)
                {
                    await DropConnectionAsync().ConfigureAwait(false);
                    if (attempt == 1)
                    {
                        throw new ProviderException("network", "Ubisoft's service could not be reached.");
                    }
                }
            }
        }
        finally
        {
            _connecting.Release();
        }
    }

    /// <summary>Closes the connection <see cref="_idleLimit"/> after now, unless something uses it first.</summary>
    private void DropWhenIdle()
    {
        CancellationTokenSource idle = new();
        _idle = idle;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_idleLimit, idle.Token).ConfigureAwait(false);
                await _connecting.WaitAsync(idle.Token).ConfigureAwait(false);
                try
                {
                    if (idle.IsCancellationRequested == false)
                    {
                        await DropConnectionAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _connecting.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Used again before the limit.
            }
        });
    }

    private async Task DropConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        _connectionTicket = null;
        if (connection != null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    #endregion
}
