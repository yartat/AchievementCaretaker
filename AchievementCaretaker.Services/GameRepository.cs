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
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.Services;

/// <summary>
/// The <see cref="IGameRepository"/> over the scoped <see cref="IDbConnection"/>: SQLite through
/// ADO.NET, with Dapper to build the commands and read the rows. Two tables and a handful of
/// statements do not need a heavier tool.
/// </summary>
/// <remarks>
/// <para>
/// The connection is shared by everything in its scope: the Steam library saves from a pool
/// thread while the store libraries load and save from the UI thread. Every public member takes
/// a lock on the connection, which the database relocator takes too.
/// </para>
/// <para>
/// The tables are made by <see cref="IDatabaseMigrator"/> at start, not here.
/// </para>
/// </remarks>
public sealed class GameRepository : IGameRepository
{
    private readonly IDbConnection _connection;
    private readonly DatabaseState _state;

    public GameRepository(IDbConnection connection, DatabaseState state)
    {
        _connection = connection;
        _state = state;
    }

    public bool IsAvailable => _state.IsAvailable;

    public string Error => _state.Error;

    private void EnsureOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    /// <summary>Runs <paramref name="work"/> under the connection's lock, or returns <paramref name="unavailable"/>.</summary>
    private T Run<T>(Func<IDbConnection, T> work, T unavailable)
    {
        if (_state.IsAvailable == false)
        {
            return unavailable;
        }
        lock (_connection)
        {
            EnsureOpen();
            return work(_connection);
        }
    }

    private void Run(Action<IDbConnection> work) => Run(
        connection =>
        {
            work(connection);
            return true;
        },
        false);

    #region Steam library

    public uint GetLastSteamAccount() => Run(
        connection => uint.TryParse(
            connection.ExecuteScalar<string>("SELECT value FROM meta WHERE key = 'last_steam_account'"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var account) ? account : 0u,
        0u);

    public void SetLastSteamAccount(uint account) => Run(connection => connection.Execute(
        """
        INSERT INTO meta (key, value) VALUES ('last_steam_account', @value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value
        """,
        new { value = account.ToString(CultureInfo.InvariantCulture) }));

    public HashSet<uint> LoadAllGameIds() => Run(
        connection => connection.Query<long>("SELECT DISTINCT id FROM games").Select(id => (uint)id).ToHashSet(),
        new HashSet<uint>());

    public IReadOnlyList<CachedGame> LoadAll(uint account) => Run(
        connection => (IReadOnlyList<CachedGame>)connection.Query<GameRow>(
            """
            SELECT id AS Id, type AS Type, name AS Name, image_url AS ImageUrl,
                   release_date AS ReleaseDate, rating_percent AS RatingPercent,
                   rating_score AS RatingScore, playtime_minutes AS PlaytimeMinutes,
                   last_played AS LastPlayed, ach_total AS AchievementsTotal,
                   ach_earned AS AchievementsEarned, has_stats AS HasStats
            FROM games
            WHERE account = @account
            """,
            new { account = (long)account }).Select(row => row.ToModel()).ToList(),
        Array.Empty<CachedGame>());

    public void Sync(uint account, IReadOnlyCollection<CachedGame> games, bool removeMissing = true) =>
        Run(connection =>
        {
            using var transaction = connection.BeginTransaction();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            connection.Execute(
                """
                INSERT INTO games (account, id, type, name, image_url, release_date, rating_percent,
                                   rating_score, playtime_minutes, last_played, ach_total,
                                   ach_earned, has_stats, updated_utc)
                VALUES (@account, @id, @type, @name, @url, @released, @percent, @score, @playtime,
                        @lastPlayed, @total, @earned, @hasStats, @updated)
                ON CONFLICT(account, id) DO UPDATE SET
                    type = excluded.type,
                    name = excluded.name,
                    image_url = excluded.image_url,
                    release_date = excluded.release_date,
                    rating_percent = excluded.rating_percent,
                    rating_score = excluded.rating_score,
                    playtime_minutes = excluded.playtime_minutes,
                    last_played = excluded.last_played,
                    ach_total = excluded.ach_total,
                    ach_earned = excluded.ach_earned,
                    has_stats = excluded.has_stats,
                    updated_utc = excluded.updated_utc
                """,
                games.Select(game => new
                {
                    account = (long)account,
                    id = (long)game.Id,
                    type = game.Type,
                    name = game.Name,
                    url = game.ImageUrl,
                    released = ToUnix(game.ReleaseDate),
                    percent = game.RatingPercent,
                    score = game.RatingScore,
                    playtime = game.PlaytimeMinutes,
                    lastPlayed = ToUnix(game.LastPlayed),
                    total = game.AchievementsTotal,
                    earned = game.AchievementsEarned,
                    hasStats = game.HasStats ? 1 : 0,
                    updated = now,
                }).ToList(),
                transaction);

            if (removeMissing == true)
            {
                var keep = games.Select(game => (long)game.Id).ToHashSet();
                var gone = connection
                    .Query<long>("SELECT id FROM games WHERE account = @account", new { account = (long)account }, transaction)
                    .Where(id => keep.Contains(id) == false)
                    .Select(id => new { account = (long)account, id })
                    .ToList();
                connection.Execute("DELETE FROM games WHERE account = @account AND id = @id", gone, transaction);
            }

            transaction.Commit();
        });

    #endregion

    #region Store libraries

    public IReadOnlyList<CachedStoreGame> LoadStoreGames(string provider, string account) => Run(
        connection => (IReadOnlyList<CachedStoreGame>)connection.Query<StoreGameRow>(
            """
            SELECT id AS Id, game_key AS GameKey, name AS Name, image_url AS ImageUrl,
                   ach_total AS AchievementsTotal, ach_earned AS AchievementsEarned,
                   has_progress AS HasProgress
            FROM store_games
            WHERE provider = @provider AND account = @account
            """,
            new { provider, account }).Select(row => row.ToModel()).ToList(),
        Array.Empty<CachedStoreGame>());

    public void SyncStoreGames(string provider, string account, IReadOnlyCollection<CachedStoreGame> games) =>
        Run(connection =>
        {
            using var transaction = connection.BeginTransaction();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // The counts are left out of the update on purpose: see IGameRepository.SyncStoreGames.
            connection.Execute(
                """
                INSERT INTO store_games (provider, account, id, game_key, name, image_url, updated_utc)
                VALUES (@provider, @account, @id, @key, @name, @url, @updated)
                ON CONFLICT(provider, account, id) DO UPDATE SET
                    game_key = excluded.game_key,
                    name = excluded.name,
                    image_url = excluded.image_url,
                    updated_utc = excluded.updated_utc
                """,
                games.Select(game => new
                {
                    provider,
                    account,
                    id = game.Id,
                    key = game.Key,
                    name = game.Name,
                    url = game.ImageUrl,
                    updated = now,
                }).ToList(),
                transaction);

            var keep = games.Select(game => game.Id).ToHashSet();
            var gone = connection
                .Query<string>(
                    "SELECT id FROM store_games WHERE provider = @provider AND account = @account",
                    new { provider, account },
                    transaction)
                .Where(id => keep.Contains(id) == false)
                .Select(id => new { provider, account, id })
                .ToList();
            connection.Execute(
                "DELETE FROM store_games WHERE provider = @provider AND account = @account AND id = @id",
                gone,
                transaction);

            transaction.Commit();
        });

    public void SaveStoreProgress(string provider, string account, IEnumerable<(string Id, int Earned, int Total)> progress) =>
        Run(connection =>
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(
                """
                UPDATE store_games
                SET ach_total = @total, ach_earned = @earned, has_progress = 1
                WHERE provider = @provider AND account = @account AND id = @id
                """,
                progress.Select(p => new { provider, account, id = p.Id, total = p.Total, earned = p.Earned }).ToList(),
                transaction);
            transaction.Commit();
        });

    #endregion

    #region Rows

    private static long? ToUnix(DateTime? value) => value.HasValue == false
        ? null
        : new DateTimeOffset(value.Value.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();

    private static DateTime? FromUnix(long? value) => value.HasValue == false
        ? null
        : DateTimeOffset.FromUnixTimeSeconds(value.Value).LocalDateTime;

    /// <summary>What a row of <c>games</c> comes back as: SQLite's integers are all 64-bit, and dates are unix seconds.</summary>
    private sealed class GameRow
    {
        public long Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public long? ReleaseDate { get; set; }
        public long? RatingPercent { get; set; }
        public long? RatingScore { get; set; }
        public long PlaytimeMinutes { get; set; }
        public long? LastPlayed { get; set; }
        public long AchievementsTotal { get; set; }
        public long AchievementsEarned { get; set; }
        public long HasStats { get; set; }

        public CachedGame ToModel() => new()
        {
            Id = (uint)Id,
            Type = Type ?? "normal",
            Name = Name,
            ImageUrl = ImageUrl,
            ReleaseDate = FromUnix(ReleaseDate),
            RatingPercent = (int?)RatingPercent,
            RatingScore = (int?)RatingScore,
            PlaytimeMinutes = (int)PlaytimeMinutes,
            LastPlayed = FromUnix(LastPlayed),
            AchievementsTotal = (int)AchievementsTotal,
            AchievementsEarned = (int)AchievementsEarned,
            HasStats = HasStats != 0,
        };
    }

    private sealed class StoreGameRow
    {
        public string Id { get; set; }
        public string GameKey { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public long AchievementsTotal { get; set; }
        public long AchievementsEarned { get; set; }
        public long HasProgress { get; set; }

        public CachedStoreGame ToModel() => new()
        {
            Id = Id,
            Key = GameKey,
            Name = Name,
            ImageUrl = ImageUrl,
            AchievementsTotal = (int)AchievementsTotal,
            AchievementsEarned = (int)AchievementsEarned,
            HasProgress = HasProgress != 0,
        };
    }

    #endregion
}
