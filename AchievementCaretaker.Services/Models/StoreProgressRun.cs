/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services.Models;

/// <summary>How a progress run ended.</summary>
/// <param name="Done">Games whose achievements were read.</param>
/// <param name="Failed">Games that could not be read (the run goes on past them).</param>
/// <param name="SessionExpired">The run stopped because the store refused the session.</param>
/// <param name="Cancelled">The run stopped because it was cancelled.</param>
public sealed record StoreProgressRun(int Done, int Failed, bool SessionExpired, bool Cancelled);
