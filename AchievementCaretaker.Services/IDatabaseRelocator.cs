/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services;

/// <summary>Moves the library database to another place, for the Settings dialog.</summary>
public interface IDatabaseRelocator
{
    /// <summary>
    /// Closes the database, moves it (with its write-ahead log files) to
    /// <paramref name="destination"/>, and points every connection made from now on there. If
    /// the move fails the database stays where it was and the failure is thrown.
    /// </summary>
    void MoveTo(string destination);
}
