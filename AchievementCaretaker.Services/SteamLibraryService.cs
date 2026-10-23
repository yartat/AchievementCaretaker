/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services;

/// <summary>The <see cref="ISteamLibraryService"/>. Holds no state, so it is a singleton.</summary>
public sealed class SteamLibraryService : ISteamLibraryService
{
    private const string PublishedListUrl = "https://gib.me/sam/games.xml";

    private readonly ILibraryScanProvider _steam;
    private readonly IHttpClientFactory _http;

    public SteamLibraryService(ILibraryScanProvider steam, IHttpClientFactory http)
    {
        _steam = steam;
        _http = http;
    }

    public Task<string> GetAccountAsync(CancellationToken cancellationToken = default) =>
        _steam.GetAccountAsync(cancellationToken);

    public async Task<IReadOnlyList<LibraryCandidate>> GetPublishedGamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _http.CreateClient().GetByteArrayAsync(new Uri(PublishedListUrl), cancellationToken);
            return ParseGameList(bytes);
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested == false && e is HttpRequestException or TaskCanceledException or XmlException)
        {
            throw new ProviderException("list_unavailable", "The list of Steam games could not be downloaded: " + e.Message);
        }
    }

    public Task<LibraryScan> ScanAsync(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken = default) =>
        _steam.ScanLibraryAsync(candidates, progress, cancellationToken);

    public Task<StoreGame> FindOwnedAsync(uint id, CancellationToken cancellationToken = default) =>
        _steam.FindOwnedAsync(new LibraryCandidate(id.ToString(CultureInfo.InvariantCulture), "normal"), cancellationToken);

    /// <summary>Reads <c>/games/game</c>: the app id as the text, the type as an attribute that defaults to "normal".</summary>
    public static IReadOnlyList<LibraryCandidate> ParseGameList(byte[] bytes)
    {
        List<LibraryCandidate> candidates = new();
        using MemoryStream stream = new(bytes, false);
        XPathDocument document = new(stream);
        var nodes = document.CreateNavigator().Select("/games/game");
        while (nodes.MoveNext() == true)
        {
            var type = nodes.Current.GetAttribute("type", "");
            candidates.Add(new LibraryCandidate(
                nodes.Current.ValueAsLong.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(type) == true ? "normal" : type));
        }
        return candidates;
    }
}
