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

using System.Collections.Generic;

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>What the launcher's ownership cache says: whose it is and the ids in it.</summary>
/// <param name="UserId">The user's id, which is the name of the file and of the user's spool folder.</param>
/// <param name="GameIds">
/// Every id the cache mentions, sorted. They are games, but also add-ons and other products, so only those
/// a name is known for are taken to be games.
/// </param>
public sealed record OwnershipCache(string UserId, IReadOnlyList<uint> GameIds);
