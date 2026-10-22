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
using AchievementCaretaker.Services;

namespace AchievementCaretaker.App.ViewModels
{
    /// <summary>
    /// One Epic or GOG game's achievements. Read-only for Epic; for GOG they can be
    /// ticked and sent back, as in the Steam editor.
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
        private readonly IStoreAchievementsService _Service;
        private readonly StoreGameInfo _Game;
        private readonly StoreImageCache _Images;
        private readonly Action<int, int> _ProgressLoaded;
        private readonly List<StoreAchievementItem> _All = new();
        private readonly CancellationTokenSource _Lifetime = new();

        private readonly List<StoreStatItem> _Stats = new();

        public ObservableCollection<StoreAchievementItem> Achievements { get; } = new();

        public ObservableCollection<StoreStatItem> Statistics { get; } = new();

        /// <summary>Raised before something that cannot be undone; the window asks. Title, message.</summary>
        public event Func<string, string, Task<bool>> ConfirmRequested;

        public string GameName => this._Game.Name;

        public string ProviderName => this._Provider.DisplayName;

        public string Title => $"{this._Game.Name} - {this._Provider.DisplayName} achievements";

        /// <summary>
        /// Whether the checkboxes and the lock / invert / unlock buttons are shown.
        /// Epic's achievements cannot be re-locked, so its provider cannot write and
        /// its window has none.
        /// </summary>
        public bool CanModify => this._Service.CanModify(this._Provider);

        /// <summary>Whether the store has statistics beside achievements (only Steam does).</summary>
        public bool HasStatistics => this._Service.HasStatistics(this._Provider);

        private bool _ShowStatistics;

        public bool IsShowingStatistics => this._ShowStatistics;

        public bool IsShowingAchievements => this._ShowStatistics == false;

        [RelayCommand]
        private void ShowTab(string tab)
        {
            this._ShowStatistics = tab == "stats" && this.HasStatistics == true;
            this.OnPropertyChanged(nameof(this.IsShowingStatistics));
            this.OnPropertyChanged(nameof(this.IsShowingAchievements));
        }

        [ObservableProperty]
        private string _StatsError = "";

        public bool HasStatsError => string.IsNullOrEmpty(this.StatsError) == false;

        private bool _IsCommitting;

        /// <summary>False while a commit is on its way, so nothing is ticked under it.</summary>
        public bool IsInputEnabled => this.CanModify == true && this._IsCommitting == false;

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
        public int UnlockedCount => this._All.Count(a => a.OriginalValue == true);
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
                    AchievementFilter.Locked => item.IsAchieved == false,
                    AchievementFilter.Unlocked => item.IsAchieved == true,
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

        #region Pending changes

        private int ChangedStatCount => this._Stats.Count(s => s.IsModified == true);

        private int ChangedCount => this._All.Count(a => a.IsChanged == true) + this.ChangedStatCount;

        public bool HasPendingChanges => this.ChangedCount > 0;

        public string PendingSummaryText
        {
            get
            {
                var unlock = this._All.Count(a => a.IsChanged == true && a.IsAchieved == true);
                var relock = this._All.Count(a => a.IsChanged == true) - unlock;
                List<string> parts = new();
                if (unlock > 0)
                {
                    parts.Add($"{unlock} to unlock");
                }
                if (relock > 0)
                {
                    parts.Add($"{relock} to lock");
                }
                if (this.ChangedStatCount > 0)
                {
                    parts.Add(this.ChangedStatCount == 1 ? "1 statistic" : $"{this.ChangedStatCount} statistics");
                }
                return string.Join(", ", parts);
            }
        }

        public string CommitText => this.ChangedCount == 1 ? "Commit 1 change" : $"Commit {this.ChangedCount} changes";

        private void OnItemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StoreAchievementItem.IsAchieved) || e.PropertyName == nameof(StoreStatItem.IsModified))
            {
                this.RefreshPending();
            }
        }

        private void RefreshPending()
        {
            this.OnPropertyChanged(nameof(this.HasPendingChanges));
            this.OnPropertyChanged(nameof(this.PendingSummaryText));
            this.OnPropertyChanged(nameof(this.CommitText));
        }

        /// <summary>Sets every achievement on screen, not the whole game.</summary>
        private void SetShown(Func<StoreAchievementItem, bool> wanted)
        {
            foreach (var item in this.Achievements.ToList())
            {
                if (item.IsProtected == true)
                {
                    continue;
                }
                item.IsAchieved = wanted(item);
            }
        }

        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private void LockAll() => this.SetShown(item => false);

        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private void UnlockAll() => this.SetShown(item => true);

        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private void InvertAll() => this.SetShown(item => item.IsAchieved == false);

        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private void DiscardChanges()
        {
            foreach (var item in this._All)
            {
                item.IsAchieved = item.OriginalValue;
            }
            foreach (var stat in this._Stats)
            {
                stat.Discard();
            }
        }

        /// <summary>
        /// Resets every statistic, and the achievements too if the user says so. Asks three
        /// times, as the old editor did: it cannot be undone.
        /// </summary>
        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private async Task ResetStatisticsAsync()
        {
            if (this.ConfirmRequested == null || this.HasStatistics == false)
            {
                return;
            }
            if (await this.ConfirmRequested("Warning", "Are you absolutely sure you want to reset stats?") == false)
            {
                return;
            }
            var achievementsToo = await this.ConfirmRequested("Question", "Do you want to reset achievements too?");
            if (await this.ConfirmRequested("Warning", "Really really sure?") == false)
            {
                return;
            }

            this.SetCommitting(true);
            this.BeginProgress("Resetting...");
            string failure;
            try
            {
                failure = await this._Service.ResetAsync(this._Provider, this._Game.Key, achievementsToo, this._Lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                this.EndProgress();
                this.SetCommitting(false);
            }

            await this.ReloadAsync();
            if (this.State == StoreAchievementsState.Ready)
            {
                this.StatusText = failure == null ? "Statistics reset." : $"Could not reset: {failure}";
            }
        }

        /// <summary>
        /// Sends every pending change to the store, then reads the achievements again
        /// whatever happened: a commit that failed half-way has still changed some, and
        /// the store is the only source of truth for which.
        /// </summary>
        [RelayCommand(CanExecute = nameof(IsInputEnabled))]
        private async Task StoreAsync()
        {
            var changes = this._All
                .Where(a => a.IsChanged == true)
                .Select(a => new KeyValuePair<string, bool>(a.Source.Id, a.IsAchieved))
                .ToList();
            var statChanges = this._Stats
                .Where(s => s.IsModified == true)
                .Select(s => new KeyValuePair<string, double>(s.Id, s.Value))
                .ToList();
            if (changes.Count + statChanges.Count == 0)
            {
                return;
            }
            var sent = changes.Count + statChanges.Count;

            this.SetCommitting(true);
            this.BeginProgress("Sending changes...");
            this.StatusText = $"Sending {sent} change(s) to {this._Provider.DisplayName}...";
            string failure;
            try
            {
                failure = await this._Service.CommitAsync(this._Provider, this._Game.Key, changes, statChanges, this._Lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                this.EndProgress();
                this.SetCommitting(false);
            }

            await this.ReloadAsync();
            if (this.State == StoreAchievementsState.Ready)
            {
                this.StatusText = failure == null
                    ? $"Sent {sent} change(s). {this.UnlockedCount} of {this.TotalCount} unlocked."
                    : $"Not everything was sent: {failure}";
            }
        }

        private void SetCommitting(bool value)
        {
            this._IsCommitting = value;
            this.OnPropertyChanged(nameof(this.IsInputEnabled));
            this.LockAllCommand.NotifyCanExecuteChanged();
            this.UnlockAllCommand.NotifyCanExecuteChanged();
            this.InvertAllCommand.NotifyCanExecuteChanged();
            this.DiscardChangesCommand.NotifyCanExecuteChanged();
            this.ResetStatisticsCommand.NotifyCanExecuteChanged();
            this.StoreCommand.NotifyCanExecuteChanged();
        }

        #endregion

        /// <param name="progressLoaded">
        /// Told the earned and total counts once they are known, so the library's meter
        /// for this game can fill in without a separate trip to the store.
        /// </param>
        public StoreAchievementsViewModel(
            IStoreProvider provider,
            IStoreAchievementsService service,
            StoreGameInfo game,
            StoreImageCache images,
            Action<int, int> progressLoaded)
        {
            this._Provider = provider;
            this._Service = service;
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
                var result = await this._Service.LoadAsync(this._Provider, this._Game.Key, token);
                if (result.Failure != null)
                {
                    this.Fail(result.Failure);
                    return;
                }
                var achievements = result.Achievements;

                foreach (var old in this._All)
                {
                    old.PropertyChanged -= this.OnItemChanged;
                }
                this._All.Clear();
                this._All.AddRange(achievements.Select(a => new StoreAchievementItem(a)));
                foreach (var item in this._All)
                {
                    item.PropertyChanged += this.OnItemChanged;
                }
                await this.LoadStatsAsync(token);
                this.RefreshPending();
                this.RefreshCounts();
                this.RefreshList();

                this.State = this._All.Count == 0 && this._Stats.Count == 0
                    ? StoreAchievementsState.Empty
                    : StoreAchievementsState.Ready;
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
            finally
            {
                this.EndProgress();
            }
        }

        private async Task LoadStatsAsync(CancellationToken token)
        {
            foreach (var old in this._Stats)
            {
                old.PropertyChanged -= this.OnItemChanged;
            }
            this._Stats.Clear();
            this.Statistics.Clear();
            this.StatsError = "";
            this.OnPropertyChanged(nameof(this.HasStatsError));
            if (this.HasStatistics == false)
            {
                return;
            }

            var result = await this._Service.LoadStatsAsync(this._Provider, this._Game.Key, token);
            if (result.Failure != null)
            {
                // The achievements are still good; only the statistics tab says what went wrong.
                this.StatsError = result.Failure;
                this.OnPropertyChanged(nameof(this.HasStatsError));
                return;
            }
            foreach (var stat in result.Stats)
            {
                StoreStatItem item = new(stat);
                item.PropertyChanged += this.OnItemChanged;
                this._Stats.Add(item);
                this.Statistics.Add(item);
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
