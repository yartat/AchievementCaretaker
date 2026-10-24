/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.Library;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Steam;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.App.ViewModels;

/// <summary>
/// The library window's other sources: Epic and GOG beside Steam.
/// </summary>
/// <remarks>
/// A separate file of the same class so the Steam half stays as it was. One source
/// is on screen at a time. Steam is the default and is represented by
/// <see cref="SelectedStoreLibrary"/> being null, not by an entry of its own,
/// because it is not a store: see <see cref="IStoreProvider"/>.
/// </remarks>
internal sealed partial class GamePickerViewModel : IProgressSink
{
    public IReadOnlyList<StoreLibraryViewModel> StoreLibraries { get; private set; } =
        Array.Empty<StoreLibraryViewModel>();

    private StoreLibraryViewModel _selectedStoreLibrary;

    /// <summary>The store on screen, or null for Steam.</summary>
    public StoreLibraryViewModel SelectedStoreLibrary
    {
        get => _selectedStoreLibrary;
        private set
        {
            var previous = _selectedStoreLibrary;
            if (SetProperty(ref _selectedStoreLibrary, value) == false)
            {
                return;
            }

            if (previous != null)
            {
                previous.IsSelected = false;
            }
            if (value != null)
            {
                value.IsSelected = true;
            }

            foreach (var name in new[]
            {
                nameof(IsSteamSelected), nameof(IsStoreSelected), nameof(ShowSteamBanner),
                nameof(ShowSteamTiles), nameof(ShowSteamContent),
                nameof(ActiveStatusText), nameof(HeaderTitle), nameof(HeaderSummary),
            })
            {
                OnPropertyChanged(name);
            }
        }
    }

    public bool IsSteamSelected => SelectedStoreLibrary == null;

    /// <summary>False until the connector has reached Steam. Cached games are shown meanwhile.</summary>
    public bool IsSteamConnected => _steamConnected;

    /// <summary>The Steam source is showing, and Steam is not there to refresh it from.</summary>
    public bool ShowSteamBanner => IsSteamSelected == true && IsSteamConnected == false;

    /// <summary>Why there is no Steam: not installed, not running, nobody logged in.</summary>
    public string SteamUnavailableText => _steam?.Reason;

    private void NotifySteamState()
    {
        OnPropertyChanged(nameof(IsSteamConnected));
        OnPropertyChanged(nameof(ShowSteamBanner));
        OnPropertyChanged(nameof(SteamUnavailableText));
    }

    public bool IsStoreSelected => SelectedStoreLibrary != null;

    // The Steam tile and row views are shown only for Steam, and only in the chosen
    // mode; the store library draws its own.
    public bool ShowSteamTiles => IsSteamSelected == true && IsTileView == true;

    public bool ShowSteamContent => IsSteamSelected == true && IsContentView == true;

    // What the status bar and the header show: the selected source's.
    public string ActiveStatusText => SelectedStoreLibrary?.StatusText ?? StatusText;

    public string HeaderTitle => SelectedStoreLibrary?.CollectionTitle ?? CollectionTitle;

    public string HeaderSummary => SelectedStoreLibrary?.LibrarySummaryText ?? LibrarySummaryText;

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(ActiveStatusText));
    }

    /// <summary>Raised when a store game's achievements should be shown; the window owns the window.</summary>
    public event Action<StoreAchievementsViewModel> StoreAchievementsRequested;

    /// <summary>Raised to sign in to a store in a browser inside the application.</summary>
    public event Func<IStoreAccount, Task<BrowserSignInResult>> BrowserSignInRequested;

    /// <summary>Raised before a sign-out; the window asks the user.</summary>
    public event Func<string, string, Task<bool>> ConfirmRequested;

    [RelayCommand]
    private void SelectSteam()
    {
        SelectedStoreLibrary = null;
    }

    [RelayCommand]
    private void SelectStore(StoreLibraryViewModel store)
    {
        SelectedStoreLibrary = store;
    }

    /// <summary>The stores to offer, from the container. Their lifetime is the container's, not this view model's.</summary>
    private readonly IEnumerable<IStoreProvider> _storeProviders;

    /// <summary>Who is signed in to each of <see cref="_storeProviders"/>: the same objects, in their other role, found by <see cref="IStore.Id"/>.</summary>
    private readonly IEnumerable<IStoreAccount> _storeAccounts;

    private StoreImageCache _storeImages;

    /// <summary>
    /// Opens a Steam game in the achievements window, in this process. The window talks to a
    /// helper process started for that one app id, which is where the "one process per game"
    /// rule of Steam's user stats now lives.
    /// </summary>
    private void OpenSteamAchievements(GameInfo info)
    {
        var id = info.Id.ToString(CultureInfo.InvariantCulture);
        StoreGameInfo game = new(SteamProvider.ProviderId, id, id) { Name = info.Name };
        var images = _storeImages ??= new(_iconCache, _http);

        StoreAchievementsRequested?.Invoke(new StoreAchievementsViewModel(
            _steamProvider,
            _storeAchievements,
            game,
            images,
            (earned, total) =>
            {
                // What the window just read is fresher than the cache files: show it here too.
                var known = info.Stats;
                info.Stats = new GameStats(known?.PlaytimeMinutes ?? 0, total, earned, known?.LastPlayed);
                RefreshCollectionCounts();
            }));
    }

    private void CreateStoreLibraries()
    {
        // One image cache for all of them: it follows the icon cache's directory
        // as it moves, so it is built from that rather than from a path.
        StoreImageCache images = new(_iconCache, _http);
        _storeImages = images;

        var accounts = _storeAccounts.ToDictionary(account => account.Id);
        List<StoreLibraryViewModel> stores = new();
        foreach (var provider in _storeProviders)
        {
            if (accounts.TryGetValue(provider.Id, out var account) == false)
            {
                throw new InvalidOperationException(
                    $"The store '{provider.Id}' is registered as an IStoreProvider but not as an IStoreAccount.");
            }

            // The database can be replaced by a cache move, so hand over how to
            // reach it rather than the instance.
            StoreLibraryViewModel store = new(provider, account, _storeLibrary, _storeSession, _storeAchievements, images, this)
            {
                IsContentView = IsContentView,
            };
            store.ErrorRaised += message => ErrorRaised?.Invoke(message);
            store.AchievementsRequested += viewer => StoreAchievementsRequested?.Invoke(viewer);
            store.BrowserSignInRequested += requested =>
                BrowserSignInRequested?.Invoke(requested) ?? Task.FromResult(new BrowserSignInResult(null, true));
            store.ConfirmRequested += (title, message) =>
                ConfirmRequested?.Invoke(title, message) ?? Task.FromResult(true);
            store.PropertyChanged += OnStorePropertyChanged;
            stores.Add(store);
        }

        StoreLibraries = stores;
        OnPropertyChanged(nameof(StoreLibraries));
    }

    private void OnStorePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedStoreLibrary) == false)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(StoreLibraryViewModel.StatusText):
                OnPropertyChanged(nameof(ActiveStatusText));
                break;
            case nameof(StoreLibraryViewModel.CollectionTitle):
                OnPropertyChanged(nameof(HeaderTitle));
                break;
            case nameof(StoreLibraryViewModel.LibrarySummaryText):
                OnPropertyChanged(nameof(HeaderSummary));
                break;
        }
    }

    private void OnViewModeChanged()
    {
        OnPropertyChanged(nameof(ShowSteamTiles));
        OnPropertyChanged(nameof(ShowSteamContent));
        foreach (var store in StoreLibraries)
        {
            store.IsContentView = IsContentView;
        }
    }

    private void ForwardSearchToStores(string text)
    {
        foreach (var store in StoreLibraries)
        {
            store.SearchText = text;
        }
    }

    private void ShutdownStoreLibraries()
    {
        foreach (var store in StoreLibraries)
        {
            store.Shutdown();
        }
    }

    void IProgressSink.Begin(string text)
    {
        BeginProgress(text);
    }

    void IProgressSink.Report(string text, int completed, int total)
    {
        ReportProgress(text, completed, total);
    }

    void IProgressSink.End()
    {
        EndProgress();
    }
}
