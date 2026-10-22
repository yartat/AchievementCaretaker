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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AchievementCaretaker.Providers.Epic.Models;

namespace AchievementCaretaker.Providers.Epic;

/// <summary>
/// Games installed through the Epic Games Launcher, read from the
/// <c>.item</c> manifests it keeps one per installed app. Needs no sign-in
/// and no network, so it works while the account is signed out.
/// </summary>
public static class EpicManifests
{
    /// <summary>
    /// The launcher only exists on Windows; elsewhere this is null and
    /// <see cref="GetInstalled"/> returns nothing.
    /// </summary>
    public static string ManifestDirectory =>
        OperatingSystem.IsWindows() == true
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests")
            : null;

    /// <summary>
    /// Installed base games. DLC and add-ons get manifests of their own,
    /// carrying <c>MainGameAppName</c>, and are left out.
    /// </summary>
    public static IReadOnlyList<EpicInstalledGame> GetInstalled()
    {
        List<EpicInstalledGame> games = new();
        var directory = ManifestDirectory;
        if (directory == null || Directory.Exists(directory) == false)
        {
            return games;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.item"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (string.IsNullOrEmpty(Get(root, "MainGameAppName")) == false)
                {
                    continue;
                }

                games.Add(new(
                    Get(root, "CatalogNamespace"),
                    Get(root, "CatalogItemId"),
                    Get(root, "AppName"),
                    Get(root, "DisplayName"),
                    Get(root, "InstallLocation")));
            }
            catch (Exception)
            {
                // One unreadable manifest, perhaps mid-write by the
                // launcher, should not hide every other installed game.
            }
        }
        return games;
    }

    private static string Get(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) == true && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
