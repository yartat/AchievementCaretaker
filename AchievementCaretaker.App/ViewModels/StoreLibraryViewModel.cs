/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.Library;
using AchievementCaretaker.Services;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.App.ViewModels;

/// <summary>
/// Where a long job reports to. The library window's status bar belongs to
/// <see cref="GamePickerViewModel"/>, which counts the jobs running so the bar
/// stays up until the last one finishes; the store libraries feed the same count
/// rather than keeping bars of their own.
/// </summary>
internal interface IProgressSink
{
    void Begin(string text);
    void Report(string text, int completed, int total);
    void End();
}

/// <summary>
/// One store's library in the library window: sign-in, the owned games, and the
/// progress through each game's achievements.
/// </summary>
/// <remarks>
/// <para>
/// A parallel of the Steam half of <see cref="GamePickerViewModel"/>, not an
/// extension of it: the Steam half is keyed throughout by a numeric app id and
/// leans on playtime, last played, review scores and demo/mod types that a store
/// does not have. Only <see cref="GameCollection.All"/>,
/// <see cref="GameCollection.WithAchievements"/> and <see cref="GameCollection.Perfect"/>,
/// and sorting by name or completion, mean anything here.
/// </para>
/// <para>
/// Construction mirrors the Steam window: the cached list is read synchronously so
/// there is something to show at once, and the network comes after. Everything
/// awaits on the UI thread's synchronization context, so the collections need no
/// marshalling; only the disk and decoding work is moved off it.
/// </para>
/// <para>
/// Achievement counts are not part of a library listing and cost a request or
/// more per game, so they are filled in as games are opened and by
/// <see cref="LoadProgressCommand"/>, never automatically.
/// </para>
/// </remarks>
internal sealed partial class StoreLibraryViewModel : ObservableObject
{
    private readonly IStoreProvider _provider;
    private readonly IStoreAccount _account;
    private readonly IStoreLibraryService _library;
    private readonly IStoreSessionService _session;
    private readonly IStoreAchievementsService _achievements;
    // The tile is about 183 wide; twice that keeps it sharp on a high-DPI screen.
    private const int CapsuleDecodeWidth = 480;

    private readonly StoreImageCache _images;
    private readonly IProgressSink _sink;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly Dictionary<string, StoreGameInfo> _games = new();

    private CancellationTokenSource _progressRun;

    public ObservableCollection<StoreGameInfo> FilteredGames { get; } = new();

    public string Id => _provider.Id;

    public string DisplayName => _provider.DisplayName;

    /// <summary>The picker raises its own error dialog from this.</summary>
    public event Action<string> ErrorRaised;

    /// <summary>The window opens the achievement viewer from this.</summary>
    public event Action<StoreAchievementsViewModel> AchievementsRequested;

    /// <summary>
    /// Raised to sign in with the store's own page in a browser inside the
    /// application; the window owns the window. Returns what the page ended on.
    /// </summary>
    public event Func<IStoreAccount, Task<BrowserSignInResult>> BrowserSignInRequested;

    /// <summary>Raised before something the user may not want; the window asks.</summary>
    public event Func<string, string, Task<bool>> ConfirmRequested;

    [ObservableProperty]
    private StoreGameInfo _selectedGame;

    /// <summary>Whether this is the source the rail has selected. Set by the picker.</summary>
    [ObservableProperty]
    private bool _isSelected;

    private bool _isContentView;

    /// <summary>
    /// Tiles or rows. The choice belongs to the picker, which pushes it to every
    /// source so switching source does not switch view.
    /// </summary>
    public bool IsContentView
    {
        get => _isContentView;
        set
        {
            if (SetProperty(ref _isContentView, value) == true)
            {
                OnPropertyChanged(nameof(IsTileView));
            }
        }
    }

    public bool IsTileView => _isContentView == false;

    [ObservableProperty]
    private string _statusText = "";

    /// <param name="provider">What the store has: its library and achievements.</param>
    /// <param name="account">Who is signed in to it. The same store as <paramref name="provider"/>.</param>
    public StoreLibraryViewModel(
        IStoreProvider provider,
        IStoreAccount account,
        IStoreLibraryService library,
        IStoreSessionService session,
        IStoreAchievementsService achievements,
        StoreImageCache images,
        IProgressSink sink)
    {
        if (provider.Id != account.Id)
        {
            throw new ArgumentException(
                $"The provider is '{provider.Id}' but the account is '{account.Id}': they must be the same store.",
                nameof(account));
        }

        _achievements = achievements;
        _provider = provider;
        _account = account;
        _library = library;
        _session = session;
        _images = images;
        _sink = sink;

        LoadFromCache();

        Task initialLoad = RefreshLibraryAsync();
        GC.KeepAlive(initialLoad);
    }

    #region Sign-in

    public string SignInTitle => $"Sign in to {DisplayName}";

    public string SignInIntro => $"Sign in with your {DisplayName} account. The sign-in page opens in a window of Caretaker.";

    /// <summary>What <see cref="PastedCode"/> is for, from the account.</summary>
    public string SignInPrompt => $"Sign in with your {DisplayName} account in the browser, then paste {_account.SignInHint}.";

    public bool IsSignedIn => _account.IsSignedIn;

    public bool IsSignedOut => _account.IsSignedIn == false;

    /// <summary>The rail's account line: who is signed in, or the way to sign in.</summary>
    public string RailAccountText =>
        IsSignedIn == true
            ? _account.AccountName ?? "Signed in"
            : "Sign in";

    public string AccountText =>
        _account.AccountName == null
            ? $"Signed in to {DisplayName}"
            : $"{DisplayName} · {_account.AccountName}";

    [ObservableProperty]
    private string _pastedCode = "";

    /// <summary>Shown inline in the sign-in panel; a sign-in problem is not worth a dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    public bool HasError => string.IsNullOrEmpty(ErrorText) == false;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value) == true)
            {
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(CanLoadProgress));
            }
        }
    }

    private void NotifySignInChanged()
    {
        foreach (var name in new[]
        {
            nameof(IsSignedIn), nameof(IsSignedOut), nameof(AccountText),
            nameof(RailAccountText), nameof(CanRefresh), nameof(CanLoadProgress),
        })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void OpenSignInPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_account.SignInUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            ErrorText = "Could not open the browser. Open this address yourself:\n" + _account.SignInUrl;
        }
    }

    /// <summary>The fallback: the user signed in elsewhere and pasted the result.</summary>
    [RelayCommand]
    private Task SignInAsync()
    {
        return CompleteSignInAsync(PastedCode);
    }

    /// <summary>Signs in with the store's own page, in a browser inside the application.</summary>
    [RelayCommand]
    private Task SignInWithBrowserAsync()
    {
        return BrowserSignInAsync();
    }

    /// <summary>
    /// Signs in as a different account. The current one stays until the new sign-in
    /// has succeeded, so backing out of the page costs nothing.
    /// </summary>
    [RelayCommand]
    private async Task SwitchAccountAsync()
    {
        if (IsBusy == true)
        {
            return;
        }
        var switching = _session.ConfirmSwitchAccount(_account);
        if (await ConfirmAsync(switching.Title, switching.Message) == false)
        {
            return;
        }
        await BrowserSignInAsync();
    }

    private async Task BrowserSignInAsync()
    {
        if (IsBusy == true || BrowserSignInRequested == null)
        {
            return;
        }

        ErrorText = "";
        var result = await BrowserSignInRequested(_account);
        if (result.Pasted != null)
        {
            await CompleteSignInAsync(result.Pasted);
        }
        else if (result.Unavailable == true)
        {
            if (IsSignedIn == true)
            {
                ErrorRaised?.Invoke(
                    "The browser built into Caretaker could not start. Sign out, then sign in with your own browser.");
                return;
            }
            OpenSignInPage();
            ErrorText = $"The built-in browser could not start, so your own browser was opened. Paste {_account.SignInHint} below.";
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        return ConfirmRequested == null || await ConfirmRequested(title, message);
    }

    private async Task CompleteSignInAsync(string pasted)
    {
        if (IsBusy == true)
        {
            return;
        }
        IsBusy = true;
        ErrorText = "";
        var signedIn = false;
        try
        {
            var result = await _session.SignInAsync(_account, pasted, _lifetime.Token);
            if (result.SignedIn == false)
            {
                ErrorText = result.Error;
            }
            else
            {
                PastedCode = "";

                // Whatever is on screen belongs to the previous account, if there was one.
                // The new account's saved library, if it has one, goes up straight away.
                CancelProgress();
                ClearLibrary();
                LoadFromCache();
                NotifySignInChanged();
                signedIn = true;
                await RefreshLibraryCoreAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsBusy = false;
        }

        // A new sign-in reads the achievement counts without being asked: it is the one
        // moment the user has just chosen this account, and it only covers games whose
        // count is not already saved, so signing back in to a known account is cheap.
        // Not after a failed library load, which would only report "nothing to read".
        if (signedIn == true && _games.Count > 0)
        {
            Task progress = LoadProgressAsync();
            GC.KeepAlive(progress);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var signingOut = _session.ConfirmSignOut(_account);
        if (await ConfirmAsync(signingOut.Title, signingOut.Message) == false)
        {
            return;
        }

        ForgetAccount();
        StatusText = $"Signed out of {DisplayName}.";
    }

    /// <summary>Called when a request finds the session no longer valid.</summary>
    private void OnSessionExpired()
    {
        ForgetAccount();
        ErrorText = _session.SessionExpiredMessage(_account);
        StatusText = "Signed out.";
    }

    /// <summary>
    /// Drops the session and stops showing the account's library. What is saved for
    /// the account stays in the database under its account id, so signing back in
    /// finds it; it is never shown to another account, which looks under its own id.
    /// A dead session is forgotten too, or the sign-in panel would never come back.
    /// </summary>
    private void ForgetAccount()
    {
        CancelProgress();
        _session.SignOut(_account);
        ClearLibrary();
    }

    /// <summary>Stops showing the account's library. Neither the session nor the saved rows are touched.</summary>
    private void ClearLibrary()
    {
        _games.Clear();
        FilteredGames.Clear();
        SelectedGame = null;

        RefreshCollectionCounts();
        ErrorText = "";
        NotifySignInChanged();
    }

    #endregion

    #region Library

    public bool CanRefresh => IsSignedIn == true && IsBusy == false && IsLoadingProgress == false;

    private void LoadFromCache()
    {
        foreach (var row in _library.LoadCached(_account))
        {
            StoreGameInfo info = new(Id, row.Id, row.Key)
            {
                Name = row.Name,
                ImageUrl = row.ImageUrl,
            };
            if (row.HasProgress == true && row.AchievementsTotal >= 0 && row.AchievementsEarned >= 0)
            {
                info.Progress = new StoreProgress(row.AchievementsEarned, row.AchievementsTotal);
            }
            _games[row.Id] = info;
        }

        if (_games.Count > 0)
        {
            RefreshCollectionCounts();
            RefreshGames();
            StatusText = $"Showing {_games.Count} cached games. Refreshing...";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy == true || IsSignedIn == false)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await RefreshLibraryCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshLibraryAsync()
    {
        if (IsSignedIn == false)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await RefreshLibraryCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-reads the owned list and merges it into what is already on screen. A
    /// failure leaves the cached list in place and says why: a store being down
    /// is not a reason to blank the window.
    /// </summary>
    private async Task RefreshLibraryCoreAsync()
    {
        ErrorText = "";
        StatusText = $"Loading your {DisplayName} library...";
        _sink.Begin($"Loading your {DisplayName} library...");
        try
        {
            var refresh = await _library.RefreshAsync(_provider, _lifetime.Token);
            if (refresh.SessionExpired == true)
            {
                OnSessionExpired();
            }
            else if (refresh.Failure != null)
            {
                StatusText = refresh.Failure;
            }
            else
            {
                Merge(refresh.Library);
                RefreshCollectionCounts();
                RefreshGames();
                await SaveLibraryAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _sink.End();
        }
    }

    /// <summary>
    /// Updates the games already known rather than rebuilding, so the decoded
    /// capsules and the progress already loaded survive a refresh.
    /// </summary>
    private void Merge(IReadOnlyList<StoreGame> library)
    {
        HashSet<string> owned = new();
        foreach (var game in library)
        {
            owned.Add(game.Id);

            if (_games.TryGetValue(game.Id, out var info) == true && info.Key == game.Key)
            {
                info.Name = game.Title;
                if (info.ImageUrl != game.ImageUrl)
                {
                    info.ImageUrl = game.ImageUrl;
                    info.LogoLoader = null;
                }
                continue;
            }

            _games[game.Id] = new(Id, game.Id, game.Key)
            {
                Name = game.Title,
                ImageUrl = game.ImageUrl,
            };
        }

        foreach (var gone in _games.Keys.Where(id => owned.Contains(id) == false).ToList())
        {
            _games.Remove(gone);
        }
    }

    private async Task SaveLibraryAsync()
    {
        var rows = _games.Values.Select(info => new CachedStoreGame()
        {
            Id = info.Id,
            Key = info.Key,
            Name = info.Name,
            ImageUrl = info.ImageUrl,
        }).ToList();

        var failure = await _library.SaveLibraryAsync(_account, rows);
        if (failure != null)
        {
            ErrorRaised?.Invoke(failure);
        }
    }

    #endregion

    #region Searching, collections and sorting

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? "") == true)
            {
                RefreshGames();
            }
        }
    }

    private GameCollection _selectedCollection = GameCollection.All;
    public GameCollection SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (SetProperty(ref _selectedCollection, value) == false)
            {
                return;
            }
            foreach (var name in new[]
            {
                nameof(CollectionTitle), nameof(IsAllSelected),
                nameof(IsWithAchievementsSelected), nameof(IsPerfectSelected),
            })
            {
                OnPropertyChanged(name);
            }
            RefreshGames();
        }
    }

    public bool IsAllSelected => SelectedCollection == GameCollection.All;
    public bool IsWithAchievementsSelected => SelectedCollection == GameCollection.WithAchievements;
    public bool IsPerfectSelected => SelectedCollection == GameCollection.Perfect;

    public string CollectionTitle => SelectedCollection switch
    {
        GameCollection.WithAchievements => "Has achievements",
        GameCollection.Perfect => "Perfect games",
        _ => "All games",
    };

    [RelayCommand]
    private void SelectCollection(GameCollection collection)
    {
        SelectedCollection = collection;
    }

    public int AllCount => _games.Count;
    public int WithAchievementsCount => _games.Values.Count(g => g.HasCompletion == true);
    public int PerfectCount => _games.Values.Count(g => g.IsPerfect == true);

    /// <summary>
    /// Earned and total across the games whose achievements have been read, which is
    /// not the whole library until <see cref="LoadProgressCommand"/> has run; the
    /// number of games it covers is said so the totals are not mistaken for all.
    /// </summary>
    public string LibrarySummaryText
    {
        get
        {
            var earned = 0;
            var total = 0;
            var covered = 0;
            foreach (var info in _games.Values)
            {
                if (info.Progress.HasValue == false || info.Progress.Value.Total <= 0)
                {
                    continue;
                }
                earned += info.Progress.Value.Earned;
                total += info.Progress.Value.Total;
                covered++;
            }
            return covered == 0
                ? $"{_games.Count:N0} owned"
                : $"{_games.Count:N0} owned · {earned:N0} of {total:N0} achievements in {covered:N0} games read";
        }
    }

    private void RefreshCollectionCounts()
    {
        foreach (var name in new[]
        {
            nameof(AllCount), nameof(WithAchievementsCount),
            nameof(PerfectCount), nameof(LibrarySummaryText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private GameSortField _sortField = GameSortField.Name;
    public GameSortField SortField
    {
        get => _sortField;
        set
        {
            if (SetProperty(ref _sortField, value) == true)
            {
                OnPropertyChanged(nameof(SortDescriptionText));
                RefreshGames();
            }
        }
    }

    private bool _sortDescending;
    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (SetProperty(ref _sortDescending, value) == true)
            {
                OnPropertyChanged(nameof(SortDescriptionText));
                RefreshGames();
            }
        }
    }

    public string SortDescriptionText => SortField == GameSortField.Completion ? "Completion" : "Name";

    [RelayCommand]
    private void SortBy(GameSortField field)
    {
        if (SortField == field)
        {
            SortDescending = SortDescending == false;
            return;
        }
        // Completion is far more useful highest-first.
        _sortDescending = field != GameSortField.Name;
        OnPropertyChanged(nameof(SortDescending));
        SortField = field;
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDescending = SortDescending == false;
    }

    private IEnumerable<StoreGameInfo> ApplySort(IEnumerable<StoreGameInfo> games)
    {
        // Name is the tie-breaker, so the order is stable when many games have no
        // completion yet; those sort to the far end either way rather than as 0%.
        return SortField == GameSortField.Completion
            ? SortDescending == true
                ? games.OrderByDescending(g => g.Completion ?? -1.0).ThenBy(g => g.Name)
                : games.OrderBy(g => g.Completion ?? 2.0).ThenBy(g => g.Name)
            : SortDescending == true
                ? games.OrderByDescending(g => g.Name)
                : games.OrderBy(g => g.Name);
    }

    private void RefreshGames()
    {
        var nameSearch = SearchText.Length > 0 ? SearchText : null;
        var keep = SelectedGame;

        FilteredGames.Clear();
        foreach (var info in ApplySort(_games.Values))
        {
            if (nameSearch != null &&
                info.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            var inCollection = SelectedCollection switch
            {
                GameCollection.WithAchievements => info.HasCompletion,
                GameCollection.Perfect => info.IsPerfect,
                _ => true,
            };
            if (inCollection == false)
            {
                continue;
            }
            FilteredGames.Add(info);
        }

        StatusText = $"Displaying {FilteredGames.Count} games. Total {_games.Count} games.";

        // Keep the selection across a refresh; losing it every time progress lands
        // would be maddening.
        SelectedGame = keep != null && FilteredGames.Contains(keep) == true
            ? keep
            : FilteredGames.FirstOrDefault();

        foreach (var info in FilteredGames)
        {
            // Read when the tile comes into view, not here.
            info.LogoLoader ??= token => _images.LoadAsync(Id, info.ImageUrl, token, CapsuleDecodeWidth);
        }
    }

    #endregion

    #region Achievements

    [RelayCommand]
    private void OpenSelected()
    {
        var game = SelectedGame;
        if (game == null)
        {
            return;
        }

        AchievementsRequested?.Invoke(new StoreAchievementsViewModel(
            _provider,
            _achievements,
            game,
            _images,
            (earned, total) => ApplyProgress(game, earned, total)));
    }

    /// <summary>
    /// Records what a game's achievements came to, from the viewer or from
    /// <see cref="LoadProgressCommand"/>, so the meter fills in without another trip
    /// to the store.
    /// </summary>
    private void ApplyProgress(StoreGameInfo game, int earned, int total)
    {
        game.Progress = new StoreProgress(earned, total);
        RefreshCollectionCounts();
        SaveProgress(new[] { (game.Id, earned, total) });
    }

    private void SaveProgress(IReadOnlyCollection<(string Id, int Earned, int Total)> results)
    {
        // Fire and forget on purpose: the numbers are already on screen, and a cache
        // that cannot be written costs only a reload next time.
        _ = Task.Run(() => _library.SaveProgress(_account, results));
    }

    private bool _isLoadingProgress;
    public bool IsLoadingProgress
    {
        get => _isLoadingProgress;
        private set
        {
            if (SetProperty(ref _isLoadingProgress, value) == true)
            {
                OnPropertyChanged(nameof(CanLoadProgress));
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    public bool CanLoadProgress => IsSignedIn == true && IsBusy == false && IsLoadingProgress == false;

    /// <summary>
    /// Reads the achievements of every game whose progress is not known yet, a few at
    /// a time, and keeps the results. It costs requests for every one of them, so it
    /// runs only when asked, can be cancelled, and keeps what it has when it is.
    /// </summary>
    [RelayCommand]
    private async Task LoadProgressAsync()
    {
        if (CanLoadProgress == false)
        {
            return;
        }

        var games = _games.Values.Where(g => g.Progress.HasValue == false).OrderBy(g => g.Name).ToList();
        var total = games.Count;
        if (total == 0)
        {
            StatusText = "Progress is already loaded for every game.";
            return;
        }

        _progressRun = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _progressRun.Token;
        IsLoadingProgress = true;
        ErrorText = "";

        _sink.Begin($"Loading {DisplayName} progress...");
        _sink.Report($"Loading {DisplayName} progress, 0 of {total:N0}...", 0, total);

        StoreProgressRun result = null;
        try
        {
            result = await _library.LoadProgressAsync(
                _provider,
                _account,
                games.Select(g => (g.Id, g.Key)).ToList(),
                (id, earned, count) =>
                {
                    if (_games.TryGetValue(id, out var game) == true)
                    {
                        game.Progress = new StoreProgress(earned, count);
                    }
                },
                finished => _sink.Report(
                    $"Loading {DisplayName} progress, {finished:N0} of {total:N0}...", finished, total),
                token);
        }
        finally
        {
            _sink.End();
            _progressRun.Dispose();
            _progressRun = null;
            IsLoadingProgress = false;

            RefreshCollectionCounts();
            RefreshGames();
        }

        if (result.SessionExpired == true)
        {
            OnSessionExpired();
            return;
        }

        var summary = result.Cancelled
            ? $"Stopped after {result.Done:N0} of {total:N0} games."
            : $"Read the achievements of {result.Done:N0} games.";
        if (result.Failed > 0)
        {
            summary += $" {result.Failed:N0} could not be read; run it again to retry them.";
        }
        StatusText = summary;
    }

    [RelayCommand]
    private void CancelProgress()
    {
        _progressRun?.Cancel();
    }

    #endregion

    public void Shutdown()
    {
        _lifetime.Cancel();
        // The provider belongs to the container, which disposes it.
    }
}

/// <summary>
/// What the in-application sign-in page ended with: the text for the provider, or
/// nothing because the user closed it, or <see cref="Unavailable"/> because the
/// browser could not start and the user should be offered their own.
/// </summary>
internal readonly record struct BrowserSignInResult(string Pasted, bool Unavailable);
