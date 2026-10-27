/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>One achievement as a game's archive defines it, in one language.</summary>
/// <param name="Id">The achievement's id.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Description">What it takes to earn it.</param>
public sealed record ArchiveAchievement(uint Id, string Name, string Description);
