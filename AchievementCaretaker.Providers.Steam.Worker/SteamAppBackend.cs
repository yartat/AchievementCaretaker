/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// The helper's work: read one game's achievements from Steam, and set and store them.
/// Steam is reached through an <see cref="ISteamAppGateway"/>.
/// </summary>
public sealed class SteamAppBackend : ISteamAppBackend
{
    private readonly ISteamAppGateway _gateway;

    public SteamAppBackend(ISteamAppGateway gateway)
    {
        _gateway = gateway;
    }

    public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(CancellationToken cancellationToken)
    {
        await _gateway.EnsureUserStatsAsync(cancellationToken);

        var schema = _gateway.LoadSchema()
            ?? throw new ProviderException("no_schema", "Steam has no achievement schema cached for this game yet.");

        List<StoreAchievement> result = new();
        foreach (var def in SteamSchemaReader.ReadAchievements(schema, _gateway.AppId, _gateway.Language))
        {
            if (string.IsNullOrEmpty(def.Id) == true ||
                _gateway.TryGetAchievement(def.Id, out var unlocked, out var unlockTime) == false)
            {
                continue;
            }

            var icon = unlocked == true || string.IsNullOrEmpty(def.IconLocked) == true ? def.IconNormal : def.IconLocked;
            result.Add(new StoreAchievement(
                def.Id,
                def.Name,
                def.Description,
                string.IsNullOrEmpty(icon)
                    ? null
                    : string.Create(CultureInfo.InvariantCulture,
                        $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{_gateway.AppId}/{icon}"),
                def.Hidden,
                null,
                null,
                unlocked,
                null,
                unlocked == true && unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime) : null,
                (def.Permission & 3) != 0));
        }
        return result;
    }

    public async Task<IReadOnlyList<StoreStat>> GetStatsAsync(CancellationToken cancellationToken)
    {
        await _gateway.EnsureUserStatsAsync(cancellationToken);

        var schema = _gateway.LoadSchema()
            ?? throw new ProviderException("no_schema", "Steam has no schema cached for this game yet.");

        List<StoreStat> result = new();
        foreach (var def in SteamSchemaReader.ReadStats(schema, _gateway.AppId, _gateway.Language))
        {
            double value;
            if (def.Kind == StoreStatKind.Integer)
            {
                if (_gateway.TryGetIntStat(def.Id, out var whole) == false)
                {
                    continue;
                }
                value = whole;
            }
            else
            {
                if (_gateway.TryGetFloatStat(def.Id, out var fraction) == false)
                {
                    continue;
                }
                value = fraction;
            }
            result.Add(new StoreStat(
                def.Id, def.DisplayName, def.Kind, value, def.Min, def.Max, def.MaxChange,
                def.IncrementOnly, def.DefaultValue, def.Protected));
        }
        return result;
    }

    public async Task CommitAsync(
        IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
        IReadOnlyCollection<KeyValuePair<string, double>> statistics,
        CancellationToken cancellationToken)
    {
        if (achievements.Count == 0 && statistics.Count == 0)
        {
            return;
        }

        await _gateway.EnsureUserStatsAsync(cancellationToken);

        if (achievements.Count > 0)
        {
            var schema = _gateway.LoadSchema();
            var protectedIds = schema == null
                ? new HashSet<string>()
                : SteamSchemaReader.ReadAchievements(schema, _gateway.AppId, _gateway.Language)
                    .Where(d => (d.Permission & 3) != 0).Select(d => d.Id).ToHashSet();
            foreach (var change in achievements)
            {
                if (protectedIds.Contains(change.Key) == true)
                {
                    throw new ProviderException(
                        "protected", $"{change.Key} is protected by Steam; nothing has been stored.");
                }
                if (_gateway.SetAchievement(change.Key, change.Value) == false)
                {
                    throw new ProviderException(
                        "set_failed", $"Steam would not change {change.Key}; nothing has been stored.");
                }
            }
        }

        if (statistics.Count > 0)
        {
            var schema = _gateway.LoadSchema()
                ?? throw new ProviderException("no_schema", "Steam has no schema cached for this game yet.");
            var definitions = SteamSchemaReader.ReadStats(schema, _gateway.AppId, _gateway.Language)
                .ToDictionary(d => d.Id);
            foreach (var change in statistics)
            {
                if (definitions.TryGetValue(change.Key, out var def) == false)
                {
                    throw new ProviderException("unknown_stat", $"{change.Key} is not a statistic of this game.");
                }
                if (def.Protected == true)
                {
                    throw new ProviderException(
                        "protected", $"{change.Key} is protected by Steam; nothing has been stored.");
                }

                var accepted = def.Kind == StoreStatKind.Integer
                    ? _gateway.SetIntStat(change.Key, checked((int)Math.Round(change.Value)))
                    : _gateway.SetFloatStat(change.Key, (float)change.Value);
                if (accepted == false)
                {
                    throw new ProviderException(
                        "set_failed", $"Steam would not change {change.Key}; nothing has been stored.");
                }
            }
        }

        if (_gateway.StoreStats() == false)
        {
            throw new ProviderException("store_failed", "Steam would not store the changes.");
        }
    }

    #region Library

    public Task<LibraryScan> ScanLibraryAsync(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken)
    {
        // Every call is a Steam client call, one per candidate over the whole published list: off
        // the caller's thread, as the library window's sweep always was.
        return Task.Run(() => Scan(candidates, progress, cancellationToken), cancellationToken);
    }

    private LibraryScan Scan(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken)
    {
        List<StoreGame> owned = new();
        HashSet<string> seen = new();
        var total = candidates.Count;
        var step = Math.Max(1, total / 200);
        var examined = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            examined++;
            if (examined % step == 0)
            {
                progress?.Report(new LibraryScanProgress(examined, total));
            }

            if (uint.TryParse(candidate.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) == false ||
                seen.Add(candidate.Id) == false ||
                _gateway.IsSubscribed(appId) == false)
            {
                continue;
            }
            owned.Add(Describe(appId, candidate.Type));
        }
        progress?.Report(new LibraryScanProgress(total, total));

        // Asked here, the moment the last call is made: a scan that ran against a dead pipe is
        // not a scan, however many "not owned" it collected.
        var complete = _gateway.IsSteamStillRunning();
        return new LibraryScan(AccountOf(_gateway.SteamId), owned, complete);
    }

    public Task<string> GetAccountAsync(CancellationToken cancellationToken) =>
        Task.FromResult(AccountOf(_gateway.SteamId));

    private static string AccountOf(ulong steamId) => ((uint)(steamId & 0xFFFFFFFFUL)).ToString(CultureInfo.InvariantCulture);

    public Task<StoreGame> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken)
    {
        var found = uint.TryParse(candidate.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) == true &&
                    _gateway.IsSubscribed(appId) == true
            ? Describe(appId, candidate.Type)
            : null;
        return Task.FromResult(found);
    }

    private StoreGame Describe(uint appId, string type)
    {
        var id = appId.ToString(CultureInfo.InvariantCulture);
        var stats = _gateway.GetCachedStats(appId);
        return new StoreGame(
            SteamProvider.ProviderId,
            id,
            id,
            _gateway.GetAppData(appId, "name"),
            ImageUrl(appId),
            string.IsNullOrEmpty(type) == true ? "normal" : type,
            ParseUnixDate(_gateway.GetAppData(appId, "steam_release_date")),
            ParseInt(_gateway.GetAppData(appId, "review_percentage")),
            ParseInt(_gateway.GetAppData(appId, "review_score")),
            stats?.PlaytimeMinutes,
            stats?.LastPlayed == null ? null : new DateTimeOffset(stats.Value.LastPlayed.Value),
            stats?.AchievementsTotal >= 0 ? stats.Value.AchievementsTotal : null,
            stats?.AchievementsEarned >= 0 ? stats.Value.AchievementsEarned : null);
    }

    /// <summary>
    /// The small capsule in the game's language, then in English, then the old logo: the order the
    /// library window always looked in.
    /// </summary>
    private string ImageUrl(uint appId)
    {
        var language = _gateway.Language;

        var candidate = _gateway.GetAppData(appId, $"small_capsule/{language}");
        if (string.IsNullOrEmpty(candidate) == false)
        {
            return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/{candidate}";
        }

        if (language != "english")
        {
            candidate = _gateway.GetAppData(appId, "small_capsule/english");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/{candidate}";
            }
        }

        candidate = _gateway.GetAppData(appId, "logo");
        return string.IsNullOrEmpty(candidate) == false
            ? $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{appId}/{candidate}.jpg"
            : null;
    }

    private static int? ParseInt(string value) =>
        string.IsNullOrEmpty(value) == false &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseUnixDate(string value) =>
        string.IsNullOrEmpty(value) == false &&
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
        seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    #endregion

    public async Task ResetAsync(bool includeAchievements, CancellationToken cancellationToken)
    {
        await _gateway.EnsureUserStatsAsync(cancellationToken);
        if (_gateway.ResetAllStats(includeAchievements) == false)
        {
            throw new ProviderException("reset_failed", "Steam would not reset the statistics.");
        }
    }
}
