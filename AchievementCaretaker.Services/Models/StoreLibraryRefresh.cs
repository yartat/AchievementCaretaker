/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Services.Models;

/// <summary>What re-reading a store's owned list came to.</summary>
/// <param name="Library">The owned list, or null when the read did not work.</param>
/// <param name="SessionExpired">The store refused the session: the account must sign in again.</param>
/// <param name="Failure">A sentence for the user when the read did not work for another reason.</param>
public sealed record StoreLibraryRefresh(IReadOnlyList<StoreGame>? Library, bool SessionExpired, string? Failure);
