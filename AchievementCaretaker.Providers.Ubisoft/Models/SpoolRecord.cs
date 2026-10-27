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

using System;

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>One achievement the launcher has recorded as earned on this computer.</summary>
/// <param name="AchievementId">The achievement's id, the same one the game's archive uses.</param>
/// <param name="EarnedAt">When it was earned.</param>
public sealed record SpoolRecord(uint AchievementId, DateTimeOffset EarnedAt);
