/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using Microsoft.Data.Sqlite;

namespace AchievementCaretaker.Services;

/// <summary>
/// Where the library database is, and whether it could be prepared. One per process: the
/// scoped connections are made from it, so a database that has been moved is found at its
/// new place by every connection made afterwards.
/// </summary>
public sealed class DatabaseState
{
    /// <summary>Makes the state for a database at a path; nothing is opened yet.</summary>
    /// <param name="path">The database file.</param>
    public DatabaseState(string path)
    {
        Path = path;
    }

    /// <summary>The database file.</summary>
    public string Path { get; internal set; }

    /// <summary>Why the database could not be prepared at start, or null. A database that could not is not used.</summary>
    public string? Error { get; internal set; }

    /// <summary>Whether the database was prepared and can be read and written.</summary>
    public bool IsAvailable => Error == null;

    /// <summary>The connection string for <see cref="Path"/>; the file is created if it is not there.</summary>
    public string ConnectionString => ConnectionStringFor(Path);

    internal string ConnectionStringFor(string path) => new SqliteConnectionStringBuilder()
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();
}
