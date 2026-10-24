/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;

namespace AchievementCaretaker.Providers.Models;

/// <summary>What checking a list of candidates against an account found.</summary>
/// <param name="Account">The account the answer belongs to, as <see cref="IStoreAccount.AccountId"/> would say it.</param>
/// <param name="Games">The candidates the account owns, with whatever the store knows about each.</param>
/// <param name="Complete">
/// Whether the scan ran to the end under the same client. Anything less may have found games but
/// says nothing about the rest: once the client is gone every remaining answer is "not owned", so a
/// cut-short scan must never be taken as a reason to delete a game.
/// </param>
public sealed record LibraryScan(string Account, IReadOnlyList<StoreGame> Games, bool Complete);
