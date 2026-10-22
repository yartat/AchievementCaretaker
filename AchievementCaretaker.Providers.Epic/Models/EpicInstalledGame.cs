/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * Written from the endpoints that the open-source launchers Legendary
 * (https://github.com/derrod/legendary) and Heroic document for Epic Games' services;
 * see docs/EPIC-RESEARCH.md.
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Epic.Models;

/// <summary>
/// An installed game, read from the Epic Games Launcher's own manifests.
/// </summary>
/// <param name="Namespace">The catalog namespace the game belongs to; one namespace can hold several games.</param>
/// <param name="CatalogItemId">The game's id within its namespace.</param>
/// <param name="AppName">The launcher's internal name for the game.</param>
/// <param name="Title">The name to show.</param>
/// <param name="InstallLocation">The folder the game is installed in.</param>
public sealed record EpicInstalledGame(
    string Namespace,
    string CatalogItemId,
    string AppName,
    string Title,
    string InstallLocation);
