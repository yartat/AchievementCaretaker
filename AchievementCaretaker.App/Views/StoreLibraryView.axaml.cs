/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AchievementCaretaker.App.ViewModels;

namespace AchievementCaretaker.App.Views
{
    public partial class StoreLibraryView : UserControl
    {
        public StoreLibraryView()
        {
            this.InitializeComponent();
        }

        private void OnGameDoubleTapped(object sender, TappedEventArgs e)
        {
            (this.DataContext as StoreLibraryViewModel)?.OpenSelectedCommand.Execute(null);
        }

        private void OnGameListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            (this.DataContext as StoreLibraryViewModel)?.OpenSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
