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
 *
 * and on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * configuration catalog, the achievement archives and the spool; and, for an account signed in to,
 * speaks the socket service UplayKit documents. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// Ubisoft Connect, read from the files the launcher keeps on this computer: the games it lists as owned,
/// each game's achievement archive, and the spool of what was earned here.
/// </summary>
/// <remarks>
/// <para>
/// There is no account to sign in to and nothing is asked of Ubisoft's servers: whoever is logged in to the
/// launcher on this computer is the account (<see cref="ILocalStoreAccount"/>). It is read only.
/// </para>
/// <para>
/// What the files hold is partial, and it is shown as such. The ownership cache lists everything the account
/// owns, but only the games a name is known for are listed. An archive exists only for games the launcher
/// has fetched one for, so a game without one has no achievements to show (an error that says so, not an
/// empty list, which would read as "this game has none"). The spool has only what was earned on this
/// computer, so an achievement not in it is shown as not earned. See <c>docs/UBISOFT-RESEARCH.md</c>.
/// </para>
/// </remarks>
public sealed partial class UbisoftProvider : IWritableStoreProvider, ILocalStoreAccount, IDisposable, IAsyncDisposable
{
    /// <summary>The provider's id: <see cref="StoreGame.Provider"/> of its games.</summary>
    public const string ProviderId = "ubisoft";

    /// <summary>The name of the <see cref="HttpClient"/> this provider asks the factory for, per request.</summary>
    public const string HttpClientName = "ubisoft";

    /// <summary>Ubisoft's public CDN folder with the launcher's pictures and achievement archives; no sign-in is needed.</summary>
    private const string AssetsUrl = "https://static3.cdn.ubi.com/orbit/uplay_launcher_3_0/assets/";

    // Ubisoft's own sign-in page and session service, the ones its web client uses (see docs/UBISOFT-RESEARCH.md).
    // The page leaves the session in its local storage; the script below hands it over in an address.
    private const string AppId = "f68a4bb5-608a-4ff2-8123-be8ef797e0a6";
    private const string GenomeId = "954e66a0-be1b-4aa0-9690-fb75201e4e9e";
    private const string SessionsUrl = "https://public-ubiservices.ubi.com/v3/profiles/sessions";
    private const string SignInEndAddress = "https://connect.ubisoft.com/change_domain/";
    private const string WebOrigin = "https://connect.ubisoft.com";
    private const string WebUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private const string WatchScript = @"
        (function () {
            if (window.__caretakerUbisoftWatcher) { return; }
            function finish() {
                var loginData = window.localStorage.getItem('PRODloginData');
                if (!loginData) { return; }
                try {
                    var session = JSON.parse(loginData);
                    if (!session.ticket || !session.sessionId || !session.userId) { return; }
                } catch (error) { return; }
                window.clearInterval(window.__caretakerUbisoftWatcher);
                window.location.replace('https://connect.ubisoft.com/change_domain/' + loginData + ',' +
                    (window.localStorage.getItem('PRODrememberMe') || 'null'));
            }
            window.__caretakerUbisoftWatcher = window.setInterval(finish, 500);
            finish();
        }());";

    private readonly UbisoftLocations _locations;
    private readonly string _iconDirectory;
    private readonly string _archiveDirectory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionStore _sessions;
    private readonly string _sessionPath;
    private readonly IUbisoftConnector _connector;
    private readonly GameNames _names;

    /// <summary>Makes a provider over the launcher's files.</summary>
    /// <param name="locations">Where the launcher keeps them.</param>
    /// <param name="iconDirectory">
    /// Where achievement icons are put, out of the archives: an archive is a zip, and the application shows
    /// files.
    /// </param>
    /// <param name="archiveDirectory">Where archives downloaded from Ubisoft's CDN are kept.</param>
    /// <param name="httpClientFactory">Asked for <see cref="HttpClientName"/> per request.</param>
    /// <param name="sessions">Where the sign-in is kept between runs.</param>
    /// <param name="sessionPath">The sign-in's file.</param>
    /// <param name="connector">Connects to Ubisoft's socket service, for an account signed in to.</param>
    /// <param name="names">What the games are called when the catalog does not say; the list that ships with the application when null.</param>
    public UbisoftProvider(
        UbisoftLocations locations,
        string iconDirectory,
        string archiveDirectory,
        IHttpClientFactory httpClientFactory,
        ISessionStore sessions,
        string sessionPath,
        IUbisoftConnector connector,
        GameNames? names = null)
    {
        _locations = locations;
        _iconDirectory = iconDirectory;
        _archiveDirectory = archiveDirectory;
        _httpClientFactory = httpClientFactory;
        _sessions = sessions;
        _sessionPath = sessionPath;
        _connector = connector;
        _names = names ?? GameNames.Embedded;
    }

    /// <inheritdoc />
    public string Id => ProviderId;

    /// <inheritdoc />
    public string DisplayName => "Ubisoft Connect";

    /// <summary>The launcher's user id, which is what the spool is filed under; null when there is no cache.</summary>
    private string? LocalUserId => OwnershipFile.Find(_locations)?.UserId;

    /// <inheritdoc />
    public string UnavailableMessage =>
        "Ubisoft Connect was not found on this computer, or nobody has logged in to it yet. " +
        "Install it and log in, then look again, or sign in with your Ubisoft account here.";

    /// <inheritdoc />
    public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
        IsSignedInOnline == true ? ReadOnlineLibraryAsync(cancellationToken) : Task.Run(ReadLibrary, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
        string gameKey,
        string locale = "en",
        CancellationToken cancellationToken = default) =>
        ReadAchievementsAsync(gameKey, locale, cancellationToken);

    private IReadOnlyList<StoreGame> ReadLibrary()
    {
        var ownership = OwnershipFile.Find(_locations) ?? throw NotFound();
        var catalog = Catalog();

        List<StoreGame> games = new();
        HashSet<uint> listed = new();
        foreach (var id in ownership.GameIds)
        {
            catalog.TryGetValue(id, out var configuration);

            // The cache lists a product under both of its ids; it is one game.
            if (configuration != null && listed.Add(configuration.ProductId) == false)
            {
                continue;
            }

            // A block that is not a game is an add-on or a tool; no block at all falls back to the shipped names.
            if (configuration is { IsGame: false })
            {
                continue;
            }

            var name = configuration?.Name;
            if (name == null && _names.TryGetName(id, out var known) == true)
            {
                name = known;
            }
            if (name == null)
            {
                continue;
            }

            // What an archive and the spool already say, so a game with them shows its meter at once.
            int? total = null;
            int? earned = null;
            using (var archive = FindArchive(id))
            {
                if (archive != null)
                {
                    var achievements = archive.ReadAchievements("en");
                    var spool = SpoolFile.ReadForGame(_locations, ownership.UserId, id);
                    total = achievements.Count;
                    earned = achievements.Count(a => spool.Any(record => record.AchievementId == a.Id));
                }
                else if (configuration != null && configuration.AchievementsArchive == null)
                {
                    total = 0;
                    earned = 0;
                }
            }

            var picture = configuration?.BackgroundImage ?? configuration?.ThumbImage;
            var key = id.ToString(CultureInfo.InvariantCulture);
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

    /// <summary>The catalog by every id a product is known by.</summary>
    private Dictionary<uint, GameConfiguration> Catalog()
    {
        Dictionary<uint, GameConfiguration> catalog = new();
        foreach (var configuration in GameConfigurations.ReadFile(_locations))
        {
            // A product's own id wins over another product's launch id, whatever the order of the file.
            catalog.TryAdd(configuration.LaunchId, configuration);
            catalog[configuration.ProductId] = configuration;
        }
        return catalog;
    }

    /// <summary>The archive the launcher saved for a game, else one downloaded earlier; null when there is neither.</summary>
    private AchievementArchive? FindArchive(uint gameId) =>
        AchievementArchive.Find(_locations.AchievementArchives, gameId) ?? AchievementArchive.Find(_archiveDirectory, gameId);

    /// <summary>Downloads a game's archive from Ubisoft's CDN into the archive folder; null when what came is not a zip.</summary>
    private async Task<AchievementArchive?> DownloadArchiveAsync(uint gameId, string spec, CancellationToken cancellationToken)
    {
        var file = Path.Combine(_archiveDirectory, gameId.ToString(CultureInfo.InvariantCulture) + "_" + Path.GetFileName(spec));
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            var bytes = await client.GetByteArrayAsync(AssetsUrl + Uri.EscapeDataString(spec), cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(_archiveDirectory);
            var temporary = file + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            throw new ProviderException("network", "Ubisoft's server could not be reached for this game's achievement list.");
        }

        return AchievementArchive.TryOpen(file);
    }

    private async Task<IReadOnlyList<StoreAchievement>> ReadAchievementsAsync(string gameKey, string locale, CancellationToken cancellationToken)
    {
        if (uint.TryParse(gameKey, NumberStyles.None, CultureInfo.InvariantCulture, out var gameId) == false)
        {
            throw new ProviderException("invalid_game", $"'{gameKey}' is not a Ubisoft game id.");
        }

        if (IsSignedInOnline == true)
        {
            return await ReadOnlineAchievementsAsync(gameId, locale, cancellationToken).ConfigureAwait(false);
        }

        var ownership = OwnershipFile.Find(_locations) ?? throw NotFound();
        var spool = SpoolFile.ReadForGame(_locations, ownership.UserId, gameId);

        var catalog = await Task.Run(Catalog, cancellationToken).ConfigureAwait(false);
        catalog.TryGetValue(gameId, out var game);

        using var archive = await FindOrDownloadArchiveAsync(gameId, game, cancellationToken).ConfigureAwait(false);
        if (archive == null && game is { AchievementsArchive: null })
        {
            // The catalog knows the game and names no list: it has none.
            return Array.Empty<StoreAchievement>();
        }
        if (archive == null)
        {
            throw new ProviderException(
                "no_local_data",
                "Ubisoft Connect has not saved this game's achievement list on this computer, so there is nothing to show. " +
                "It saves one when it needs it, for instance when the game's Achievements page is opened in Ubisoft Connect. " +
                "Signing in with your Ubisoft account here does not need the launcher." +
                (spool.Count > 0
                    ? $" {spool.Count} achievements are recorded as earned here, but they cannot be named without the list."
                    : ""));
        }

        return ToAchievements(archive, gameId, locale, spool.ToDictionary(record => record.AchievementId, record => (DateTimeOffset?)record.EarnedAt));
    }

    /// <summary>The launcher's archive, else one downloaded earlier, else the one the catalog names, downloaded now; null when none.</summary>
    private async Task<AchievementArchive?> FindOrDownloadArchiveAsync(uint gameId, GameConfiguration? game, CancellationToken cancellationToken) =>
        FindArchive(gameId) ?? (game?.AchievementsArchive is { } spec
            ? await DownloadArchiveAsync(gameId, spec, cancellationToken).ConfigureAwait(false)
            : null);

    /// <summary>The archive's achievements, each marked earned if it is in <paramref name="earnedAt"/> (with its time, when there is one).</summary>
    private IReadOnlyList<StoreAchievement> ToAchievements(
        AchievementArchive archive,
        uint gameId,
        string locale,
        IReadOnlyDictionary<uint, DateTimeOffset?> earnedAt,
        bool lockEarned = false)
    {
        var icons = ExtractIcons(archive, gameId);
        return archive.ReadAchievements(locale)
            .Select(achievement =>
            {
                var earned = earnedAt.TryGetValue(achievement.Id, out var when);
                return new StoreAchievement(
                    achievement.Id.ToString(CultureInfo.InvariantCulture),
                    achievement.Name,
                    achievement.Description,
                    icons.GetValueOrDefault(achievement.Id),
                    false,
                    null,
                    null,
                    earned,
                    null,
                    earned == true ? when : null,
                    Protected: lockEarned == true && earned == true);
            })
            .ToList();
    }

    /// <summary>
    /// Puts the archive's icons in files, once: an archive's name changes with its contents, so a file
    /// older than the archive is written again. Returns each icon's address by achievement id.
    /// </summary>
    private Dictionary<uint, string> ExtractIcons(AchievementArchive archive, uint gameId)
    {
        Dictionary<uint, string> addresses = new();
        var directory = Path.Combine(_iconDirectory, gameId.ToString(CultureInfo.InvariantCulture));
        var archiveWritten = File.GetLastWriteTimeUtc(archive.Path);
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var achievement in archive.ReadAchievements("en"))
            {
                if (archive.HasIcon(achievement.Id) == false)
                {
                    continue;
                }

                var file = Path.Combine(directory, achievement.Id.ToString(CultureInfo.InvariantCulture) + ".png");
                if (File.Exists(file) == false || File.GetLastWriteTimeUtc(file) < archiveWritten)
                {
                    using var destination = File.Create(file);
                    archive.CopyIcon(achievement.Id, destination);
                }

                addresses[achievement.Id] = new Uri(file).AbsoluteUri;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An icon is not worth failing the list for: what could be written is shown.
        }

        return addresses;
    }

    private ProviderException NotFound() => new("login_required", UnavailableMessage);
}
