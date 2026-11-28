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
    private static readonly string[] _suffixes = { "", "-wal", "-shm" };

    /// <summary>Makes the relocator over the scope's connection.</summary>
    /// <param name="connection">The connection, closed before the files are moved.</param>
    /// <param name="state">Where the database is; pointed at the new place once it has moved.</param>
    public DatabaseRelocator(IDbConnection connection, DatabaseState state)
    {
        _connection = connection;
        _state = state;
    }

    /// <inheritdoc />
    public void MoveTo(string destination)
    {
        lock (_connection)
        {
            var source = _state.Path;
            var moves = IsSameFile(source, destination) == false;
            if (moves == true)
            {
                // Asked before anything is closed or moved: what is there is not ours to replace.
                foreach (var suffix in _suffixes)
                {
                    var target = destination + suffix;
                    if (File.Exists(target) == true || Directory.Exists(target) == true)
                    {
                        throw new IOException($"'{target}' already exists. Choose a place without a database, or move that file away first.");
                    }
                }
                Checkpoint();
            }

            _connection.Close();
            SqliteConnection.ClearAllPools();

            List<string> moved = new();
            try
            {
                if (moves == true)
                {
                    var directory = Path.GetDirectoryName(destination);
                    if (string.IsNullOrEmpty(directory) == false)
                    {
                        Directory.CreateDirectory(directory);
                    }

                    foreach (var suffix in _suffixes)
                    {
                        if (File.Exists(source + suffix) == true)
                        {
                            File.Move(source + suffix, destination + suffix, false);
                            moved.Add(suffix);
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
                SqliteConnection.ClearAllPools();
                foreach (var suffix in moved)
                {
                    TryMoveBack(destination + suffix, source + suffix);
                }
                _connection.ConnectionString = _state.ConnectionStringFor(source);
                throw;
            }
        }
    }

    // Whether the two names reach one file. The file system is asked, not the operating system:
    // macOS can be formatted to tell cases apart, and Windows can make one folder do it.
    private static bool IsSameFile(string source, string destination)
    {
        var from = Path.GetFullPath(source);
        var to = Path.GetFullPath(destination);
        if (string.Equals(from, to, StringComparison.Ordinal) == true)
        {
            return true;
        }
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) == false || File.Exists(to) == false)
        {
            return false;
        }
        try
        {
            // Both exist under one spelling or they are two files.
            return string.Equals(OnDisk(from), OnDisk(to), StringComparison.OrdinalIgnoreCase) == true &&
                string.Equals(OnDisk(from)[Path.GetPathRoot(from)!.Length..], OnDisk(to)[Path.GetPathRoot(to)!.Length..], StringComparison.Ordinal) == true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed: moving nothing is the safe answer for a file that is there.
            return true;
        }
    }

    // The path as the folders spell it: for each part the entry of exactly that name, else the one it reaches.
    private static string OnDisk(string full)
    {
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string? other = null;
            string? exact = null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, part, StringComparison.Ordinal) == true)
                {
                    exact = name;
                    break;
                }
                if (string.Equals(name, part, StringComparison.OrdinalIgnoreCase) == true)
                {
                    other = name;
                }
            }
            current = Path.Combine(current, exact ?? other ?? part);
        }
        return current;
    }

    // Puts what the write-ahead log holds into the database itself, so the one file is whole.
    private void Checkpoint()
    {
        if (_connection.State != ConnectionState.Open)
        {
            return;
        }
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // The log then moves with the database, as it did before.
        }
    }

    private static void TryMoveBack(string from, string to)
    {
        try
        {
            File.Move(from, to, false);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
    }
}
