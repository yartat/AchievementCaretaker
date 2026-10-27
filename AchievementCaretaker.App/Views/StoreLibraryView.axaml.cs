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

namespace AchievementCaretaker.App.Views;

/// <summary>One store's games: its sign-in panel, or its tiles and rows.</summary>
public partial class StoreLibraryView : UserControl
{
    /// <summary>Makes the view; its view model arrives as the data context.</summary>
    public StoreLibraryView()
    {
        InitializeComponent();
    }

    private void OnGameDoubleTapped(object sender, TappedEventArgs e)
    {
        (DataContext as StoreLibraryViewModel)?.OpenSelectedCommand.Execute(null);
    }

    private void OnGameListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        (DataContext as StoreLibraryViewModel)?.OpenSelectedCommand.Execute(null);
        e.Handled = true;
    }
}
