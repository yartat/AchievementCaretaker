/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.App.Achievements
{
    /// <summary>
    /// One row of the read-only achievement list for an Epic or GOG game.
    /// </summary>
    /// <remarks>
    /// Read-only in every sense: nothing here can be ticked, and the type has no
    /// pending state. The Steam editor's <c>AchievementInfo</c> is the model for
    /// the column formats.
    /// </remarks>
    internal sealed partial class StoreAchievementItem : ObservableObject
    {
        public StoreAchievement Source { get; }

        /// <summary>Filled in by the icon loader, on the UI thread.</summary>
        [ObservableProperty]
        private Bitmap _Icon;

        public StoreAchievementItem(StoreAchievement source)
        {
            this.Source = source;
        }

        public string Name => this.Source.DisplayName;

        /// <summary>Empty rather than null, so the row's second line collapses on it.</summary>
        public string Description => this.Source.Description ?? "";

        public string IconUrl => this.Source.IconUrl;

        public bool Unlocked => this.Source.Unlocked;

        public bool Hidden => this.Source.Hidden;

        /// <summary>
        /// The day it was earned, as in the library and the Steam editor. Blank for a
        /// locked achievement, and <c>—</c> for one the store reports unlocked without
        /// saying when: "unlocked, date unknown" is a different answer from "locked".
        /// </summary>
        public string UnlockDateText =>
            this.Source.UnlockDate.HasValue == true
                ? this.Source.UnlockDate.Value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                : this.Source.Unlocked == true ? "—" : "";

        /// <summary>Blank when the store has no XP, as GOG has none. Not "0 XP".</summary>
        public string XpText =>
            this.Source.XP.HasValue == true
                ? this.Source.XP.Value.ToString(CultureInfo.CurrentCulture) + " XP"
                : "";

        /// <summary>The share of players who have it, when the store says.</summary>
        public string RarityText =>
            this.Source.RarityPercent.HasValue == true
                ? this.Source.RarityPercent.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%"
                : "";

        public string RarityTooltip => "Share of players who have unlocked this";
    }
}
