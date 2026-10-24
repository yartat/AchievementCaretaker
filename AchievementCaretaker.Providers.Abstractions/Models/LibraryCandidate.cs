/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>A game the store might own, to be checked: it is not in a list of what is owned, it is in a list of what exists.</summary>
/// <param name="Id">The game's identifier within the store.</param>
/// <param name="Type">What kind of app the published list says it is ("normal", "demo", "mod", "junk").</param>
public sealed record LibraryCandidate(string Id, string Type);
