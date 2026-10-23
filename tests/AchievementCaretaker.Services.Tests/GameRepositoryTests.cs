/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using AchievementCaretaker.Services.Models;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class GameRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private IGameRepository Repo => _db.Repository;

    private static CachedGame Game(uint id, string name = null) => new() { Id = id, Name = name ?? "Game " + id, Type = "normal" };

    #region Steam library

    [Fact]
    public void Accounts_are_kept_side_by_side()
    {
        Repo.Sync(1, new[] { Game(10), Game(11) });
        Repo.Sync(2, new[] { Game(11), Game(12) });

        Assert.Equal(new uint[] { 10, 11 }, Repo.LoadAll(1).Select(g => g.Id).Order());
        Assert.Equal(new uint[] { 11, 12 }, Repo.LoadAll(2).Select(g => g.Id).Order());
    }

    [Fact]
    public void Every_account_s_game_ids_are_known_together()
    {
        Repo.Sync(1, new[] { Game(10), Game(11) });
        Repo.Sync(2, new[] { Game(11), Game(12) });

        Assert.Equal(new uint[] { 10, 11, 12 }, Repo.LoadAllGameIds().Order());
    }

    [Fact]
    public void A_full_sync_removes_what_the_account_no_longer_owns_and_only_its_own()
    {
        Repo.Sync(1, new[] { Game(10), Game(11) });
        Repo.Sync(2, new[] { Game(10) });

        Repo.Sync(1, new[] { Game(10) });

        Assert.Equal(new uint[] { 10 }, Repo.LoadAll(1).Select(g => g.Id));
        Assert.Single(Repo.LoadAll(2));
    }

    [Fact]
    public void A_partial_sync_adds_and_updates_but_deletes_nothing()
    {
        Repo.Sync(1, new[] { Game(10), Game(11) });

        Repo.Sync(1, new[] { Game(10, "Renamed"), Game(12) }, removeMissing: false);

        var games = Repo.LoadAll(1).ToDictionary(g => g.Id);
        Assert.Equal(new uint[] { 10, 11, 12 }, games.Keys.Order());
        Assert.Equal("Renamed", games[10].Name);
    }

    [Fact]
    public void An_empty_full_sync_empties_the_account()
    {
        Repo.Sync(1, new[] { Game(10) });

        Repo.Sync(1, Array.Empty<CachedGame>());

        Assert.Empty(Repo.LoadAll(1));
    }

    [Fact]
    public void Every_field_of_a_game_survives_a_round_trip()
    {
        var released = new DateTime(2020, 5, 17, 10, 30, 0, DateTimeKind.Local);
        var played = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var game = new CachedGame
        {
            Id = 4_000_000_000,
            Type = "demo",
            Name = "Name",
            ImageUrl = "https://example/img.jpg",
            ReleaseDate = released,
            RatingPercent = 93,
            RatingScore = 8,
            PlaytimeMinutes = 1234,
            LastPlayed = played,
            AchievementsTotal = 50,
            AchievementsEarned = 12,
            HasStats = true,
        };

        Repo.Sync(7, new[] { game });
        var back = Repo.LoadAll(7).Single();

        Assert.Equal(game.Id, back.Id);
        Assert.Equal("demo", back.Type);
        Assert.Equal("Name", back.Name);
        Assert.Equal("https://example/img.jpg", back.ImageUrl);
        Assert.Equal(released, back.ReleaseDate);
        Assert.Equal(93, back.RatingPercent);
        Assert.Equal(8, back.RatingScore);
        Assert.Equal(1234, back.PlaytimeMinutes);
        Assert.Equal(played, back.LastPlayed);
        Assert.Equal(50, back.AchievementsTotal);
        Assert.Equal(12, back.AchievementsEarned);
        Assert.True(back.HasStats);
    }

    [Fact]
    public void Unknown_values_stay_unknown()
    {
        Repo.Sync(1, new[] { new CachedGame { Id = 5, Name = "Bare" } });

        var back = Repo.LoadAll(1).Single();

        Assert.Null(back.ReleaseDate);
        Assert.Null(back.LastPlayed);
        Assert.Null(back.RatingPercent);
        Assert.Null(back.RatingScore);
        Assert.Null(back.ImageUrl);
        Assert.Equal(-1, back.AchievementsTotal);
        Assert.Equal(-1, back.AchievementsEarned);
        Assert.False(back.HasStats);
    }

    [Fact]
    public void A_game_saved_without_a_type_reads_back_as_normal()
    {
        Repo.Sync(1, new[] { new CachedGame { Id = 5, Name = "Typeless" } });

        Assert.Equal("normal", Repo.LoadAll(1).Single().Type);
    }

    [Fact]
    public void The_last_steam_account_is_remembered_across_scopes()
    {
        Assert.Equal(0u, Repo.GetLastSteamAccount());
        Repo.SetLastSteamAccount(42);
        Repo.SetLastSteamAccount(43);

        _db.Reopen();

        Assert.Equal(43u, _db.Repository.GetLastSteamAccount());
    }

    #endregion

    #region Store libraries

    private static CachedStoreGame Store(string id, string name = null) => new() { Id = id, Key = "k" + id, Name = name ?? "Store " + id };

    [Fact]
    public void A_store_library_is_kept_per_provider_and_account()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1"), Store("2") });
        Repo.SyncStoreGames("gog", "u2", new[] { Store("1") });
        Repo.SyncStoreGames("epic", "u1", new[] { Store("9") });

        Assert.Equal(new[] { "1", "2" }, Repo.LoadStoreGames("gog", "u1").Select(g => g.Id).Order());
        Assert.Single(Repo.LoadStoreGames("gog", "u2"));
        Assert.Equal("9", Repo.LoadStoreGames("epic", "u1").Single().Id);
        Assert.Empty(Repo.LoadStoreGames("epic", "u2"));
    }

    [Fact]
    public void A_store_sync_deletes_only_the_games_of_its_own_provider_and_account()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1"), Store("2") });
        Repo.SyncStoreGames("gog", "u2", new[] { Store("2") });

        Repo.SyncStoreGames("gog", "u1", new[] { Store("1") });

        Assert.Equal("1", Repo.LoadStoreGames("gog", "u1").Single().Id);
        Assert.Equal("2", Repo.LoadStoreGames("gog", "u2").Single().Id);
    }

    [Fact]
    public void A_store_sync_keeps_the_progress_already_read_and_renames()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1") });
        Repo.SaveStoreProgress("gog", "u1", new[] { ("1", 3, 10) });

        Repo.SyncStoreGames("gog", "u1", new[] { Store("1", "New name") });

        var game = Repo.LoadStoreGames("gog", "u1").Single();
        Assert.Equal("New name", game.Name);
        Assert.True(game.HasProgress);
        Assert.Equal((3, 10), (game.AchievementsEarned, game.AchievementsTotal));
    }

    [Fact]
    public void A_game_with_no_progress_read_is_unknown_not_zero()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1") });

        var game = Repo.LoadStoreGames("gog", "u1").Single();

        Assert.False(game.HasProgress);
        Assert.Equal(-1, game.AchievementsTotal);
        Assert.Equal(-1, game.AchievementsEarned);
    }

    [Fact]
    public void Progress_belongs_to_its_own_account()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1") });
        Repo.SyncStoreGames("gog", "u2", new[] { Store("1") });

        Repo.SaveStoreProgress("gog", "u1", new[] { ("1", 5, 5) });

        Assert.True(Repo.LoadStoreGames("gog", "u1").Single().HasProgress);
        Assert.False(Repo.LoadStoreGames("gog", "u2").Single().HasProgress);
    }

    [Fact]
    public void Progress_for_a_game_that_is_not_saved_is_ignored()
    {
        Repo.SyncStoreGames("gog", "u1", new[] { Store("1") });

        Repo.SaveStoreProgress("gog", "u1", new[] { ("nope", 1, 1) });

        Assert.Single(Repo.LoadStoreGames("gog", "u1"));
    }

    [Fact]
    public void The_store_tables_and_the_steam_table_do_not_touch_each_other()
    {
        Repo.Sync(1, new[] { Game(10) });
        Repo.SyncStoreGames("gog", "1", new[] { Store("10") });

        Repo.SyncStoreGames("gog", "1", Array.Empty<CachedStoreGame>());

        Assert.Single(Repo.LoadAll(1));
    }

    #endregion

    #region Lifetime and registration

    [Fact]
    public void Data_written_in_one_scope_is_there_in_the_next()
    {
        Repo.Sync(1, new[] { Game(10) });

        _db.Reopen();

        Assert.Single(_db.Repository.LoadAll(1));
    }

    [Fact]
    public void Within_a_scope_the_connection_and_command_are_shared_and_between_scopes_they_are_not()
    {
        var first = _db.Services;
        var second = _db.NewScope();

        Assert.Same(first.GetRequiredService<IDbConnection>(), first.GetRequiredService<IDbConnection>());
        Assert.Same(first.GetRequiredService<IDbCommand>(), first.GetRequiredService<IDbCommand>());
        Assert.NotSame(first.GetRequiredService<IDbConnection>(), second.GetRequiredService<IDbConnection>());
        Assert.NotSame(first.GetRequiredService<IGameRepository>(), second.GetRequiredService<IGameRepository>());
        Assert.Same(first.GetRequiredService<IDbConnection>(), first.GetRequiredService<IDbCommand>().Connection);
    }

    [Fact]
    public void Two_scopes_see_the_same_data()
    {
        Repo.Sync(1, new[] { Game(10) });

        var other = _db.NewScope().GetRequiredService<IGameRepository>();

        Assert.Single(other.LoadAll(1));
    }

    [Fact]
    public void The_writer_and_a_reader_in_different_scopes_do_not_block_each_other()
    {
        var other = _db.NewScope().GetRequiredService<IGameRepository>();

        Repo.Sync(1, Enumerable.Range(1, 200).Select(i => Game((uint)i)).ToList());
        var read = other.LoadAll(1).Count;
        Repo.Sync(1, Enumerable.Range(1, 100).Select(i => Game((uint)i)).ToList());

        Assert.Equal(200, read);
        Assert.Equal(100, other.LoadAll(1).Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_save_from_a_pool_thread_and_the_load_from_another_do_not_corrupt_the_connection()
    {
        Repo.Sync(1, new[] { Game(1) });
        var token = TestContext.Current.CancellationToken;

        var writes = System.Threading.Tasks.Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                Repo.Sync(1, new[] { Game(1), Game((uint)(2 + i)) }, removeMissing: false);
            }
        }, token);
        var reads = System.Threading.Tasks.Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                Repo.LoadStoreGames("gog", "u1");
                Repo.LoadAll(1);
            }
        }, token);

        await System.Threading.Tasks.Task.WhenAll(writes, reads);
        Assert.Equal(51, Repo.LoadAll(1).Count);
    }

    #endregion
}

public sealed class DatabaseMigrationTests : IDisposable
{
    private TestDatabase _db;

    public void Dispose() => _db?.Dispose();

    private static int Count(IDbConnection connection, string sql)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void Migrating_makes_the_file_its_folder_and_the_tables()
    {
        var path = Path.Combine(Path.GetTempPath(), "sac-test-" + Guid.NewGuid().ToString("N"), "deep", "games.db");
        _db = new TestDatabase(path);

        Assert.True(File.Exists(path));
        var connection = _db.Services.GetRequiredService<IDbConnection>();
        foreach (var table in new[] { "meta", "games", "store_games" })
        {
            Assert.Equal(1, Count(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'"));
        }
        try
        {
            _db.Dispose();
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, true);
        }
        catch (IOException)
        {
        }
        _db = null;
    }

    [Fact]
    public void The_write_ahead_log_is_on()
    {
        _db = new TestDatabase();

        var mode = ScalarText(_db.Services.GetRequiredService<IDbConnection>(), "PRAGMA journal_mode");

        Assert.Equal("wal", mode);
    }

    private static string ScalarText(IDbConnection connection, string sql)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar());
    }

    [Fact]
    public void Migrating_twice_keeps_what_is_saved()
    {
        _db = new TestDatabase();
        _db.Repository.Sync(1, new[] { new CachedGame { Id = 10, Name = "Kept" } });

        _db.Root.MigrateLibraryDatabase();

        Assert.Equal("Kept", _db.Repository.LoadAll(1).Single().Name);
    }

    [Fact]
    public void A_library_table_from_before_accounts_were_recorded_is_rebuilt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sac-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "games.db");
        using (SqliteConnection old = new($"Data Source={path}"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE games (id INTEGER PRIMARY KEY, name TEXT);
                INSERT INTO games VALUES (1, 'old');
                CREATE TABLE store_games (provider TEXT, id TEXT, name TEXT);
                INSERT INTO store_games VALUES ('gog', '1', 'old');
                """;
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        _db = new TestDatabase(path);

        Assert.Empty(_db.Repository.LoadAll(1));
        Assert.Empty(_db.Repository.LoadStoreGames("gog", "any"));
        // And the rebuilt tables take rows under an account.
        _db.Repository.Sync(1, new[] { new CachedGame { Id = 2, Name = "new" } });
        Assert.Single(_db.Repository.LoadAll(1));
        _db.Dispose();
        _db = null;
        Directory.Delete(dir, true);
    }

    [Fact]
    public void A_database_that_cannot_be_prepared_is_recorded_and_the_repository_reads_empty()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "sac-block-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");
        try
        {
            _db = new TestDatabase(Path.Combine(blocker, "games.db"));
            var repo = _db.Repository;

            Assert.False(repo.IsAvailable);
            Assert.False(string.IsNullOrEmpty(repo.Error));
            repo.Sync(1, new[] { new CachedGame { Id = 10, Name = "x" } });
            Assert.Empty(repo.LoadAll(1));
            Assert.Empty(repo.LoadAllGameIds());
            Assert.Empty(repo.LoadStoreGames("gog", "u"));
            Assert.Equal(0u, repo.GetLastSteamAccount());
            repo.SetLastSteamAccount(5);
            repo.SyncStoreGames("gog", "u", new[] { new CachedStoreGame { Id = "1", Key = "k" } });
            repo.SaveStoreProgress("gog", "u", new[] { ("1", 1, 1) });
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void Without_migrating_the_repository_has_no_tables_to_read()
    {
        _db = new TestDatabase(migrate: false);

        Assert.True(_db.Repository.IsAvailable);
        Assert.ThrowsAny<Exception>(() => _db.Repository.LoadAll(1));
    }
}

public sealed class DatabaseRelocatorTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Moving_carries_the_data_and_the_next_scope_finds_it_there()
    {
        _db.Repository.Sync(1, new[] { new CachedGame { Id = 10, Name = "Moved" } });
        var destination = Path.Combine(_db.Directory, "elsewhere", "games.db");

        _db.Services.GetRequiredService<IDatabaseRelocator>().MoveTo(destination);

        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(_db.DatabasePath));
        Assert.Equal(destination, _db.State.Path);
        Assert.Equal("Moved", _db.Repository.LoadAll(1).Single().Name);
        // A scope made afterwards opens the new place.
        Assert.Equal("Moved", _db.NewScope().GetRequiredService<IGameRepository>().LoadAll(1).Single().Name);
    }

    [Fact]
    public void Moving_to_where_it_already_is_changes_nothing()
    {
        _db.Repository.Sync(1, new[] { new CachedGame { Id = 10, Name = "Same" } });

        _db.Services.GetRequiredService<IDatabaseRelocator>().MoveTo(_db.DatabasePath);

        Assert.Equal("Same", _db.Repository.LoadAll(1).Single().Name);
    }

    [Fact]
    public void A_failed_move_leaves_the_database_where_it_was_and_working()
    {
        _db.Repository.Sync(1, new[] { new CachedGame { Id = 10, Name = "Stays" } });
        var blocker = Path.Combine(_db.Directory, "blocker");
        File.WriteAllText(blocker, "x");

        Assert.ThrowsAny<Exception>(() =>
            _db.Services.GetRequiredService<IDatabaseRelocator>().MoveTo(Path.Combine(blocker, "games.db")));

        Assert.Equal(_db.DatabasePath, _db.State.Path);
        Assert.True(File.Exists(_db.DatabasePath));
        Assert.Equal("Stays", _db.Repository.LoadAll(1).Single().Name);
    }
}
