/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;

namespace AchievementCaretaker.App.ViewModels;

internal sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _databasePath;

    [ObservableProperty]
    private string _iconCachePath;

    /// <summary>The theme list, in display order. Fixed for the lifetime of the dialog.</summary>
    public ThemeOption[] Themes { get; } =
    [
        new(AppTheme.System),
        new(AppTheme.Light),
        new(AppTheme.Dark),
    ];

    private ThemeOption _selectedTheme;

    /// <summary>
    /// Written by hand rather than generated because the setter also asks
    /// the view to preview the choice; see <see cref="ThemePreviewRequested"/>.
    /// </summary>
    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value == null || ReferenceEquals(_selectedTheme, value) == true)
            {
                return;
            }
            SetProperty(ref _selectedTheme, value);
            ThemePreviewRequested?.Invoke(value.Value);
        }
    }

    /// <summary>
    /// Asks the view to show the theme straight away. A theme is a thing you
    /// judge by looking at it, so the dialog applies it live; the view
    /// restores the previous one if the dialog is cancelled.
    /// </summary>
    public event Action<AppTheme> ThemePreviewRequested;

    /// <summary>Asks the view for a folder; returns null if cancelled.</summary>
    public event Func<string, string, Task<string>> FolderRequested;

    /// <summary>Raised with true when the user accepted the changes.</summary>
    public event Action<bool> CloseRequested;

    public SettingsViewModel(AppSettings settings)
    {
        DatabasePath = settings.DatabasePath;
        IconCachePath = settings.IconCachePath;
        // Assigned to the backing field: selecting the stored theme is not
        // a change the user made, so it must not raise a preview.
        _selectedTheme = Array.Find(Themes, t => t.Value == settings.Theme) ?? Themes[0];
    }

    /// <summary>The database is a file, so browsing picks its directory.</summary>
    [RelayCommand]
    private async Task BrowseDatabaseAsync()
    {
        var current = Path.GetDirectoryName(DatabasePath);
        var picked = await RequestFolderAsync("Choose where to keep the game database", current);
        if (picked == null)
        {
            return;
        }
        var fileName = Path.GetFileName(DatabasePath);
        if (string.IsNullOrWhiteSpace(fileName) == true)
        {
            fileName = "games.db";
        }
        DatabasePath = Path.Combine(picked, fileName);
    }

    [RelayCommand]
    private async Task BrowseIconsAsync()
    {
        var picked = await RequestFolderAsync("Choose where to keep cached game icons", IconCachePath);
        if (picked != null)
        {
            IconCachePath = picked;
        }
    }

    private Task<string> RequestFolderAsync(string title, string start)
    {
        return FolderRequested == null
            ? Task.FromResult<string>(null)
            : FolderRequested(title, start);
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        DatabasePath = AppSettings.DefaultDatabasePath;
        IconCachePath = AppSettings.DefaultIconCachePath;
        SelectedTheme = Themes[0];
    }

    [RelayCommand]
    private void Save()
    {
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    public AppSettings ToSettings()
    {
        return new()
        {
            DatabasePath = DatabasePath?.Trim(),
            IconCachePath = IconCachePath?.Trim(),
            Theme = SelectedTheme?.Value ?? AppTheme.System,
        };
    }
}
