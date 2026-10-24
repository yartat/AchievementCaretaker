/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using Avalonia.Controls;
using AchievementCaretaker.App.ViewModels;

namespace AchievementCaretaker.App.Views;

public partial class StoreAchievementsWindow : Window
{
    public StoreAchievementsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewer != null)
            {
                _viewer.ConfirmRequested -= OnConfirmRequested;
            }
            _viewer = DataContext as StoreAchievementsViewModel;
            if (_viewer != null)
            {
                _viewer.ConfirmRequested += OnConfirmRequested;
            }
        };
    }

    private StoreAchievementsViewModel _viewer;

    private System.Threading.Tasks.Task<bool> OnConfirmRequested(string title, string message)
    {
        return MessageWindow.ShowConfirmAsync(this, title, message);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Stops a load that is still reading achievements or icons.
        (DataContext as StoreAchievementsViewModel)?.Shutdown();
        base.OnClosed(e);
    }
}
