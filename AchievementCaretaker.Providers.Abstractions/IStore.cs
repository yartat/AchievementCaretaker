/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers;

/// <summary>
/// What a game store is called. The part <see cref="IStoreProvider"/> (what the store has) and
/// <see cref="IStoreAccount"/> (who is signed in to it) have in common, so that either can be
/// named in a message or a file name without the other.
/// </summary>
public interface IStore
{
    /// <summary>Stable lower-case id: names the session file and the command line.</summary>
    string Id { get; }

    string DisplayName { get; }
}
