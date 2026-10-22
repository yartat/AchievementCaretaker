/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.App.Achievements;

/// <summary>
/// One row of the achievement list for an Epic or GOG game.
/// </summary>
/// <remarks>
/// <see cref="IsAchieved"/> can be ticked, but the window only allows it for a store
/// that can write (GOG). The Steam editor's <c>AchievementInfo</c> is the model for
/// the pending state and the column formats.
/// </remarks>
internal sealed partial class StoreAchievementItem : ObservableObject
{
    public StoreAchievement Source { get; }

    /// <summary>Filled in by the icon loader, on the UI thread.</summary>
    [ObservableProperty]
    private Bitmap _icon;

    public StoreAchievementItem(StoreAchievement source)
    {
        Source = source;
        _isAchieved = source.Unlocked;
    }

    public string Name => Source.DisplayName;

    /// <summary>Empty rather than null, so the row's second line collapses on it.</summary>
    public string Description => Source.Description ?? "";

    public string IconUrl => Source.IconUrl;

    /// <summary>What the store reported; <see cref="IsAchieved"/> is what is on screen.</summary>
    public bool OriginalValue => Source.Unlocked;

    /// <summary>The state on screen: the store's, unless the user has ticked it since.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isAchieved;

    /// <summary>True while this row differs from what the store reported.</summary>
    public bool IsChanged => IsAchieved != OriginalValue;

    public bool Hidden => Source.Hidden;

    /// <summary>The store will not let this one be changed (Steam's permission bits).</summary>
    public bool IsProtected => Source.Protected;

    /// <summary>The tick box is live: protected rows are shown but cannot be toggled.</summary>
    public bool IsEditable => Source.Protected == false;

    /// <summary>The old editor painted protected rows dark red; keep that cue.</summary>
    public IBrush RowBackground => Source.Protected == true
        ? new SolidColorBrush(Color.FromArgb(64, 200, 0, 0))
        : Brushes.Transparent;

    /// <summary>
    /// The day it was earned, as in the library and the Steam editor. Blank for a
    /// locked achievement, and <c>—</c> for one the store reports unlocked without
    /// saying when: "unlocked, date unknown" is a different answer from "locked".
    /// </summary>
    public string UnlockDateText =>
        Source.UnlockDate.HasValue == true
            ? Source.UnlockDate.Value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            : Source.Unlocked == true ? "—" : "";

    /// <summary>A pending change reads as an intention: nothing is sent until Commit.</summary>
    public string StateText => IsChanged == true
        ? (IsAchieved == true ? "Will unlock" : "Will lock")
        : (IsAchieved == true ? "Unlocked" : "Locked");

    /// <summary>Full date and time for the detail pane; blank when the store gives none.</summary>
    public string UnlockTimeText =>
        Source.UnlockDate.HasValue == true
            ? Source.UnlockDate.Value.ToLocalTime().ToString(CultureInfo.CurrentCulture)
            : "";

    /// <summary>Blank when the store has no XP, as GOG has none. Not "0 XP".</summary>
    public string XpText =>
        Source.XP.HasValue == true
            ? Source.XP.Value.ToString(CultureInfo.CurrentCulture) + " XP"
            : "";

    /// <summary>The share of players who have it, when the store says.</summary>
    public string RarityText =>
        Source.RarityPercent.HasValue == true
            ? Source.RarityPercent.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%"
            : "";

    public string RarityTooltip => "Share of players who have unlocked this";
}
