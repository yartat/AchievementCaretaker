/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services;

/// <summary>Brings the library database to the shape the repository expects. Run once, at start.</summary>
public interface IDatabaseMigrator
{
    /// <summary>
    /// Creates the folder and the file if they are not there, turns on the write-ahead log, and
    /// makes the tables. A database that cannot be prepared throws.
    /// </summary>
    void Migrate();
}
