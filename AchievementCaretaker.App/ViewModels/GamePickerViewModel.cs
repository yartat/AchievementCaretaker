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
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.Library;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;
using AchievementCaretaker.Providers.Steam;
using AchievementCaretaker.Services;
using static AchievementCaretaker.App.InvariantShorthand;
using AchievementCaretaker.Providers.Steam.Models;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.App.ViewModels;

internal sealed partial class GamePickerViewModel : ProgressViewModel
{
    private readonly IHttpClientFactory _http;

    // Steam is looked for in the background and may not be there at all. The
    // application holds no connection to it: the connector says whether a helper can
    // reach it, and the library service asks the helper.
    private readonly SteamConnector _steam;
    private readonly ISteamLibraryService _steamLibrary;
    private bool _steamConnected;

    // Bumped each time Steam is connected or lost. A refresh remembers the number it
    // started under, and throws away what it found if it has changed: a scan that
    // outlived its Steam saw nothing owned, and merging that would empty the library
    // and then the cache.
    private int _steamGeneration;

    /// <summary>
    /// The Steam account (32-bit id) whose library is on screen and is saved to, or 0
    /// before any Steam has been seen. The cache holds a library per account; this is
    /// which one is showing. Without Steam it is the account shown last time.
    /// </summary>
    private uint _steamAccount;

    private readonly Dictionary<uint, GameInfo> _games = new();

    /// <summary>
    /// How many capsules are downloaded at once. Capsules are read when their tile comes into view
    /// (<c>LazyImage</c>), and a fast scroll asks for dozens.
    /// </summary>
    private const int LogoDownloads = 8;

    private readonly SemaphoreSlim _logoDownloads = new(LogoDownloads);

    public ObservableCollection<GameInfo> FilteredGames { get; } = new();

    [ObservableProperty]
    private GameInfo? _selectedGame;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _addGameText = "";

    // Off until Steam has been reached: there is nothing to refresh from before that.
    [ObservableProperty]
    private bool _canRefresh;

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
    public event Func<AppSettings, Task<AppSettings?>>? SettingsRequested;

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

    private void SetFilter(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
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
    public event Action<string>? ErrorRaised;

    public GamePickerViewModel(SteamConnector steam, ISteamLibraryService steamLibrary, IEnumerable<IStoreProvider> storeProviders, IEnumerable<IStoreAccount> storeAccounts, IGameRepository cache,
        IDatabaseRelocator relocator,
        IStoreAchievementsService storeAchievements, IStoreLibraryService storeLibrary,
        IStoreSessionService storeSession,
        SteamProvider steamProvider,
        IHttpClientFactory http)
    {
        _steam = steam;
        _steamLibrary = steamLibrary;
        _storeProviders = storeProviders;
        _storeAccounts = storeAccounts;
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

        CreateStoreLibraries();

        // Last, and without waiting: Steam may not be running. The window is
        // already usable, and the Steam library refreshes from the moment the
        // connector reaches Steam, if it ever does.
        steam.Changed += OnSteamChanged;
        steam.Start();
    }

    private void OnSteamChanged()
    {
        // The connector's account is the truth. A different one, or none, means the one
        // held here is gone; a new one is a new connection.
        var account = _steam.Account;
        if (_steamConnected == true && account == null)
        {
            DisconnectSteam();
        }
        if (account != null && _steamConnected == false)
        {
            ConnectSteam(account.Value);
        }
        NotifySteamState();
    }

    /// <summary>
    /// Steam has gone. Keep what is on screen: the cached games are still the best there
    /// is, and they are refreshed if Steam returns.
    /// </summary>
    private void DisconnectSteam()
    {
        _steamGeneration++;
        _steamConnected = false;
        CanRefresh = false;
        StatusText = $"Steam has stopped. Showing {_games.Count} games saved last time.";
    }

    /// <summary>
    /// Steam is there: show its account's library and refresh it.
    /// </summary>
    private void ConnectSteam(uint account)
    {
        _steamGeneration++;
        _steamConnected = true;
        UseSteamAccount(account);

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
        _steam.Changed -= OnSteamChanged;
        _steam.Dispose();
        ShutdownStoreLibraries();
        // The cache belongs to the container, which disposes it.
    }

    #region Game list

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_steamConnected == false)
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
    /// Checks the library against Steam: adds what it finds, and removes what it no
    /// longer lists.
    /// </summary>
    /// <returns>
    /// True only if the synchronization completed: the scan ran to the end under the same
    /// Steam. Anything less may have added games but never removes any, because what a
    /// cut-short check did not find says nothing about ownership: once Steam is gone,
    /// every remaining question is answered "not owned".
    /// </returns>
    private async Task<bool> LoadGamesAsync()
    {
        var generation = _steamGeneration;
        var account = _steamAccount;
        CanRefresh = false;
        StatusText = "Downloading game list...";
        BeginProgress("Downloading game list...");

        LibraryScan scan;
        try
        {
            var candidates = await _steamLibrary.GetPublishedGamesAsync();

            StatusText = "Checking game ownership...";
            var total = candidates.Count;

            // Created here, on the UI thread, so its callback marshals back to it.
            IProgress<LibraryScanProgress> sweep = new Progress<LibraryScanProgress>(
                step => ReportProgress($"Checking ownership, {step.Examined:N0} of {step.Total:N0}...", step.Examined, step.Total));
            ReportProgress($"Checking ownership, 0 of {total:N0}...", 0, total);

            scan = await _steamLibrary.ScanAsync(candidates, sweep);
        }
        catch (ProviderException e)
        {
            // Nothing was learned about ownership, so what is on screen stays as it is.
            // A Steam that went away is the connector's news, not an error to click away.
            CanRefresh = _steamConnected;
            EndProgress();
            if (generation == _steamGeneration)
            {
                ErrorRaised?.Invoke(e.Message);
            }
            return false;
        }

        // Complete only if it was the same Steam from start to finish.
        var complete = scan.Complete == true && generation == _steamGeneration;

        // Steam came back as someone else while this ran: what it found belongs to the
        // account that was on screen when it started, not to the one on screen now.
        if (account != _steamAccount || scan.Account != _steamAccount.ToString(CultureInfo.InvariantCulture))
        {
            CanRefresh = _steamConnected;
            EndProgress();
            return false;
        }

        // What was found is kept either way: Steam said those are owned. Merged rather
        // than rebuilt, because the cached entries already on screen carry decoded
        // capsules and the user's own rating, and replacing them wholesale would blank
        // the window and re-download everything.
        HashSet<uint> owned = new();
        foreach (var game in scan.Games)
        {
            var id = uint.Parse(game.Id, CultureInfo.InvariantCulture);
            owned.Add(id);
            if (_games.TryGetValue(id, out var info) == false)
            {
                info = new GameInfo(id, game.Type ?? "normal")
                {
                    Name = game.Title ?? "",
                    OwnRating = _ratingStore.Get(id),
                };
                _games[id] = info;
            }

            // Never trade a known name for the placeholder: a name asked for just as
            // Steam went away comes back empty.
            if (string.IsNullOrEmpty(game.Title) == false)
            {
                info.Name = game.Title;
            }

            // The details of a cut-short scan may all be empty, and applying them would
            // blank what the cache has.
            if (complete == false)
            {
                continue;
            }

            if (game.ImageUrl != null && game.ImageUrl != info.ImageUrl)
            {
                // A new address: the tile asks again, from RefreshGames below.
                info.ImageUrl = game.ImageUrl;
                info.LogoLoader = null;
            }
            info.ReleaseDate = game.ReleaseDate?.LocalDateTime;
            info.SteamRatingPercent = game.RatingPercent;
            info.SteamRatingScore = game.RatingScore;
            info.Stats = game.PlaytimeMinutes.HasValue == true
                ? new GameStats(
                    game.PlaytimeMinutes.Value,
                    game.AchievementsTotal ?? -1,
                    game.AchievementsEarned ?? -1,
                    game.LastPlayed?.LocalDateTime)
                : null;
        }

        // Only a finished scan may drop a game.
        if (complete == true)
        {
            foreach (var id in _games.Keys.Where(id => owned.Contains(id) == false).ToList())
            {
                _games.Remove(id);
            }
        }

        CanRefresh = _steamConnected;
        EndProgress();
        RefreshCollectionCounts();
        RefreshGames();
        return complete;
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
            info.LogoLoader ??= token => LoadLogoAsync(info, token);
        }
    }

    [RelayCommand]
    private async Task AddGameByIdAsync()
    {
        if (uint.TryParse(AddGameText, out var id) == false)
        {
            ErrorRaised?.Invoke("Please enter a valid game ID.");
            return;
        }

        if (_steamConnected == false)
        {
            ErrorRaised?.Invoke("Steam is not running, so a game cannot be looked up by its ID.");
            return;
        }

        StoreGame? game;
        try
        {
            game = await _steamLibrary.FindOwnedAsync(id);
        }
        catch (ProviderException e)
        {
            ErrorRaised?.Invoke(e.Message);
            return;
        }
        if (game == null)
        {
            ErrorRaised?.Invoke("You don't own that game.");
            return;
        }

        AddGameText = "";
        _games.Clear();
        _games[id] = new GameInfo(id, game.Type ?? "normal")
        {
            Name = game.Title ?? "",
            ImageUrl = game.ImageUrl,
            OwnRating = _ratingStore.Get(id),
        };
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
        if (_steamConnected == false)
        {
            ErrorRaised?.Invoke("Steam is not running. The achievement editor needs it: start Steam and try again.");
            return;
        }

        OpenSteamAchievements(info);
    }

    #endregion

    #region Logos

    /// <summary>
    /// The capsule, from the disk cache if it is there and from the network if not (and then kept).
    /// Runs when the tile comes into view; null when there is none.
    /// </summary>
    private async Task<Bitmap?> LoadLogoAsync(GameInfo info, CancellationToken token)
    {
        var icons = _iconCache;
        var appId = info.Id;

        // Read and decoded off the UI thread: decoding a screenful there is what made scrolling stall.
        var cached = await Task.Run(() => Decode(icons.TryRead(appId)), token);
        if (cached != null)
        {
            return cached;
        }

        var url = info.ImageUrl;
        if (string.IsNullOrEmpty(url) == true)
        {
            return null;
        }

        await _logoDownloads.WaitAsync(token);
        try
        {
            var data = await _http.CreateClient().GetByteArrayAsync(new Uri(url), token);
            var bitmap = await Task.Run(() => Decode(data), token);
            if (bitmap != null)
            {
                await Task.Run(() => icons.Write(appId, data), token);
            }

            return bitmap;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A missing capsule image is not worth surfacing.
            return null;
        }
        finally
        {
            _logoDownloads.Release();
        }
    }

    /// <summary>Null for no data, and for data that is not an image: a truncated cache file means a re-download.</summary>
    private static Bitmap? Decode(byte[]? data)
    {
        if (data == null)
        {
            return null;
        }
        try
        {
            using MemoryStream stream = new(data, false);
            // Avalonia's Bitmap copies the decoded pixels, so the stream can go.
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion
}
