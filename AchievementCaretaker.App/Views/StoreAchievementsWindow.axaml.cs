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
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stops a load that is still reading achievements or icons.
            (this.DataContext as StoreAchievementsViewModel)?.Shutdown();
            base.OnClosed(e);
        }
    }
}
