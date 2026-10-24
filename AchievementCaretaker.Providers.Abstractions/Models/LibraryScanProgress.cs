/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>How far through the candidates a scan is.</summary>
/// <param name="Examined">Candidates looked at so far.</param>
/// <param name="Total">Candidates in all.</param>
public sealed record LibraryScanProgress(int Examined, int Total);
