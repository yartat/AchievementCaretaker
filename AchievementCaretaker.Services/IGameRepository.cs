/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.Services;

/// <summary>
/// The saved libraries: what the window paints before any network or Steam work has
/// started, and what a refresh writes back.
/// </summary>
/// <remarks>
/// <para>
/// Every library is saved under its account, so signing out or switching account deletes
/// nothing. A Steam library is keyed by the 32-bit Steam account id, a store's by its
/// provider id and the store's own account id.
/// </para>
/// <para>
/// A database that could not be prepared must not stop the application, it only means every
/// start is a cold one. That case is a repository whose <see cref="IsAvailable"/> is false:
/// reads are empty, writes do nothing, and <see cref="Error"/> says why.
/// </para>
/// </remarks>
public interface IGameRepository
{
    /// <summary>Whether the database is there to read and write.</summary>
    bool IsAvailable { get; }

    /// <summary>Why it is not, or null.</summary>
    string? Error { get; }

    /// <summary>The Steam account the library window showed last, or 0 if none yet.</summary>
    uint GetLastSteamAccount();

    /// <summary>Remembers which Steam account the library window is showing.</summary>
    /// <param name="account">The 32-bit Steam account id.</param>
    void SetLastSteamAccount(uint account);

    /// <summary>One Steam account's saved games; empty when the database is not available.</summary>
    /// <param name="account">The 32-bit Steam account id.</param>
    IReadOnlyList<CachedGame> LoadAll(uint account);

    /// <summary>
    /// Every app id any account has cached. What the icon cache must keep: art that belongs
    /// to a game one account owns is not stale just because another does not.
    /// </summary>
    HashSet<uint> LoadAllGameIds();

    /// <summary>
    /// Makes one account's rows match <paramref name="games"/>: new ones are inserted and
    /// existing ones updated. Other accounts' rows are not touched. One transaction, so a
    /// crash mid-sync cannot leave a half-written library behind.
    /// </summary>
    /// <param name="account">The 32-bit Steam account id.</param>
    /// <param name="games">The games the account owns, as the synchronization found them.</param>
    /// <param name="removeMissing">
    /// False adds and updates only. A synchronization that did not finish learned about the
    /// games it reached and nothing about the rest, so it must not delete them.
    /// </param>
    void Sync(uint account, IReadOnlyCollection<CachedGame> games, bool removeMissing = true);

    /// <summary>One account's saved games at one store; empty when the database is not available.</summary>
    /// <param name="provider">The store's id.</param>
    /// <param name="account">The store's own id for the account.</param>
    IReadOnlyList<CachedStoreGame> LoadStoreGames(string provider, string account);

    /// <summary>
    /// Makes one account's rows at one provider match <paramref name="games"/>: games no
    /// longer owned are deleted, new ones inserted, existing ones renamed or re-imaged. The
    /// achievement counts of a game that is still owned are kept: a refreshed list says
    /// nothing about progress, and finding it out again costs requests for every game.
    /// </summary>
    void SyncStoreGames(string provider, string account, IReadOnlyCollection<CachedStoreGame> games);

    /// <summary>Records how far through their achievements the user is, for games whose achievements have just been read.</summary>
    void SaveStoreProgress(string provider, string account, IEnumerable<(string Id, int Earned, int Total)> progress);
}
