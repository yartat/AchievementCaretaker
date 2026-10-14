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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.ViewModels;

namespace AchievementCaretaker.App.Views
{
    public partial class SettingsWindow : Window
    {
        private SettingsViewModel _ViewModel;

        /// <summary>
        /// What the application looked like when the dialog opened, so a
        /// cancelled preview can be undone. Captured from the application
        /// rather than from the settings file: the two can differ if a previous
        /// save failed to write.
        /// </summary>
        private readonly ThemeVariant _OriginalTheme;

        public SettingsWindow()
        {
            this._OriginalTheme = Application.Current?.RequestedThemeVariant;
            this.InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            this.Detach();

            this._ViewModel = this.DataContext as SettingsViewModel;
            if (this._ViewModel != null)
            {
                this._ViewModel.FolderRequested += this.OnFolderRequested;
                this._ViewModel.CloseRequested += this.OnCloseRequested;
                this._ViewModel.ThemePreviewRequested += this.OnThemePreviewRequested;
            }

            base.OnDataContextChanged(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            this.Detach();
            base.OnClosed(e);
        }

        private void Detach()
        {
            if (this._ViewModel == null)
            {
                return;
            }
            this._ViewModel.FolderRequested -= this.OnFolderRequested;
            this._ViewModel.CloseRequested -= this.OnCloseRequested;
            this._ViewModel.ThemePreviewRequested -= this.OnThemePreviewRequested;
        }

        private async Task<string> OnFolderRequested(string title, string startIn)
        {
            IStorageFolder start = null;
            try
            {
                if (string.IsNullOrWhiteSpace(startIn) == false && Directory.Exists(startIn) == true)
                {
                    start = await this.StorageProvider.TryGetFolderFromPathAsync(startIn);
                }
            }
            catch (Exception)
            {
            }

            var picked = await this.StorageProvider.OpenFolderPickerAsync(new()
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });

            return picked.Count == 0 ? null : picked[0].Path.LocalPath;
        }

        private void OnThemePreviewRequested(AppTheme theme)
        {
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = AppThemes.ToVariant(theme);
            }
        }

        private void OnCloseRequested(bool accepted)
        {
            // Saving is the picker's job; all this has to undo is the preview.
            if (accepted == false && Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = this._OriginalTheme;
            }
            this.Close(accepted);
        }
    }
}
