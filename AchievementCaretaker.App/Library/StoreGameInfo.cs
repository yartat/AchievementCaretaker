/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AchievementCaretaker.App.Library
{
    /// <summary>How far through a game's achievements the user is.</summary>
    internal readonly record struct StoreProgress(int Earned, int Total);

    /// <summary>
    /// One game in an Epic or GOG library.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="GameInfo"/>. That class, the cache table behind it,
    /// the rating store and the capsule cache are all keyed by a Steam app id, and a
    /// store's game id is a string. Store games have none of Steam's playtime, last
    /// played, review rating or demo/mod types either. The members the tile and row
    /// templates bind to are named as they are on <see cref="GameInfo"/> so the XAML
    /// can be read side by side.
    /// </remarks>
    internal sealed partial class StoreGameInfo : ObservableObject
    {
        private string _Name;

        public string Provider { get; }

        public string Id { get; }

        /// <summary>What <c>IStoreProvider.GetAchievementsAsync</c> is called with.</summary>
        public string Key { get; }

        public string ImageUrl { get; set; }

        /// <summary>
        /// Filled in asynchronously by the logo loader; the view binds to it
        /// directly, so the assignment must happen on the UI thread.
        /// </summary>
        [ObservableProperty]
        private Bitmap _Logo;

        public string Name
        {
            get => this._Name;
            set => this.SetProperty(ref this._Name, string.IsNullOrWhiteSpace(value) ? this.Id : value);
        }

        /// <summary>
        /// Null means "not read yet", which is deliberately distinct from "none
        /// earned": a store's achievements cost a request per game to find out.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AchievementsText))]
        [NotifyPropertyChangedFor(nameof(Completion))]
        [NotifyPropertyChangedFor(nameof(HasCompletion))]
        [NotifyPropertyChangedFor(nameof(CompletionFraction))]
        [NotifyPropertyChangedFor(nameof(CompletionText))]
        [NotifyPropertyChangedFor(nameof(IsPerfect))]
        private StoreProgress? _Progress;

        /// <summary>0..1, or null when the achievements have not been read or the game has none.</summary>
        public double? Completion
        {
            get
            {
                if (this.Progress.HasValue == false || this.Progress.Value.Total <= 0)
                {
                    return null;
                }
                return Math.Clamp(this.Progress.Value.Earned / (double)this.Progress.Value.Total, 0.0, 1.0);
            }
        }

        public bool HasCompletion => this.Completion.HasValue;

        public double CompletionFraction => this.Completion ?? 0.0;

        public bool IsPerfect => this.Completion.HasValue == true && this.Completion.Value >= 1.0;

        public string CompletionText =>
            this.Completion.HasValue == false
                ? "Not loaded yet"
                : (this.Completion.Value * 100.0).ToString("0", CultureInfo.CurrentCulture) + "%";

        public string AchievementsText
        {
            get
            {
                if (this.Progress.HasValue == false)
                {
                    return "—";
                }
                if (this.Progress.Value.Total == 0)
                {
                    return "none";
                }
                return $"{this.Progress.Value.Earned} / {this.Progress.Value.Total}";
            }
        }

        public StoreGameInfo(string provider, string id, string key)
        {
            this.Provider = provider;
            this.Id = id;
            this.Key = key;
            this.Name = null;
        }
    }
}
