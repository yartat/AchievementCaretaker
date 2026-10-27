/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Data;
using System.IO;

namespace AchievementCaretaker.Services;

/// <summary>
/// The <see cref="IDatabaseMigrator"/>: plain DDL through the scoped connection and command.
/// </summary>
/// <remarks>
/// <para>
/// The library tables are a cache and nothing else is migrated. A database written before
/// accounts were recorded has no way to say whose rows they are, so a library table without an
/// <c>account</c> column is dropped and made again.
/// </para>
/// <para>
/// The write-ahead log (<c>journal_mode=WAL</c>) lets readers overlap the one writer, which
/// they do inside the process: the Steam library saves from a pool thread while the store
/// libraries read. It is kept.
/// </para>
/// </remarks>
public sealed class DatabaseMigrator : IDatabaseMigrator
{
    private readonly IDbConnection _connection;
    private readonly IDbCommand _command;
    private readonly DatabaseState _state;

    /// <summary>Makes the migrator over the scope's connection and command.</summary>
    /// <param name="connection">The connection the tables are made through.</param>
    /// <param name="command">The command the statements are run with.</param>
    /// <param name="state">Where the database is; told why it could not be prepared if it cannot.</param>
    public DatabaseMigrator(IDbConnection connection, IDbCommand command, DatabaseState state)
    {
        _connection = connection;
        _command = command;
        _state = state;
    }

    /// <inheritdoc />
    public void Migrate()
    {
        var directory = Path.GetDirectoryName(_state.Path);
        if (string.IsNullOrEmpty(directory) == false)
        {
            Directory.CreateDirectory(directory);
        }

        lock (_connection)
        {
            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }

            Execute("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS meta (
                    key                TEXT PRIMARY KEY,
                    value              TEXT
                );
                """);

            DropIfWithoutAccount("games");
            DropIfWithoutAccount("store_games");

            Execute("""
                CREATE TABLE IF NOT EXISTS games (
                    account            INTEGER NOT NULL,
                    id                 INTEGER NOT NULL,
                    type               TEXT,
                    name               TEXT,
                    image_url          TEXT,
                    release_date       INTEGER,
                    rating_percent     INTEGER,
                    rating_score       INTEGER,
                    playtime_minutes   INTEGER NOT NULL DEFAULT 0,
                    last_played        INTEGER,
                    ach_total          INTEGER NOT NULL DEFAULT -1,
                    ach_earned         INTEGER NOT NULL DEFAULT -1,
                    has_stats          INTEGER NOT NULL DEFAULT 0,
                    updated_utc        INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (account, id)
                );
                CREATE TABLE IF NOT EXISTS store_games (
                    provider           TEXT NOT NULL,
                    account            TEXT NOT NULL,
                    id                 TEXT NOT NULL,
                    game_key           TEXT NOT NULL,
                    name               TEXT,
                    image_url          TEXT,
                    ach_total          INTEGER NOT NULL DEFAULT -1,
                    ach_earned         INTEGER NOT NULL DEFAULT -1,
                    has_progress       INTEGER NOT NULL DEFAULT 0,
                    updated_utc        INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (provider, account, id)
                );
                """);
        }
    }

    private void DropIfWithoutAccount(string table)
    {
        var columns = Scalar($"SELECT COUNT(*) FROM pragma_table_info('{table}')");
        var hasAccount = Scalar($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = 'account'") > 0;
        if (columns > 0 && hasAccount == false)
        {
            Execute($"DROP TABLE {table}");
        }
    }

    private void Execute(string sql)
    {
        _command.CommandText = sql;
        _command.ExecuteNonQuery();
    }

    private int Scalar(string sql)
    {
        _command.CommandText = sql;
        return Convert.ToInt32(_command.ExecuteScalar());
    }
}
