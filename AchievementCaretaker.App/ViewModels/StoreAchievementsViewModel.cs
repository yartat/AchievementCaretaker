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

namespace AchievementCaretaker.App.ViewModels;

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

    private readonly IStoreProvider _provider;
    private readonly IStoreAchievementsService _service;
    private readonly StoreGameInfo _game;
    private readonly StoreImageCache _images;
    private readonly Action<int, int> _progressLoaded;
    private readonly List<StoreAchievementItem> _all = new();
    private readonly CancellationTokenSource _lifetime = new();

    private readonly List<StoreStatItem> _stats = new();

    public ObservableCollection<StoreAchievementItem> Achievements { get; } = new();

    public ObservableCollection<StoreStatItem> Statistics { get; } = new();

    /// <summary>Raised before something that cannot be undone; the window asks. Title, message.</summary>
    public event Func<string, string, Task<bool>>? ConfirmRequested;

    public string GameName => _game.Name;

    public string ProviderName => _provider.DisplayName;

    public string Title => $"{_game.Name} - {_provider.DisplayName} achievements";

    /// <summary>
    /// Whether the checkboxes and the lock / invert / unlock buttons are shown.
    /// Epic's achievements cannot be re-locked, so its provider cannot write and
    /// its window has none.
    /// </summary>
    public bool CanModify => _service.CanModify(_provider);

    /// <summary>Whether the store has statistics beside achievements (only Steam does).</summary>
    public bool HasStatistics => _service.HasStatistics(_provider);

    private bool _showStatistics;

    public bool IsShowingStatistics => _showStatistics;

    public bool IsShowingAchievements => _showStatistics == false;

    [RelayCommand]
    private void ShowTab(string tab)
    {
        _showStatistics = tab == "stats" && HasStatistics == true;
        OnPropertyChanged(nameof(IsShowingStatistics));
        OnPropertyChanged(nameof(IsShowingAchievements));
    }

    [ObservableProperty]
    private string _statsError = "";

    public bool HasStatsError => string.IsNullOrEmpty(StatsError) == false;

    private bool _isCommitting;

    /// <summary>False while a commit is on its way, so nothing is ticked under it.</summary>
    public bool IsInputEnabled => CanModify == true && _isCommitting == false;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _errorText = "";

    [ObservableProperty]
    private StoreAchievementItem? _selectedAchievement;

    private StoreAchievementsState _state = StoreAchievementsState.Loading;
    public StoreAchievementsState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value) == false)
            {
                return;
            }
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsFailed));
        }
    }

    public bool IsLoading => State == StoreAchievementsState.Loading;
    public bool IsReady => State == StoreAchievementsState.Ready;
    public bool IsEmpty => State == StoreAchievementsState.Empty;
    public bool IsFailed => State == StoreAchievementsState.Failed;

    #region Counts and filters

    public int TotalCount => _all.Count;
    public int UnlockedCount => _all.Count(a => a.OriginalValue == true);
    public int LockedCount => TotalCount - UnlockedCount;

    public double CompletionFraction => TotalCount == 0 ? 0.0 : UnlockedCount / (double)TotalCount;

    public string CompletionText =>
        TotalCount == 0
            ? ""
            : $"{UnlockedCount} of {TotalCount} unlocked · " +
              (CompletionFraction * 100.0).ToString("0", CultureInfo.CurrentCulture) + "%";

    /// <summary>
    /// The XP and rarity columns appear only when the store provided them for at
    /// least one achievement. GOG has no XP, and an empty column of blanks would
    /// look like a column of zeros.
    /// </summary>
    public bool HasXp => _all.Any(a => a.Source.XP.HasValue);
    public bool HasRarity => _all.Any(a => a.Source.RarityPercent.HasValue);

    private AchievementFilter _filter = AchievementFilter.All;
    public AchievementFilter Filter
    {
        get => _filter;
        private set
        {
            if (SetProperty(ref _filter, value) == false)
            {
                return;
            }
            OnPropertyChanged(nameof(IsShowingAll));
            OnPropertyChanged(nameof(IsShowingLocked));
            OnPropertyChanged(nameof(IsShowingUnlocked));
            RefreshList();
        }
    }

    public bool IsShowingAll => Filter == AchievementFilter.All;
    public bool IsShowingLocked => Filter == AchievementFilter.Locked;
    public bool IsShowingUnlocked => Filter == AchievementFilter.Unlocked;

    [RelayCommand]
    private void ShowFilter(AchievementFilter filter)
    {
        Filter = filter;
    }

    private string _matchingString = "";
    public string MatchingString
    {
        get => _matchingString;
        set
        {
            if (SetProperty(ref _matchingString, value ?? "") == true)
            {
                RefreshList();
            }
        }
    }

    private void RefreshList()
    {
        var matching = MatchingString.Trim();
        var keep = SelectedAchievement;

        Achievements.Clear();
        foreach (var item in _all)
        {
            var wanted = Filter switch
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
            Achievements.Add(item);
        }

        SelectedAchievement = keep != null && Achievements.Contains(keep) == true ? keep : null;
    }

    private void RefreshCounts()
    {
        foreach (var name in new[]
        {
            nameof(TotalCount), nameof(UnlockedCount), nameof(LockedCount),
            nameof(CompletionFraction), nameof(CompletionText),
            nameof(HasXp), nameof(HasRarity),
        })
        {
            OnPropertyChanged(name);
        }
    }

    #endregion

    #region Pending changes

    private int ChangedStatCount => _stats.Count(s => s.IsModified == true);

    private int ChangedCount => _all.Count(a => a.IsChanged == true) + ChangedStatCount;

    public bool HasPendingChanges => ChangedCount > 0;

    public string PendingSummaryText
    {
        get
        {
            var unlock = _all.Count(a => a.IsChanged == true && a.IsAchieved == true);
            var relock = _all.Count(a => a.IsChanged == true) - unlock;
            List<string> parts = new();
            if (unlock > 0)
            {
                parts.Add($"{unlock} to unlock");
            }
            if (relock > 0)
            {
                parts.Add($"{relock} to lock");
            }
            if (ChangedStatCount > 0)
            {
                parts.Add(ChangedStatCount == 1 ? "1 statistic" : $"{ChangedStatCount} statistics");
            }
            return string.Join(", ", parts);
        }
    }

    public string CommitText => ChangedCount == 1 ? "Commit 1 change" : $"Commit {ChangedCount} changes";

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StoreAchievementItem.IsAchieved) || e.PropertyName == nameof(StoreStatItem.IsModified))
        {
            RefreshPending();
        }
    }

    private void RefreshPending()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PendingSummaryText));
        OnPropertyChanged(nameof(CommitText));
    }

    /// <summary>Sets every achievement on screen, not the whole game.</summary>
    private void SetShown(Func<StoreAchievementItem, bool> wanted)
    {
        foreach (var item in Achievements.ToList())
        {
            if (item.IsProtected == true)
            {
                continue;
            }
            item.IsAchieved = wanted(item);
        }
    }

    [RelayCommand(CanExecute = nameof(IsInputEnabled))]
    private void LockAll() => SetShown(item => false);

    [RelayCommand(CanExecute = nameof(IsInputEnabled))]
    private void UnlockAll() => SetShown(item => true);

    [RelayCommand(CanExecute = nameof(IsInputEnabled))]
    private void InvertAll() => SetShown(item => item.IsAchieved == false);

    [RelayCommand(CanExecute = nameof(IsInputEnabled))]
    private void DiscardChanges()
    {
        foreach (var item in _all)
        {
            item.IsAchieved = item.OriginalValue;
        }
        foreach (var stat in _stats)
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
        if (ConfirmRequested == null || HasStatistics == false)
        {
            return;
        }
        if (await ConfirmRequested("Warning", "Are you absolutely sure you want to reset stats?") == false)
        {
            return;
        }
        var achievementsToo = await ConfirmRequested("Question", "Do you want to reset achievements too?");
        if (await ConfirmRequested("Warning", "Really really sure?") == false)
        {
            return;
        }

        SetCommitting(true);
        BeginProgress("Resetting...");
        string? failure;
        try
        {
            failure = await _service.ResetAsync(_provider, _game.Key, achievementsToo, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            EndProgress();
            SetCommitting(false);
        }

        await ReloadAsync();
        if (State == StoreAchievementsState.Ready)
        {
            StatusText = failure == null ? "Statistics reset." : $"Could not reset: {failure}";
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
        var changes = _all
            .Where(a => a.IsChanged == true)
            .Select(a => new KeyValuePair<string, bool>(a.Source.Id, a.IsAchieved))
            .ToList();
        var statChanges = _stats
            .Where(s => s.IsModified == true)
            .Select(s => new KeyValuePair<string, double>(s.Id, s.Value))
            .ToList();
        if (changes.Count + statChanges.Count == 0)
        {
            return;
        }
        var sent = changes.Count + statChanges.Count;

        SetCommitting(true);
        BeginProgress("Sending changes...");
        StatusText = $"Sending {sent} change(s) to {_provider.DisplayName}...";
        string? failure;
        try
        {
            failure = await _service.CommitAsync(_provider, _game.Key, changes, statChanges, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            EndProgress();
            SetCommitting(false);
        }

        await ReloadAsync();
        if (State == StoreAchievementsState.Ready)
        {
            StatusText = failure == null
                ? $"Sent {sent} change(s). {UnlockedCount} of {TotalCount} unlocked."
                : $"Not everything was sent: {failure}";
        }
    }

    private void SetCommitting(bool value)
    {
        _isCommitting = value;
        OnPropertyChanged(nameof(IsInputEnabled));
        LockAllCommand.NotifyCanExecuteChanged();
        UnlockAllCommand.NotifyCanExecuteChanged();
        InvertAllCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        ResetStatisticsCommand.NotifyCanExecuteChanged();
        StoreCommand.NotifyCanExecuteChanged();
    }

    #endregion

    /// <param name="provider">Where the achievements are read from, and written to when it can.</param>
    /// <param name="service">Reads, commits and resets, and words what went wrong.</param>
    /// <param name="game">The game the window is for.</param>
    /// <param name="images">Reads and keeps the achievements' icons.</param>
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
        _provider = provider;
        _service = service;
        _game = game;
        _images = images;
        _progressLoaded = progressLoaded;

        // Discarded explicitly: the load reports its own failures.
        Task load = ReloadAsync();
        GC.KeepAlive(load);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var token = _lifetime.Token;
        State = StoreAchievementsState.Loading;
        ErrorText = "";
        StatusText = $"Reading {_game.Name} from {_provider.DisplayName}...";
        BeginProgress("Loading achievements...");

        try
        {
            var result = await _service.LoadAsync(_provider, _game.Key, token);
            if (result.Failure != null)
            {
                Fail(result.Failure);
                return;
            }
            var achievements = result.Achievements;

            foreach (var old in _all)
            {
                old.PropertyChanged -= OnItemChanged;
            }
            _all.Clear();
            _all.AddRange(achievements.Select(a => new StoreAchievementItem(a)));
            foreach (var item in _all)
            {
                item.PropertyChanged += OnItemChanged;
            }
            await LoadStatsAsync(token);
            RefreshPending();
            RefreshCounts();
            RefreshList();

            State = _all.Count == 0 && _stats.Count == 0
                ? StoreAchievementsState.Empty
                : StoreAchievementsState.Ready;
            StatusText = _all.Count == 0
                ? $"{_provider.DisplayName} lists no achievements for this game."
                : $"{UnlockedCount} of {TotalCount} unlocked.";
            _progressLoaded?.Invoke(UnlockedCount, TotalCount);

            await LoadIconsAsync(token);
        }
        catch (OperationCanceledException)
        {
            // The window closed while this was running.
        }
        finally
        {
            EndProgress();
        }
    }

    private async Task LoadStatsAsync(CancellationToken token)
    {
        foreach (var old in _stats)
        {
            old.PropertyChanged -= OnItemChanged;
        }
        _stats.Clear();
        Statistics.Clear();
        StatsError = "";
        OnPropertyChanged(nameof(HasStatsError));
        if (HasStatistics == false)
        {
            return;
        }

        var result = await _service.LoadStatsAsync(_provider, _game.Key, token);
        if (result.Failure != null)
        {
            // The achievements are still good; only the statistics tab says what went wrong.
            StatsError = result.Failure;
            OnPropertyChanged(nameof(HasStatsError));
            return;
        }
        foreach (var stat in result.Stats)
        {
            StoreStatItem item = new(stat);
            item.PropertyChanged += OnItemChanged;
            _stats.Add(item);
            Statistics.Add(item);
        }
    }

    private void Fail(string message)
    {
        State = StoreAchievementsState.Failed;
        ErrorText = message;
        StatusText = "Could not read the achievements.";
    }

    private async Task LoadIconsAsync(CancellationToken token)
    {
        Queue<StoreAchievementItem> queue = new(
            _all.Where(item => string.IsNullOrEmpty(item.IconUrl) == false));
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
                item.Icon = await _images.LoadAsync(_provider.Id, item.IconUrl, token);
                done++;
                ReportProgress($"Loading icons, {done:N0} of {total:N0}...", done, total);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, IconWorkers).Select(index => Worker()));
    }

    public void Shutdown()
    {
        _lifetime.Cancel();
    }
}

internal enum StoreAchievementsState
{
    Loading = 0,
    Ready = 1,
    Empty = 2,
    Failed = 3,
}
