/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using Avalonia.Controls;
using AchievementCaretaker.App.ViewModels;

namespace AchievementCaretaker.App.Views
{
    public partial class StoreAchievementsWindow : Window
    {
        public StoreAchievementsWindow()
        {
            this.InitializeComponent();
            this.DataContextChanged += (_, _) =>
            {
                if (this._Viewer != null)
                {
                    this._Viewer.ConfirmRequested -= this.OnConfirmRequested;
                }
                this._Viewer = this.DataContext as StoreAchievementsViewModel;
                if (this._Viewer != null)
                {
                    this._Viewer.ConfirmRequested += this.OnConfirmRequested;
                }
            };
        }

        private StoreAchievementsViewModel _Viewer;

        private System.Threading.Tasks.Task<bool> OnConfirmRequested(string title, string message)
        {
            return MessageWindow.ShowConfirmAsync(this, title, message);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stops a load that is still reading achievements or icons.
            (this.DataContext as StoreAchievementsViewModel)?.Shutdown();
            base.OnClosed(e);
        }
    }
}
