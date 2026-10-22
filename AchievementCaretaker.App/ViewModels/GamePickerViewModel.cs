/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.Library;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Steam;
using AchievementCaretaker.Services;
using static AchievementCaretaker.App.InvariantShorthand;
using APITypes = AchievementCaretaker.SteamApi.Types;
using AchievementCaretaker.Providers.Steam.Models;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.App.ViewModels;

internal sealed partial class GamePickerViewModel : ProgressViewModel
{
    private readonly IHttpClientFactory _http;

    // Steam is looked for in the background and may not be there at all, so the
    // client, its callback and the timer that pumps it only exist once it has been
    // reached. Everything that uses them checks _steamClient first.
    private readonly SteamConnector _steam;
    private SteamApi.Client _steamClient;
    private SteamApi.Callbacks.AppDataChanged _appDataChangedCallback;
    private DispatcherTimer _callbackTimer;

    // Bumped each time Steam is connected or lost. A refresh remembers the number it
    // started under, and throws away what it found if it has changed: on a dead pipe
    // the ownership sweep sees nothing owned, and merging that would empty the library
    // and then the cache.
    private int _steamGeneration;

    /// <summary>
    /// The Steam account (32-bit id) whose library is on screen and is saved to, or 0
    /// before any Steam has been seen. The cache holds a library per account; this is
    /// which one is showing. Without Steam it is the account shown last time.
    /// </summary>
    private uint _steamAccount;

    private readonly Dictionary<uint, GameInfo> _games = new();
    private readonly HashSet<string> _logosAttempted = new();
    private readonly ConcurrentQueue<GameInfo> _logoQueue = new();

    // How many have been queued and finished since the queue last drained,
    // so the bar has a denominator. UI thread only: EnqueueLogo is called
    // from it, and the worker's continuations return to it.
    private int _logosQueued;
    private int _logosCompleted;
    private readonly SemaphoreSlim _logoSignal = new(0);

    /// <summary>
    /// How many capsules are read, decoded or downloaded at once. A warm start is hundreds
    /// of small files, and one at a time is nowhere near what a disk or a connection can do.
    /// </summary>
    private const int LogoWorkers = 8;

    public ObservableCollection<GameInfo> FilteredGames { get; } = new();

    [ObservableProperty]
    private GameInfo _selectedGame;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _addGameText = "";

    // Off until Steam has been reached: there is nothing to refresh from before that.
    [ObservableProperty]
    private bool _canRefresh;

    private LibraryStats _libraryStats;

    private readonly AppSettings _settings;
    private readonly IGameRepository _cache;
    private readonly IDatabaseRelocator _relocator;
    private readonly IStoreAchievementsService _storeAchievements;
    private readonly IStoreLibraryService _storeLibrary;
    private readonly IStoreSessionService _storeSession;
    private readonly SteamProvider _steamProvider;
    private IconCache _iconCache;

    private bool _isContentView;
    /// <summary>
    /// False shows the tile grid (large capsule + name), true shows one row
    /// per game with playtime and achievement counts.
    /// </summary>
    public bool IsContentView
    {
        get => _isContentView;
        set
        {
            if (SetProperty(ref _isContentView, value) == true)
            {
                OnPropertyChanged(nameof(IsTileView));
                OnViewModeChanged();
            }
        }
    }

    public bool IsTileView
    {
        get => _isContentView == false;
        set => IsContentView = value == false;
    }

    [RelayCommand]
    private void ShowTiles()
    {
        IsContentView = false;
    }

    [RelayCommand]
    private void ShowContent()
    {
        IsContentView = true;
    }

    #region Sorting

    private readonly RatingStore _ratingStore = RatingStore.Load();

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

    public string SortDescriptionText => SortField switch
    {
        GameSortField.ReleaseDate => "Released",
        GameSortField.LastPlayed => "Last played",
        GameSortField.SteamRating => "Steam rating",
        GameSortField.OwnRating => "My rating",
        GameSortField.Completion => "Completion",
        _ => "Name",
    };

    private GameCollection _selectedCollection = GameCollection.All;

    /// <summary>
    /// Which of the rail's collections is showing. Unlike the type toggles
    /// below, these are mutually exclusive.
    /// </summary>
    public GameCollection SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (SetProperty(ref _selectedCollection, value) == false)
            {
                return;
            }
            OnPropertyChanged(nameof(CollectionTitle));
            foreach (var name in _collectionSelectionNames)
            {
                OnPropertyChanged(name);
            }
            RefreshGames();
        }
    }

    private static readonly string[] _collectionSelectionNames =
    {
        nameof(IsAllSelected), nameof(IsRecentSelected), nameof(IsWithAchievementsSelected),
        nameof(IsPerfectSelected), nameof(IsLikedSelected),
    };

    public bool IsAllSelected => SelectedCollection == GameCollection.All;
    public bool IsRecentSelected => SelectedCollection == GameCollection.RecentlyPlayed;
    public bool IsWithAchievementsSelected => SelectedCollection == GameCollection.WithAchievements;
    public bool IsPerfectSelected => SelectedCollection == GameCollection.Perfect;
    public bool IsLikedSelected => SelectedCollection == GameCollection.Liked;

    public string CollectionTitle => SelectedCollection switch
    {
        GameCollection.RecentlyPlayed => "Recently played",
        GameCollection.WithAchievements => "Has achievements",
        GameCollection.Perfect => "Perfect games",
        GameCollection.Liked => "Liked",
        _ => "All games",
    };

    [RelayCommand]
    private void SelectCollection(GameCollection collection)
    {
        SelectedCollection = collection;
    }

    /// <summary>
    /// The rail's counts. Deliberately computed over the whole library
    /// rather than the filtered view, so a count never changes just because
    /// a different collection is open.
    /// </summary>
    public int AllCount => _games.Count;

    public int RecentCount => _games.Values.Count(g => g.LastPlayed.HasValue == true);

    public int WithAchievementsCount => _games.Values.Count(g => g.HasCompletion == true);

    public int PerfectCount => _games.Values.Count(g => g.IsPerfect == true);

    public int LikedCount => _games.Values.Count(g => g.IsLiked == true);

    /// <summary>Earned and total across everything Steam has cached.</summary>
    public string LibrarySummaryText
    {
        get
        {
            var earned = 0;
            var total = 0;
            foreach (var info in _games.Values)
            {
                if (info.Stats.HasValue == false || info.Stats.Value.AchievementsTotal <= 0)
                {
                    continue;
                }
                earned += info.Stats.Value.AchievementsEarned;
                total += info.Stats.Value.AchievementsTotal;
            }
            return total == 0
                ? $"{_games.Count:N0} owned"
                : $"{_games.Count:N0} owned · {earned:N0} of {total:N0} achievements";
        }
    }

    private static readonly string[] _collectionCountNames =
    {
        nameof(AllCount), nameof(RecentCount), nameof(WithAchievementsCount),
        nameof(PerfectCount), nameof(LikedCount), nameof(LibrarySummaryText),
    };

    /// <summary>Call after anything that changes the library or its stats.</summary>
    private void RefreshCollectionCounts()
    {
        foreach (var name in _collectionCountNames)
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// Clicking the field already being sorted by flips the direction,
    /// which is what a list header is expected to do.
    /// </summary>
    [RelayCommand]
    private void SortBy(GameSortField field)
    {
        if (SortField == field)
        {
            SortDescending = SortDescending == false;
            return;
        }
        // Dates and ratings are far more useful highest-first.
        _sortDescending = field != GameSortField.Name;
        OnPropertyChanged(nameof(SortDescending));
        SortField = field;
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDescending = SortDescending == false;
    }

    private IEnumerable<GameInfo> ApplySort(IEnumerable<GameInfo> games)
    {
        // Name is always the tie-breaker so the order is stable and
        // predictable when a key is missing for many games.
        return SortField switch
        {
            GameSortField.ReleaseDate => SortDescending == true
                ? games.OrderByDescending(g => g.ReleaseDate ?? DateTime.MinValue).ThenBy(g => g.Name)
                : games.OrderBy(g => g.ReleaseDate ?? DateTime.MaxValue).ThenBy(g => g.Name),
            GameSortField.LastPlayed => SortDescending == true
                ? games.OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue).ThenBy(g => g.Name)
                : games.OrderBy(g => g.LastPlayed ?? DateTime.MaxValue).ThenBy(g => g.Name),
            GameSortField.SteamRating => SortDescending == true
                ? games.OrderByDescending(g => g.SteamRatingPercent ?? -1).ThenBy(g => g.Name)
                : games.OrderBy(g => g.SteamRatingPercent ?? int.MaxValue).ThenBy(g => g.Name),
            GameSortField.OwnRating => SortDescending == true
                ? games.OrderByDescending(g => RatingOrder(g.OwnRating)).ThenBy(g => g.Name)
                : games.OrderBy(g => RatingOrder(g.OwnRating)).ThenBy(g => g.Name),
            // Games Steam has never cached sort to the far end either way
            // rather than pretending to be 0%.
            GameSortField.Completion => SortDescending == true
                ? games.OrderByDescending(g => g.Completion ?? -1.0).ThenBy(g => g.Name)
                : games.OrderBy(g => g.Completion ?? 2.0).ThenBy(g => g.Name),
            _ => SortDescending == true
                ? games.OrderByDescending(g => g.Name)
                : games.OrderBy(g => g.Name),
        };
    }

    private static int RatingOrder(OwnRating rating) => rating switch
    {
        OwnRating.Like => 2,
        OwnRating.None => 1,
        OwnRating.Dislike => 0,
        _ => 1,
    };

    #endregion

    #region Settings

    /// <summary>
    /// Asks the view to show the settings dialog; returns the accepted
    /// settings, or null if the user cancelled.
    /// </summary>
    public event Func<AppSettings, Task<AppSettings>> SettingsRequested;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (SettingsRequested == null)
        {
            return;
        }

        var updated = await SettingsRequested(_settings.Clone());
        if (updated == null)
        {
            return;
        }

        await ApplySettingsAsync(updated);
    }

    /// <summary>
    /// Applies new cache locations, moving the existing data across, and
    /// stores the chosen theme. The database is closed before it is moved
    /// because SQLite keeps its WAL sidecars open.
    /// </summary>
    private async Task ApplySettingsAsync(AppSettings updated)
    {
        var databaseMoved = string.Equals(
            updated.DatabasePath, _settings.DatabasePath, StringComparison.OrdinalIgnoreCase) == false;
        var iconsMoved = string.Equals(
            updated.IconCachePath, _settings.IconCachePath, StringComparison.OrdinalIgnoreCase) == false;
        var themeChanged = updated.Theme != _settings.Theme;

        if (databaseMoved == false && iconsMoved == false && themeChanged == false)
        {
            return;
        }

        // The settings dialog already put the theme on screen; all that is
        // left to do here is write it to disk. It does not depend on the
        // cache move, so a failed move must not leave it unsaved.
        var locationsApplied = true;
        try
        {
            if (databaseMoved == true)
            {
                var destination = updated.DatabasePath;
                await Task.Run(() => _relocator.MoveTo(destination));
            }

            if (iconsMoved == true)
            {
                var icons = _iconCache;
                var destination = updated.IconCachePath;
                await Task.Run(() => icons.MoveTo(destination));
            }
        }
        catch (Exception e)
        {
            ErrorRaised?.Invoke(
                "Could not move the cache, so it was left where it was:\n" + e.Message);
            locationsApplied = false;
        }

        if (locationsApplied == true)
        {
            _settings.DatabasePath = updated.DatabasePath;
            _settings.IconCachePath = updated.IconCachePath;
        }
        _settings.Theme = updated.Theme;

        if (_settings.Save() == false)
        {
            ErrorRaised?.Invoke("The new settings are in use for this session but could not be saved.");
        }
    }

    #endregion

    #region Own rating

    [RelayCommand]
    private void ToggleLike(GameInfo info)
    {
        SetOwnRating(info, info?.OwnRating == OwnRating.Like ? OwnRating.None : OwnRating.Like);
    }

    [RelayCommand]
    private void ToggleDislike(GameInfo info)
    {
        SetOwnRating(info, info?.OwnRating == OwnRating.Dislike ? OwnRating.None : OwnRating.Dislike);
    }

    private void SetOwnRating(GameInfo info, OwnRating rating)
    {
        if (info == null)
        {
            return;
        }

        info.OwnRating = rating;

        if (_ratingStore.Set(info.Id, rating) == false)
        {
            ErrorRaised?.Invoke(
                "Your rating was applied for this session but could not be saved to disk.");
        }

        RefreshCollectionCounts();

        if (SortField == GameSortField.OwnRating ||
            SelectedCollection == GameCollection.Liked)
        {
            RefreshGames();
        }
    }

    #endregion

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) == true)
            {
                RefreshGames();
                ForwardSearchToStores(value);
            }
        }
    }

    private bool _showGames = true;
    public bool ShowGames
    {
        get => _showGames;
        set => SetFilter(ref _showGames, value);
    }

    private bool _showDemos;
    public bool ShowDemos
    {
        get => _showDemos;
        set => SetFilter(ref _showDemos, value);
    }

    private bool _showMods;
    public bool ShowMods
    {
        get => _showMods;
        set => SetFilter(ref _showMods, value);
    }

    private bool _showJunk;
    public bool ShowJunk
    {
        get => _showJunk;
        set => SetFilter(ref _showJunk, value);
    }

    private void SetFilter(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
    {
        if (SetProperty(ref field, value, name) == true)
        {
            RefreshGames();
        }
    }

    /// <summary>
    /// Raised when something needs to be shown to the user; the view owns
    /// the actual dialog because the view model has no window to parent to.
    /// </summary>
    public event Action<string> ErrorRaised;

    public GamePickerViewModel(SteamConnector steam, IEnumerable<IStoreProvider> storeProviders, IGameRepository cache,
        IDatabaseRelocator relocator,
        IStoreAchievementsService storeAchievements, IStoreLibraryService storeLibrary,
        IStoreSessionService storeSession,
        SteamProvider steamProvider,
        IHttpClientFactory http)
    {
        _steam = steam;
        _storeProviders = storeProviders;
        _cache = cache;
        _relocator = relocator;
        _storeAchievements = storeAchievements;
        _storeLibrary = storeLibrary;
        _storeSession = storeSession;
        _steamProvider = steamProvider;
        _http = http;

        _settings = AppSettings.Load();
        _iconCache = new(_settings.IconCachePath);
        try
        {
            _steamAccount = _cache.GetLastSteamAccount();
        }
        catch (Exception)
        {
            _steamAccount = 0;
        }

        // Cached rows are loaded synchronously so the window has content
        // the moment it is shown; everything that touches the network or
        // Steam happens afterwards, in the background.
        LoadFromCache();

        // Discards are spelled out here because `using static
        // InvariantShorthand` puts a method named `_` in scope.
        Task[] logoWorkers = Enumerable.Range(0, LogoWorkers).Select(index => RunLogoWorkerAsync()).ToArray();
        Task icons = HydrateCachedIconsAsync();
        GC.KeepAlive((logoWorkers, icons));

        CreateStoreLibraries();

        // Last, and without waiting: Steam may not be running. The window is
        // already usable, and the Steam library refreshes from the moment the
        // connector reaches Steam, if it ever does.
        steam.Changed += OnSteamChanged;
        steam.Start();
    }

    private void OnSteamChanged()
    {
        // The connector's client is the truth. A different one, or none, means the one
        // held here is gone; a new one is a new connection.
        var client = _steam.Client;
        if (_steamClient != null && ReferenceEquals(client, _steamClient) == false)
        {
            DisconnectSteam();
        }
        if (client != null && _steamClient == null)
        {
            ConnectSteam();
        }
        NotifySteamState();
    }

    /// <summary>
    /// Steam has gone. Stop talking to it and keep what is on screen: the cached
    /// games are still the best there is, and they are refreshed if Steam returns.
    /// </summary>
    private void DisconnectSteam()
    {
        _steamGeneration++;

        _callbackTimer?.Stop();
        _callbackTimer = null;
        if (_appDataChangedCallback != null)
        {
            _appDataChangedCallback.OnRun -= OnAppDataChanged;
            _appDataChangedCallback = null;
        }

        _steamClient = null;
        _libraryStats = null;
        CanRefresh = false;
        StatusText = $"Steam has stopped. Showing {_games.Count} games saved last time.";
    }

    /// <summary>
    /// Steam is there: start listening to it and refresh the library. This is
    /// what the constructor used to do unconditionally.
    /// </summary>
    private void ConnectSteam()
    {
        var client = _steam.Client;
        _steamGeneration++;
        _steamClient = client;

        _appDataChangedCallback = client.CreateAndRegisterCallback<SteamApi.Callbacks.AppDataChanged>();
        _appDataChangedCallback.OnRun += OnAppDataChanged;

        var steamId = client.SteamUser.GetSteamId();
        _libraryStats = LibraryStats.Create(steamId);
        UseSteamAccount((uint)(steamId & 0xFFFFFFFF));

        // The WinForms build pumped callbacks from a Forms.Timer; the
        // dispatcher timer is the direct equivalent and keeps callbacks on
        // the UI thread, which the rest of this class assumes.
        _callbackTimer = new(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, OnTimer);
        _callbackTimer.Start();

        Task initialLoad = RefreshLibraryAsync();
        GC.KeepAlive(initialLoad);
    }

    /// <summary>
    /// Makes <paramref name="account"/> the one on screen. Steam may have come back as a
    /// different account than the one shown from the cache; that account's own saved
    /// library goes up, and the previous one's stays saved under its id for next time.
    /// </summary>
    private void UseSteamAccount(uint account)
    {
        if (account == 0 || account == _steamAccount)
        {
            return;
        }

        _steamAccount = account;
        try
        {
            _cache.SetLastSteamAccount(account);
        }
        catch (Exception)
        {
            // Only decides what is shown when Steam is missing at the next start.
        }

        _games.Clear();
        LoadFromCache();
        RefreshCollectionCounts();
        RefreshGames();

        Task decode = HydrateCachedIconsAsync();
        GC.KeepAlive(decode);
    }

    /// <summary>
    /// Paints the window from the database before any network or Steam work
    /// happens. Everything here is already-persisted data.
    /// </summary>
    private void LoadFromCache()
    {
        // A cache that will not open must not stop the app; it just means every start is a
        // cold one.
        if (_cache.IsAvailable == false)
        {
            StatusText = _cache.Error == null
                ? "Loading games..."
                : $"Cache unavailable ({_cache.Error}).";
            return;
        }

        IReadOnlyList<CachedGame> rows;
        try
        {
            rows = _cache.LoadAll(_steamAccount);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var row in rows)
        {
            GameInfo info = new(row.Id, row.Type ?? "normal")
            {
                Name = row.Name,
                ImageUrl = row.ImageUrl,
                ReleaseDate = row.ReleaseDate,
                SteamRatingPercent = row.RatingPercent,
                SteamRatingScore = row.RatingScore,
                OwnRating = _ratingStore.Get(row.Id),
            };
            if (row.HasStats == true)
            {
                info.Stats = new GameStats(
                    row.PlaytimeMinutes,
                    row.AchievementsTotal,
                    row.AchievementsEarned,
                    row.LastPlayed);
            }
            _games[row.Id] = info;
        }

        if (_games.Count > 0)
        {
            RefreshCollectionCounts();
            RefreshGames();
            StatusText = $"Showing {_games.Count} cached games.";
        }
    }

    public void Shutdown()
    {
        _callbackTimer?.Stop();
        _steam.Changed -= OnSteamChanged;
        _steam.Dispose();
        ShutdownStoreLibraries();
        // The cache belongs to the container, which disposes it.
    }

    private void OnTimer(object sender, EventArgs e)
    {
        _steamClient?.RunCallbacks(false);
    }

    private void OnAppDataChanged(APITypes.AppDataChanged param)
    {
        if (param.Result == false)
        {
            return;
        }
        if (_games.TryGetValue(param.Id, out var info) == false)
        {
            return;
        }
        // An empty answer is Steam going away mid-call, not a game with no name.
        var name = _steamClient?.SteamApps001.GetAppData(info.Id, "name");
        if (string.IsNullOrEmpty(name) == false)
        {
            info.Name = name;
        }
        EnqueueLogo(info);
    }

    #region Game list

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_steamClient == null)
        {
            return;
        }

        AddGameText = "";
        await RefreshLibraryAsync();
    }

    /// <summary>
    /// Brings the cache up to date: re-reads the published list, re-checks
    /// ownership, refreshes stats, then writes the result back so the next
    /// start is instant. Runs after the cached view is already on screen.
    /// </summary>
    private async Task RefreshLibraryAsync()
    {
        // Saved either way: what a cut-short refresh found is real and worth keeping. It
        // only gets to delete when it finished.
        var complete = await LoadGamesAsync();
        await SaveCacheAsync(complete);
    }

    /// <param name="complete">
    /// Whether the synchronization finished. If not, rows are added and updated but none
    /// are deleted, and the capsules of games not seen are left alone.
    /// </param>
    private async Task SaveCacheAsync(bool complete)
    {
        var account = _steamAccount;
        if (account == 0)
        {
            return;
        }

        var rows = _games.Values.Select(info => new CachedGame()
        {
            Id = info.Id,
            Type = info.Type,
            Name = info.Name,
            ImageUrl = info.ImageUrl,
            ReleaseDate = info.ReleaseDate,
            RatingPercent = info.SteamRatingPercent,
            RatingScore = info.SteamRatingScore,
            PlaytimeMinutes = info.Stats?.PlaytimeMinutes ?? 0,
            LastPlayed = info.Stats?.LastPlayed,
            AchievementsTotal = info.Stats?.AchievementsTotal ?? -1,
            AchievementsEarned = info.Stats?.AchievementsEarned ?? -1,
            HasStats = info.Stats.HasValue,
        }).ToList();

        var cache = _cache;
        var icons = _iconCache;

        // What this account owns, as this refresh found it. Taken now, not read back from
        // the database: the table can hold only part of the library at this point, and
        // pruning against that part deleted most of the art of games still owned.
        var owned = _games.Keys.ToHashSet();

        try
        {
            // Rows for games no longer owned are deleted here, and their
            // cached capsules go with them.
            await Task.Run(() =>
            {
                cache.Sync(account, rows, removeMissing: complete);
                if (complete == true)
                {
                    // Art is kept for every game any saved account owns, not just this one.
                    var keep = cache.LoadAllGameIds();
                    keep.UnionWith(owned);
                    icons.Prune(keep);
                }
            });
        }
        catch (Exception e)
        {
            ErrorRaised?.Invoke("Could not update the game cache:\n" + e.Message);
        }
    }

    /// <summary>
    /// Decodes capsules already on disk, off the UI thread, so a warm start
    /// shows art without touching the network.
    /// </summary>
    private async Task HydrateCachedIconsAsync()
    {
        var games = _games.Values.Where(g => g.Logo == null).ToList();
        if (games.Count == 0)
        {
            return;
        }

        var icons = _iconCache;
        var total = games.Count;
        var step = ProgressStep(total);
        IProgress<int> decode = new Progress<int>(
            done => ReportProgress($"Loading cached icons, {done:N0} of {total:N0}...", done, total));
        BeginProgress("Loading cached icons...");
        ReportProgress($"Loading cached icons, 0 of {total:N0}...", 0, total);

        // One task per capsule. The read and the decode run on the thread pool, which is
        // what bounds how many go at once; everything after the await resumes on the UI
        // thread, so each capsule goes on screen as soon as it is ready and the counter
        // needs no locking.
        var finished = 0;
        var loadTasks = games.Select(async info =>
        {
            var bitmap = await Task.Run(() =>
            {
                var bytes = icons.TryRead(info.Id);
                if (bytes == null)
                {
                    return null;
                }
                try
                {
                    using MemoryStream stream = new(bytes, false);
                    return new Bitmap(stream);
                }
                catch (Exception)
                {
                    // A truncated cache file just means a re-download.
                    return null;
                }
            });

            // Not if the network got there first: it has the newer capsule.
            if (bitmap != null)
            {
                info.Logo ??= bitmap;
            }

            finished++;
            if (finished % step == 0)
            {
                decode.Report(finished);
            }
        }).ToList();
        await Task.WhenAll(loadTasks);

        EndProgress();
    }

    /// <summary>
    /// Checks the library against Steam: adds what it finds, and removes what it no
    /// longer lists.
    /// </summary>
    /// <returns>
    /// True only if the synchronization completed: the sweep and the stats pass both
    /// ran to the end under the same Steam. Anything less may have added games but
    /// never removes any, because what a cut-short check did not find says nothing
    /// about ownership: once Steam is gone, every remaining call answers "not owned".
    /// </returns>
    private async Task<bool> LoadGamesAsync()
    {
        // This refresh's Steam: the field may be null by the time the sweep runs.
        var client = _steamClient;
        var generation = _steamGeneration;
        var account = _steamAccount;
        CanRefresh = false;
        StatusText = "Downloading game list...";
        BeginProgress("Downloading game list...");

        List<KeyValuePair<uint, string>> pairs;
        try
        {
            var bytes = await _http.CreateClient().GetByteArrayAsync(new Uri("https://gib.me/sam/games.xml"));
            pairs = ParseGameList(bytes);
        }
        catch (Exception e)
        {
            // The list could not be had, so nothing was learned about ownership and
            // what is on screen stays as it is. It used to be replaced with Spacewar.
            CanRefresh = true;
            EndProgress();
            ErrorRaised?.Invoke(e.ToString());
            return false;
        }

        StatusText = "Checking game ownership...";

        // Created here, on the UI thread, so its callback marshals back to
        // the UI thread from inside the Task.Run below.
        var total = pairs.Count;
        var step = ProgressStep(total);
        IProgress<int> sweep = new Progress<int>(
            done => ReportProgress($"Checking ownership, {done:N0} of {total:N0}...", done, total));
        ReportProgress($"Checking ownership, 0 of {total:N0}...", 0, total);

        // Ownership and name lookups are Steam client calls, one per app id
        // over the whole published list, so they cannot run on the UI
        // thread. The WinForms build ran them on a BackgroundWorker for the
        // same reason.
        var (owned, steamHeld) = await Task.Run(() =>
        {
            Dictionary<uint, (string Type, string Name)> result = new();
            var examined = 0;
            foreach (var kv in pairs)
            {
                examined++;
                if (examined % step == 0)
                {
                    sweep.Report(examined);
                }
                if (result.ContainsKey(kv.Key) == true)
                {
                    continue;
                }
                if (client.SteamApps008.IsSubscribedApp(kv.Key) == false)
                {
                    continue;
                }
                result.Add(kv.Key, (kv.Value, client.SteamApps001.GetAppData(kv.Key, "name")));
            }

            // Asked here, the moment the last call is made, and not left to the watch:
            // that notices a lost Steam every few seconds, and a sweep over the whole
            // published list can finish inside that time against a pipe that died
            // halfway, with every later call answering "not owned".
            return (result, _steam.IsSteamStillRunning());
        });

        // Complete only if it was the same Steam from start to finish.
        var complete = steamHeld == true && generation == _steamGeneration;

        // Steam came back as someone else while this ran: what it found belongs to the
        // account that was on screen when it started, not to the one on screen now.
        if (account != _steamAccount)
        {
            EndProgress();
            return false;
        }

        // What was found is kept either way: Steam said those are owned. Merged rather
        // than rebuilt, because the cached entries already on screen carry decoded
        // capsules and the user's own rating, and replacing them wholesale would blank
        // the window and re-download everything.
        foreach (var kv in owned)
        {
            if (_games.TryGetValue(kv.Key, out var existing) == true)
            {
                // Never trade a known name for the placeholder: a name asked for just
                // as Steam went away comes back empty.
                if (string.IsNullOrEmpty(kv.Value.Name) == false)
                {
                    existing.Name = kv.Value.Name;
                }
                continue;
            }
            _games[kv.Key] = new GameInfo(kv.Key, kv.Value.Type)
            {
                Name = kv.Value.Name,
                OwnRating = _ratingStore.Get(kv.Key),
            };
        }

        // What Steam no longer lists. Worked out now, applied only once everything below
        // has finished too.
        var gone = complete == true
            ? _games.Keys.Where(id => owned.ContainsKey(id) == false).ToList()
            : new List<uint>();

        RefreshCollectionCounts();
        RefreshGames();
        CanRefresh = _steamClient != null;
        EndProgress();

        if (complete == false)
        {
            return false;
        }

        if (await LoadLibraryStatsAsync() == false)
        {
            return false;
        }

        // The sweep and the stats pass both finished under the same Steam. Only now may
        // a game go.
        foreach (var id in gone)
        {
            _games.Remove(id);
        }
        if (gone.Count > 0)
        {
            RefreshCollectionCounts();
            RefreshGames();
        }
        return true;
    }

    /// <summary>
    /// Reads playtime and achievement counts for every owned game off disk.
    /// Done eagerly in the background rather than on first switch to the
    /// content view, so the view has data the moment it is shown. It is
    /// file I/O over a few hundred small files, so it stays off the UI
    /// thread.
    /// </summary>
    private async Task<bool> LoadLibraryStatsAsync()
    {
        var games = _games.Values.ToList();
        var client = _steamClient;
        var libraryStats = _libraryStats;
        var generation = _steamGeneration;

        // Own ratings are already in memory; apply them before anything
        // that might sort on them.
        foreach (var info in games)
        {
            info.OwnRating = _ratingStore.Get(info.Id);
        }

        var total = games.Count;
        var step = ProgressStep(total);
        IProgress<int> pass = new Progress<int>(
            done => ReportProgress($"Reading library stats, {done:N0} of {total:N0}...", done, total));
        BeginProgress("Reading library stats...");
        ReportProgress($"Reading library stats, 0 of {total:N0}...", 0, total);

        // Both halves are slow for a few hundred games: file I/O for the
        // caches, and two Steam calls per app for the store metadata.
        var (loaded, steamHeld) = await Task.Run(() =>
        {
            Dictionary<uint, (GameStats? Stats, int? Percent, int? Score, DateTime? Released)> result = new();
            var done = 0;
            foreach (var info in games)
            {
                done++;
                if (done % step == 0)
                {
                    pass.Report(done);
                }
                var stats = libraryStats?.TryGet(info.Id);
                result[info.Id] = (
                    stats,
                    ParseInt(client.SteamApps001.GetAppData(info.Id, "review_percentage")),
                    ParseInt(client.SteamApps001.GetAppData(info.Id, "review_score")),
                    ParseUnixDate(client.SteamApps001.GetAppData(info.Id, "steam_release_date")));
            }
            return (result, _steam.IsSteamStillRunning());
        });

        // Without Steam the review scores and release dates all came back empty, and
        // applying them would blank what the cache has.
        if (steamHeld == false || generation != _steamGeneration)
        {
            EndProgress();
            return false;
        }

        foreach (var info in games)
        {
            if (loaded.TryGetValue(info.Id, out var value) == false)
            {
                continue;
            }
            info.Stats = value.Stats;
            info.SteamRatingPercent = value.Percent;
            info.SteamRatingScore = value.Score;
            info.ReleaseDate = value.Released;
        }

        EndProgress();
        RefreshCollectionCounts();

        // Completion only becomes known here, so a collection or sort that
        // depends on it has to be re-evaluated.
        if (SortField != GameSortField.Name ||
            SelectedCollection != GameCollection.All)
        {
            RefreshGames();
        }

        return true;
    }

    private static int? ParseInt(string value)
    {
        return string.IsNullOrEmpty(value) == false &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseUnixDate(string value)
    {
        return string.IsNullOrEmpty(value) == false &&
               long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
               seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            : null;
    }

    private static List<KeyValuePair<uint, string>> ParseGameList(byte[] bytes)
    {
        List<KeyValuePair<uint, string>> pairs = new();
        using MemoryStream stream = new(bytes, false);
        XPathDocument document = new(stream);
        var navigator = document.CreateNavigator();
        var nodes = navigator.Select("/games/game");
        while (nodes.MoveNext() == true)
        {
            var type = nodes.Current.GetAttribute("type", "");
            if (string.IsNullOrEmpty(type) == true)
            {
                type = "normal";
            }
            pairs.Add(new((uint)nodes.Current.ValueAsLong, type));
        }
        return pairs;
    }

    private void AddGame(uint id, string type)
    {
        if (_games.ContainsKey(id) == true)
        {
            return;
        }
        if (_steamClient == null || _steamClient.SteamApps008.IsSubscribedApp(id) == false)
        {
            return;
        }
        GameInfo info = new(id, type)
        {
            Name = _steamClient.SteamApps001.GetAppData(id, "name"),
        };
        _games.Add(id, info);
    }

    private void RefreshGames()
    {
        var nameSearch = SearchText.Length > 0 ? SearchText : null;

        FilteredGames.Clear();
        foreach (var info in ApplySort(_games.Values))
        {
            if (nameSearch != null &&
                info.Name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            bool wanted = info.Type switch
            {
                "normal" => ShowGames,
                "demo" => ShowDemos,
                "mod" => ShowMods,
                "junk" => ShowJunk,
                _ => true,
            };
            if (wanted == false)
            {
                continue;
            }

            var inCollection = SelectedCollection switch
            {
                GameCollection.RecentlyPlayed => info.LastPlayed.HasValue,
                GameCollection.WithAchievements => info.HasCompletion,
                GameCollection.Perfect => info.IsPerfect,
                GameCollection.Liked => info.IsLiked,
                _ => true,
            };
            if (inCollection == false)
            {
                continue;
            }

            FilteredGames.Add(info);
        }

        StatusText =
            $"Displaying {FilteredGames.Count} games. Total {_games.Count} games.";

        if (FilteredGames.Count > 0)
        {
            SelectedGame = FilteredGames[0];
        }

        foreach (var info in FilteredGames)
        {
            EnqueueLogo(info);
        }
    }

    [RelayCommand]
    private void AddGameById()
    {
        if (uint.TryParse(AddGameText, out var id) == false)
        {
            ErrorRaised?.Invoke("Please enter a valid game ID.");
            return;
        }

        if (_steamClient == null)
        {
            ErrorRaised?.Invoke("Steam is not running, so a game cannot be looked up by its ID.");
            return;
        }

        if (_steamClient.SteamApps008.IsSubscribedApp(id) == false)
        {
            ErrorRaised?.Invoke("You don't own that game.");
            return;
        }

        while (_logoQueue.TryDequeue(out var discarded) == true)
        {
            // Clear the queue; only one app is about to be shown.
            _logosAttempted.Remove(discarded.ImageUrl);
        }

        AddGameText = "";
        _games.Clear();
        AddGame(id, "normal");
        ShowGames = true;
        RefreshGames();
    }

    [RelayCommand]
    private void LaunchSelected()
    {
        var info = SelectedGame;
        if (info == null)
        {
            return;
        }

        // The helper that reads and writes the achievements has to reach Steam itself, and
        // would only report that it could not; say so here instead.
        if (_steamClient == null)
        {
            ErrorRaised?.Invoke("Steam is not running. The achievement editor needs it: start Steam and try again.");
            return;
        }

        OpenSteamAchievements(info);
    }

    #endregion

    #region Logos

    private string GetGameImageUrl(uint id)
    {
        var currentLanguage = _steamClient.SteamApps008.GetCurrentGameLanguage();

        var candidate = _steamClient.SteamApps001.GetAppData(id, _($"small_capsule/{currentLanguage}"));
        if (string.IsNullOrEmpty(candidate) == false)
        {
            return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
        }

        if (currentLanguage != "english")
        {
            candidate = _steamClient.SteamApps001.GetAppData(id, "small_capsule/english");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
            }
        }

        candidate = _steamClient.SteamApps001.GetAppData(id, "logo");
        if (string.IsNullOrEmpty(candidate) == false)
        {
            return _($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{candidate}.jpg");
        }

        return null;
    }

    private void EnqueueLogo(GameInfo info)
    {
        if (info.Logo != null)
        {
            return;
        }

        // Steam knows the current capsule; without it, the address saved with the game.
        var imageUrl = _steamClient != null ? GetGameImageUrl(info.Id) : info.ImageUrl;
        if (string.IsNullOrEmpty(imageUrl) == true)
        {
            return;
        }

        info.ImageUrl = imageUrl;

        if (_logosAttempted.Add(imageUrl) == false)
        {
            return;
        }

        if (_logosQueued == 0)
        {
            BeginProgress("Loading game icons...");
        }

        _logoQueue.Enqueue(info);
        _logosQueued++;
        _logoSignal.Release();
    }

    /// <summary>
    /// One of <see cref="LogoWorkers"/> consumers of the queue. They all resume on the UI
    /// thread after each await, so the counters below are only ever touched from it.
    /// </summary>
    private async Task RunLogoWorkerAsync()
    {
        while (true)
        {
            await _logoSignal.WaitAsync();

            if (_logoQueue.TryDequeue(out var info) == false)
            {
                continue;
            }

            await LoadLogoAsync(info);

            // Counted here rather than inside LoadLogoAsync so that every
            // way of finishing an item -- already loaded, read from disk,
            // downloaded, or failed -- advances the bar by exactly one.
            _logosCompleted++;
            if (_logosCompleted == _logosQueued)
            {
                EndProgress();
                _logosQueued = 0;
                _logosCompleted = 0;
                continue;
            }

            ReportProgress(
                $"Loading game icons, {_logosCompleted:N0} of {_logosQueued:N0}...",
                _logosCompleted,
                _logosQueued);
        }
    }

    private async Task LoadLogoAsync(GameInfo info)
    {
        if (info.Logo != null)
        {
            // Hydrated from the disk cache while this was queued.
            return;
        }

        // Disk before network: a warm cache means no request at all.
        var icons = _iconCache;
        var appId = info.Id;
        // Read and decoded off the UI thread: decoding a few hundred capsules there
        // one after another is what made a warm start slow.
        var cachedLogo = await Task.Run(() =>
        {
            var bytes = icons.TryRead(appId);
            if (bytes == null)
            {
                return null;
            }
            try
            {
                using MemoryStream stream = new(bytes, false);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                // Fall through and re-download a corrupt cache entry.
                return null;
            }
        });
        if (cachedLogo != null)
        {
            info.Logo = cachedLogo;
            return;
        }

        try
        {
            var data = await _http.CreateClient().GetByteArrayAsync(new Uri(info.ImageUrl));
            // Avalonia's Bitmap copies the decoded pixels, so unlike
            // System.Drawing it does not need the stream kept alive.
            info.Logo = await Task.Run(() =>
            {
                using MemoryStream stream = new(data, false);
                return new Bitmap(stream);
            });
            await Task.Run(() => icons.Write(appId, data));
        }
        catch (Exception)
        {
            // A missing capsule image is not worth surfacing.
        }
    }

    #endregion
}
