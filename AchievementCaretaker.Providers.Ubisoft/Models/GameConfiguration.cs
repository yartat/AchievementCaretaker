/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 * and on galaxy-integration-uplay by melcom (MIT)
 * https://github.com/melcom-creations/galaxy-integration-uplay
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * configuration catalog, the achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>One product in the launcher's configuration catalog: what it is called and what it looks like.</summary>
/// <param name="ProductId">The product's id, which is what the ownership cache lists.</param>
/// <param name="LaunchId">The id the product is launched by; often the same.</param>
/// <param name="Name">
/// The name to show, found through the product's localization table, or null when the block has no usable
/// one (the catalog also holds internal stubs that carry a placeholder).
/// </param>
/// <param name="IsGame">The block says how to start the product, so it is something that can be played, not an add-on.</param>
/// <param name="BackgroundImage">The file name of its wide background (1200 x 700), or null.</param>
/// <param name="ThumbImage">The file name of its small tile art (240 x 280), or null.</param>
/// <param name="AchievementsArchive">
/// The name of the product's achievement archive on Ubisoft's CDN (a hash, with <c>.zip</c> when the catalog
/// says so), or null when the game has none.
/// </param>
/// <param name="AchievementsSyncId">
/// The key Ubisoft's achievements service knows the game's achievements by (<c>ACK</c> for Assassin's Creed
/// Valhalla), which is not its product id; null when the block has none.
/// </param>
public sealed record GameConfiguration(
    uint ProductId,
    uint LaunchId,
    string? Name,
    bool IsGame,
    string? BackgroundImage,
    string? ThumbImage,
    string? AchievementsArchive,
    string? AchievementsSyncId);
