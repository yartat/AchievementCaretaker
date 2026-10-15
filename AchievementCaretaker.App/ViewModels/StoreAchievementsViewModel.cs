/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Achievements;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.Library;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.App.ViewModels
{
    /// <summary>
    /// A read-only view of one Epic or GOG game's achievements.
    /// </summary>
    /// <remarks>
    /// Runs in the library's own process. Steam's achievement editor cannot: its
    /// interface is scoped to the one app id a process initialised with, which is
    /// why that window is a second copy of the executable. A store's service is
    /// plain HTTPS with no such scope, so nothing of that applies here.
    /// <para>
    /// Everything awaits on the UI thread's synchronization context, like the rest of
    /// the application's downloads, so the collections need no marshalling.
    /// </para>
    /// </remarks>
    internal sealed partial class StoreAchievementsViewModel : ProgressViewModel
    {
        private const int IconWorkers = 4;

        private readonly IStoreProvider _Provider;
        private readonly StoreGameInfo _Game;
        private readonly StoreImageCache _Images;
        private readonly Action<int, int> _ProgressLoaded;
        private readonly List<StoreAchievementItem> _All = new();
        private readonly CancellationTokenSource _Lifetime = new();

        public ObservableCollection<StoreAchievementItem> Achievements { get; } = new();

        public string GameName => this._Game.Name;

        public string ProviderName => this._Provider.DisplayName;

        public string Title => $"{this._Game.Name} - {this._Provider.DisplayName} achievements";

        /// <summary>
        /// Whether the lock / invert / unlock buttons are shown. GOG's achievements can
        /// be changed; Epic's cannot be re-locked, so its window has none.
        /// </summary>
        public bool CanModify => this._Provider.Id == "gog";

        [ObservableProperty]
        private string _StatusText = "";

        [ObservableProperty]
        private string _ErrorText = "";

        [ObservableProperty]
        private StoreAchievementItem _SelectedAchievement;

        private StoreAchievementsState _State = StoreAchievementsState.Loading;
        public StoreAchievementsState State
        {
            get => this._State;
            private set
            {
                if (this.SetProperty(ref this._State, value) == false)
                {
                    return;
                }
                this.OnPropertyChanged(nameof(this.IsLoading));
                this.OnPropertyChanged(nameof(this.IsReady));
                this.OnPropertyChanged(nameof(this.IsEmpty));
                this.OnPropertyChanged(nameof(this.IsFailed));
            }
        }

        public bool IsLoading => this.State == StoreAchievementsState.Loading;
        public bool IsReady => this.State == StoreAchievementsState.Ready;
        public bool IsEmpty => this.State == StoreAchievementsState.Empty;
        public bool IsFailed => this.State == StoreAchievementsState.Failed;

        #region Counts and filters

        public int TotalCount => this._All.Count;
        public int UnlockedCount => this._All.Count(a => a.Unlocked == true);
        public int LockedCount => this.TotalCount - this.UnlockedCount;

        public double CompletionFraction => this.TotalCount == 0 ? 0.0 : this.UnlockedCount / (double)this.TotalCount;

        public string CompletionText =>
            this.TotalCount == 0
                ? ""
                : $"{this.UnlockedCount} of {this.TotalCount} unlocked · " +
                  (this.CompletionFraction * 100.0).ToString("0", CultureInfo.CurrentCulture) + "%";

        /// <summary>
        /// The XP and rarity columns appear only when the store provided them for at
        /// least one achievement. GOG has no XP, and an empty column of blanks would
        /// look like a column of zeros.
        /// </summary>
        public bool HasXp => this._All.Any(a => a.Source.XP.HasValue);
        public bool HasRarity => this._All.Any(a => a.Source.RarityPercent.HasValue);

        private AchievementFilter _Filter = AchievementFilter.All;
        public AchievementFilter Filter
        {
            get => this._Filter;
            private set
            {
                if (this.SetProperty(ref this._Filter, value) == false)
                {
                    return;
                }
                this.OnPropertyChanged(nameof(this.IsShowingAll));
                this.OnPropertyChanged(nameof(this.IsShowingLocked));
                this.OnPropertyChanged(nameof(this.IsShowingUnlocked));
                this.RefreshList();
            }
        }

        public bool IsShowingAll => this.Filter == AchievementFilter.All;
        public bool IsShowingLocked => this.Filter == AchievementFilter.Locked;
        public bool IsShowingUnlocked => this.Filter == AchievementFilter.Unlocked;

        [RelayCommand]
        private void ShowFilter(AchievementFilter filter)
        {
            this.Filter = filter;
        }

        private string _MatchingString = "";
        public string MatchingString
        {
            get => this._MatchingString;
            set
            {
                if (this.SetProperty(ref this._MatchingString, value ?? "") == true)
                {
                    this.RefreshList();
                }
            }
        }

        private void RefreshList()
        {
            var matching = this.MatchingString.Trim();
            var keep = this.SelectedAchievement;

            this.Achievements.Clear();
            foreach (var item in this._All)
            {
                var wanted = this.Filter switch
                {
                    AchievementFilter.Locked => item.Unlocked == false,
                    AchievementFilter.Unlocked => item.Unlocked == true,
                    _ => true,
                };
                if (wanted == false)
                {
                    continue;
                }

                if (matching.Length > 0 &&
                    item.Name.Contains(matching, StringComparison.OrdinalIgnoreCase) == false &&
                    item.Description.Contains(matching, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }
                this.Achievements.Add(item);
            }

            this.SelectedAchievement = keep != null && this.Achievements.Contains(keep) == true ? keep : null;
        }

        private void RefreshCounts()
        {
            foreach (var name in new[]
            {
                nameof(this.TotalCount), nameof(this.UnlockedCount), nameof(this.LockedCount),
                nameof(this.CompletionFraction), nameof(this.CompletionText),
                nameof(this.HasXp), nameof(this.HasRarity),
            })
            {
                this.OnPropertyChanged(name);
            }
        }

        #endregion

        /// <param name="progressLoaded">
        /// Told the earned and total counts once they are known, so the library's meter
        /// for this game can fill in without a separate trip to the store.
        /// </param>
        public StoreAchievementsViewModel(
            IStoreProvider provider,
            StoreGameInfo game,
            StoreImageCache images,
            Action<int, int> progressLoaded)
        {
            this._Provider = provider;
            this._Game = game;
            this._Images = images;
            this._ProgressLoaded = progressLoaded;

            // Discarded explicitly: the load reports its own failures.
            Task load = this.ReloadAsync();
            GC.KeepAlive(load);
        }

        [RelayCommand]
        private async Task ReloadAsync()
        {
            var token = this._Lifetime.Token;
            this.State = StoreAchievementsState.Loading;
            this.ErrorText = "";
            this.StatusText = $"Reading {this._Game.Name} from {this._Provider.DisplayName}...";
            this.BeginProgress("Loading achievements...");

            try
            {
                var achievements = await this._Provider.GetAchievementsAsync(this._Game.Key, "en", token);

                this._All.Clear();
                this._All.AddRange(achievements.Select(a => new StoreAchievementItem(a)));
                this.RefreshCounts();
                this.RefreshList();

                this.State = this._All.Count == 0 ? StoreAchievementsState.Empty : StoreAchievementsState.Ready;
                this.StatusText = this._All.Count == 0
                    ? $"{this._Provider.DisplayName} lists no achievements for this game."
                    : $"{this.UnlockedCount} of {this.TotalCount} unlocked.";
                this._ProgressLoaded?.Invoke(this.UnlockedCount, this.TotalCount);

                await this.LoadIconsAsync(token);
            }
            catch (OperationCanceledException)
            {
                // The window closed while this was running.
            }
            catch (ProviderException e)
            {
                this.Fail(e.ErrorCode == "login_required"
                    ? $"You are signed out of {this._Provider.DisplayName}. Sign in again from the library."
                    : e.Message);
            }
            catch (Exception e)
            {
                this.Fail($"Could not reach {this._Provider.DisplayName}: {e.Message}");
            }
            finally
            {
                this.EndProgress();
            }
        }

        private void Fail(string message)
        {
            this.State = StoreAchievementsState.Failed;
            this.ErrorText = message;
            this.StatusText = "Could not read the achievements.";
        }

        private async Task LoadIconsAsync(CancellationToken token)
        {
            Queue<StoreAchievementItem> queue = new(
                this._All.Where(item => string.IsNullOrEmpty(item.IconUrl) == false));
            var total = queue.Count;
            var done = 0;
            if (total == 0)
            {
                return;
            }

            async Task Worker()
            {
                while (queue.TryDequeue(out var item) == true)
                {
                    item.Icon = await this._Images.LoadAsync(this._Provider.Id, item.IconUrl, token);
                    done++;
                    this.ReportProgress($"Loading icons, {done:N0} of {total:N0}...", done, total);
                }
            }

            await Task.WhenAll(Enumerable.Range(0, IconWorkers).Select(index => Worker()));
        }

        public void Shutdown()
        {
            this._Lifetime.Cancel();
        }
    }

    internal enum StoreAchievementsState
    {
        Loading = 0,
        Ready = 1,
        Empty = 2,
        Failed = 3,
    }
}
