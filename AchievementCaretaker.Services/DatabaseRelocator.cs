/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AchievementCaretaker.Services;

/// <summary>The <see cref="IDatabaseRelocator"/>.</summary>
/// <remarks>
/// The write-ahead log leaves <c>-wal</c> and <c>-shm</c> files beside the database, and the
/// connection pool keeps them open: the connection is closed and the pools cleared first, or
/// the files stay locked on Windows and cannot be moved.
/// </remarks>
public sealed class DatabaseRelocator : IDatabaseRelocator
{
    private readonly IDbConnection _connection;
    private readonly DatabaseState _state;

    public DatabaseRelocator(IDbConnection connection, DatabaseState state)
    {
        _connection = connection;
        _state = state;
    }

    public void MoveTo(string destination)
    {
        lock (_connection)
        {
            var source = _state.Path;
            _connection.Close();
            SqliteConnection.ClearAllPools();

            try
            {
                if (string.Equals(
                        Path.GetFullPath(source),
                        Path.GetFullPath(destination),
                        StringComparison.OrdinalIgnoreCase) == false)
                {
                    var directory = Path.GetDirectoryName(destination);
                    if (string.IsNullOrEmpty(directory) == false)
                    {
                        Directory.CreateDirectory(directory);
                    }

                    foreach (var suffix in new[] { "", "-wal", "-shm" })
                    {
                        var from = source + suffix;
                        if (File.Exists(from) == true)
                        {
                            File.Move(from, destination + suffix, true);
                        }
                    }
                }

                _connection.ConnectionString = _state.ConnectionStringFor(destination);
                _state.Path = destination;
                // Open it now: a destination that cannot be opened is a failed move, not a surprise later.
                _connection.Open();
            }
            catch
            {
                _state.Path = source;
                _connection.Close();
                _connection.ConnectionString = _state.ConnectionStringFor(source);
                throw;
            }
        }
    }
}
