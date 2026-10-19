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

namespace AchievementCaretaker.App.ViewModels
{
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
        private readonly IStoreProvider _Provider;
        private readonly IStoreLibraryService _Library;
        private readonly IStoreSessionService _Session;
        private readonly IStoreAchievementsService _Achievements;
        private readonly StoreImageCache _Images;
        private readonly IProgressSink _Sink;
        private readonly CancellationTokenSource _Lifetime = new();

        private readonly Dictionary<string, StoreGameInfo> _Games = new();

        // Logos: one consumer, like the Steam window's queue, counted so the bar has a
        // denominator. UI thread only.
        private readonly Channel<StoreGameInfo> _LogoQueue =
            Channel.CreateUnbounded<StoreGameInfo>(new() { SingleReader = false });

        /// <summary>How many images are read, decoded or downloaded at once.</summary>
        private const int LogoWorkers = 8;
        private readonly HashSet<string> _LogosAttempted = new();
        private int _LogosQueued;
        private int _LogosCompleted;

        private CancellationTokenSource _ProgressRun;

        public ObservableCollection<StoreGameInfo> FilteredGames { get; } = new();

        public string Id => this._Provider.Id;

        public string DisplayName => this._Provider.DisplayName;

        /// <summary>The picker raises its own error dialog from this.</summary>
        public event Action<string> ErrorRaised;

        /// <summary>The window opens the achievement viewer from this.</summary>
        public event Action<StoreAchievementsViewModel> AchievementsRequested;

        /// <summary>
        /// Raised to sign in with the store's own page in a browser inside the
        /// application; the window owns the window. Returns what the page ended on.
        /// </summary>
        public event Func<IStoreProvider, Task<BrowserSignInResult>> BrowserSignInRequested;

        /// <summary>Raised before something the user may not want; the window asks.</summary>
        public event Func<string, string, Task<bool>> ConfirmRequested;

        [ObservableProperty]
        private StoreGameInfo _SelectedGame;

        /// <summary>Whether this is the source the rail has selected. Set by the picker.</summary>
        [ObservableProperty]
        private bool _IsSelected;

        private bool _IsContentView;

        /// <summary>
        /// Tiles or rows. The choice belongs to the picker, which pushes it to every
        /// source so switching source does not switch view.
        /// </summary>
        public bool IsContentView
        {
            get => this._IsContentView;
            set
            {
                if (this.SetProperty(ref this._IsContentView, value) == true)
                {
                    this.OnPropertyChanged(nameof(this.IsTileView));
                }
            }
        }

        public bool IsTileView => this._IsContentView == false;

        [ObservableProperty]
        private string _StatusText = "";

        public StoreLibraryViewModel(
            IStoreProvider provider,
            IStoreLibraryService library,
            IStoreSessionService session,
            IStoreAchievementsService achievements,
            StoreImageCache images,
            IProgressSink sink)
        {
            this._Achievements = achievements;
            this._Provider = provider;
            this._Library = library;
            this._Session = session;
            this._Images = images;
            this._Sink = sink;

            this.LoadFromCache();

            Task[] logoWorkers = Enumerable.Range(0, LogoWorkers).Select(index => this.RunLogoWorkerAsync()).ToArray();
            Task initialLoad = this.RefreshLibraryAsync();
            GC.KeepAlive((logoWorkers, initialLoad));
        }

        #region Sign-in

        public string SignInTitle => $"Sign in to {this.DisplayName}";

        public string SignInIntro => $"Sign in with your {this.DisplayName} account. The sign-in page opens in a window of Caretaker.";

        /// <summary>What <see cref="PastedCode"/> is for, from the provider.</summary>
        public string SignInPrompt => $"Sign in with your {this.DisplayName} account in the browser, then paste {this._Provider.SignInHint}.";

        public bool IsSignedIn => this._Provider.IsSignedIn;

        public bool IsSignedOut => this._Provider.IsSignedIn == false;

        /// <summary>The rail's account line: who is signed in, or the way to sign in.</summary>
        public string RailAccountText =>
            this.IsSignedIn == true
                ? this._Provider.AccountName ?? "Signed in"
                : "Sign in";

        public string AccountText =>
            this._Provider.AccountName == null
                ? $"Signed in to {this.DisplayName}"
                : $"{this.DisplayName} · {this._Provider.AccountName}";

        [ObservableProperty]
        private string _PastedCode = "";

        /// <summary>Shown inline in the sign-in panel; a sign-in problem is not worth a dialog.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _ErrorText = "";

        public bool HasError => string.IsNullOrEmpty(this.ErrorText) == false;

        private bool _IsBusy;
        public bool IsBusy
        {
            get => this._IsBusy;
            private set
            {
                if (this.SetProperty(ref this._IsBusy, value) == true)
                {
                    this.OnPropertyChanged(nameof(this.CanRefresh));
                    this.OnPropertyChanged(nameof(this.CanLoadProgress));
                }
            }
        }

        private void NotifySignInChanged()
        {
            foreach (var name in new[]
            {
                nameof(this.IsSignedIn), nameof(this.IsSignedOut), nameof(this.AccountText),
                nameof(this.RailAccountText), nameof(this.CanRefresh), nameof(this.CanLoadProgress),
            })
            {
                this.OnPropertyChanged(name);
            }
        }

        [RelayCommand]
        private void OpenSignInPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(this._Provider.SignInUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                this.ErrorText = "Could not open the browser. Open this address yourself:\n" + this._Provider.SignInUrl;
            }
        }

        /// <summary>The fallback: the user signed in elsewhere and pasted the result.</summary>
        [RelayCommand]
        private Task SignInAsync()
        {
            return this.CompleteSignInAsync(this.PastedCode);
        }

        /// <summary>Signs in with the store's own page, in a browser inside the application.</summary>
        [RelayCommand]
        private Task SignInWithBrowserAsync()
        {
            return this.BrowserSignInAsync();
        }

        /// <summary>
        /// Signs in as a different account. The current one stays until the new sign-in
        /// has succeeded, so backing out of the page costs nothing.
        /// </summary>
        [RelayCommand]
        private async Task SwitchAccountAsync()
        {
            if (this.IsBusy == true)
            {
                return;
            }
            var switching = this._Session.ConfirmSwitchAccount(this._Provider);
            if (await this.ConfirmAsync(switching.Title, switching.Message) == false)
            {
                return;
            }
            await this.BrowserSignInAsync();
        }

        private async Task BrowserSignInAsync()
        {
            if (this.IsBusy == true || this.BrowserSignInRequested == null)
            {
                return;
            }

            this.ErrorText = "";
            var result = await this.BrowserSignInRequested(this._Provider);
            if (result.Pasted != null)
            {
                await this.CompleteSignInAsync(result.Pasted);
            }
            else if (result.Unavailable == true)
            {
                if (this.IsSignedIn == true)
                {
                    this.ErrorRaised?.Invoke(
                        "The browser built into Caretaker could not start. Sign out, then sign in with your own browser.");
                    return;
                }
                this.OpenSignInPage();
                this.ErrorText = $"The built-in browser could not start, so your own browser was opened. Paste {this._Provider.SignInHint} below.";
            }
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            return this.ConfirmRequested == null || await this.ConfirmRequested(title, message);
        }

        private async Task CompleteSignInAsync(string pasted)
        {
            if (this.IsBusy == true)
            {
                return;
            }
            this.IsBusy = true;
            this.ErrorText = "";
            var signedIn = false;
            try
            {
                var result = await this._Session.SignInAsync(this._Provider, pasted, this._Lifetime.Token);
                if (result.SignedIn == false)
                {
                    this.ErrorText = result.Error;
                }
                else
                {
                    this.PastedCode = "";

                    // Whatever is on screen belongs to the previous account, if there was one.
                    // The new account's saved library, if it has one, goes up straight away.
                    this.CancelProgress();
                    this.ClearLibrary();
                    this.LoadFromCache();
                    this.NotifySignInChanged();
                    signedIn = true;
                    await this.RefreshLibraryCoreAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                this.IsBusy = false;
            }

            // A new sign-in reads the achievement counts without being asked: it is the one
            // moment the user has just chosen this account, and it only covers games whose
            // count is not already saved, so signing back in to a known account is cheap.
            // Not after a failed library load, which would only report "nothing to read".
            if (signedIn == true && this._Games.Count > 0)
            {
                Task progress = this.LoadProgressAsync();
                GC.KeepAlive(progress);
            }
        }

        [RelayCommand]
        private async Task SignOutAsync()
        {
            var signingOut = this._Session.ConfirmSignOut(this._Provider);
            if (await this.ConfirmAsync(signingOut.Title, signingOut.Message) == false)
            {
                return;
            }

            this.ForgetAccount();
            this.StatusText = $"Signed out of {this.DisplayName}.";
        }

        /// <summary>Called when a request finds the session no longer valid.</summary>
        private void OnSessionExpired()
        {
            this.ForgetAccount();
            this.ErrorText = this._Session.SessionExpiredMessage(this._Provider);
            this.StatusText = "Signed out.";
        }

        /// <summary>
        /// Drops the session and stops showing the account's library. What is saved for
        /// the account stays in the database under its account id, so signing back in
        /// finds it; it is never shown to another account, which looks under its own id.
        /// A dead session is forgotten too, or the sign-in panel would never come back.
        /// </summary>
        private void ForgetAccount()
        {
            this.CancelProgress();
            this._Session.SignOut(this._Provider);
            this.ClearLibrary();
        }

        /// <summary>Stops showing the account's library. Neither the session nor the saved rows are touched.</summary>
        private void ClearLibrary()
        {
            this._Games.Clear();
            this.FilteredGames.Clear();
            this.SelectedGame = null;

            this.RefreshCollectionCounts();
            this.ErrorText = "";
            this.NotifySignInChanged();
        }

        #endregion

        #region Library

        public bool CanRefresh => this.IsSignedIn == true && this.IsBusy == false && this.IsLoadingProgress == false;

        private void LoadFromCache()
        {
            foreach (var row in this._Library.LoadCached(this._Provider))
            {
                StoreGameInfo info = new(this.Id, row.Id, row.Key)
                {
                    Name = row.Name,
                    ImageUrl = row.ImageUrl,
                };
                if (row.HasProgress == true && row.AchievementsTotal >= 0 && row.AchievementsEarned >= 0)
                {
                    info.Progress = new StoreProgress(row.AchievementsEarned, row.AchievementsTotal);
                }
                this._Games[row.Id] = info;
            }

            if (this._Games.Count > 0)
            {
                this.RefreshCollectionCounts();
                this.RefreshGames();
                this.StatusText = $"Showing {this._Games.Count} cached games. Refreshing...";
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (this.IsBusy == true || this.IsSignedIn == false)
            {
                return;
            }
            this.IsBusy = true;
            try
            {
                await this.RefreshLibraryCoreAsync();
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        private async Task RefreshLibraryAsync()
        {
            if (this.IsSignedIn == false)
            {
                return;
            }
            this.IsBusy = true;
            try
            {
                await this.RefreshLibraryCoreAsync();
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        /// <summary>
        /// Re-reads the owned list and merges it into what is already on screen. A
        /// failure leaves the cached list in place and says why: a store being down
        /// is not a reason to blank the window.
        /// </summary>
        private async Task RefreshLibraryCoreAsync()
        {
            this.ErrorText = "";
            this.StatusText = $"Loading your {this.DisplayName} library...";
            this._Sink.Begin($"Loading your {this.DisplayName} library...");
            try
            {
                var refresh = await this._Library.RefreshAsync(this._Provider, this._Lifetime.Token);
                if (refresh.SessionExpired == true)
                {
                    this.OnSessionExpired();
                }
                else if (refresh.Failure != null)
                {
                    this.StatusText = refresh.Failure;
                }
                else
                {
                    this.Merge(refresh.Library);
                    this.RefreshCollectionCounts();
                    this.RefreshGames();
                    await this.SaveLibraryAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                this._Sink.End();
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

                if (this._Games.TryGetValue(game.Id, out var info) == true && info.Key == game.Key)
                {
                    info.Name = game.Title;
                    if (info.ImageUrl != game.ImageUrl)
                    {
                        info.ImageUrl = game.ImageUrl;
                        info.Logo = null;
                    }
                    continue;
                }

                this._Games[game.Id] = new(this.Id, game.Id, game.Key)
                {
                    Name = game.Title,
                    ImageUrl = game.ImageUrl,
                };
            }

            foreach (var gone in this._Games.Keys.Where(id => owned.Contains(id) == false).ToList())
            {
                this._Games.Remove(gone);
            }
        }

        private async Task SaveLibraryAsync()
        {
            var rows = this._Games.Values.Select(info => new CachedStoreGame()
            {
                Id = info.Id,
                Key = info.Key,
                Name = info.Name,
                ImageUrl = info.ImageUrl,
            }).ToList();

            var failure = await this._Library.SaveLibraryAsync(this._Provider, rows);
            if (failure != null)
            {
                this.ErrorRaised?.Invoke(failure);
            }
        }

        #endregion

        #region Searching, collections and sorting

        private string _SearchText = "";
        public string SearchText
        {
            get => this._SearchText;
            set
            {
                if (this.SetProperty(ref this._SearchText, value ?? "") == true)
                {
                    this.RefreshGames();
                }
            }
        }

        private GameCollection _SelectedCollection = GameCollection.All;
        public GameCollection SelectedCollection
        {
            get => this._SelectedCollection;
            set
            {
                if (this.SetProperty(ref this._SelectedCollection, value) == false)
                {
                    return;
                }
                foreach (var name in new[]
                {
                    nameof(this.CollectionTitle), nameof(this.IsAllSelected),
                    nameof(this.IsWithAchievementsSelected), nameof(this.IsPerfectSelected),
                })
                {
                    this.OnPropertyChanged(name);
                }
                this.RefreshGames();
            }
        }

        public bool IsAllSelected => this.SelectedCollection == GameCollection.All;
        public bool IsWithAchievementsSelected => this.SelectedCollection == GameCollection.WithAchievements;
        public bool IsPerfectSelected => this.SelectedCollection == GameCollection.Perfect;

        public string CollectionTitle => this.SelectedCollection switch
        {
            GameCollection.WithAchievements => "Has achievements",
            GameCollection.Perfect => "Perfect games",
            _ => "All games",
        };

        [RelayCommand]
        private void SelectCollection(GameCollection collection)
        {
            this.SelectedCollection = collection;
        }

        public int AllCount => this._Games.Count;
        public int WithAchievementsCount => this._Games.Values.Count(g => g.HasCompletion == true);
        public int PerfectCount => this._Games.Values.Count(g => g.IsPerfect == true);

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
                foreach (var info in this._Games.Values)
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
                    ? $"{this._Games.Count:N0} owned"
                    : $"{this._Games.Count:N0} owned · {earned:N0} of {total:N0} achievements in {covered:N0} games read";
            }
        }

        private void RefreshCollectionCounts()
        {
            foreach (var name in new[]
            {
                nameof(this.AllCount), nameof(this.WithAchievementsCount),
                nameof(this.PerfectCount), nameof(this.LibrarySummaryText),
            })
            {
                this.OnPropertyChanged(name);
            }
        }

        private GameSortField _SortField = GameSortField.Name;
        public GameSortField SortField
        {
            get => this._SortField;
            set
            {
                if (this.SetProperty(ref this._SortField, value) == true)
                {
                    this.OnPropertyChanged(nameof(this.SortDescriptionText));
                    this.RefreshGames();
                }
            }
        }

        private bool _SortDescending;
        public bool SortDescending
        {
            get => this._SortDescending;
            set
            {
                if (this.SetProperty(ref this._SortDescending, value) == true)
                {
                    this.OnPropertyChanged(nameof(this.SortDescriptionText));
                    this.RefreshGames();
                }
            }
        }

        public string SortDescriptionText => this.SortField == GameSortField.Completion ? "Completion" : "Name";

        [RelayCommand]
        private void SortBy(GameSortField field)
        {
            if (this.SortField == field)
            {
                this.SortDescending = this.SortDescending == false;
                return;
            }
            // Completion is far more useful highest-first.
            this._SortDescending = field != GameSortField.Name;
            this.OnPropertyChanged(nameof(this.SortDescending));
            this.SortField = field;
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            this.SortDescending = this.SortDescending == false;
        }

        private IEnumerable<StoreGameInfo> ApplySort(IEnumerable<StoreGameInfo> games)
        {
            // Name is the tie-breaker, so the order is stable when many games have no
            // completion yet; those sort to the far end either way rather than as 0%.
            return this.SortField == GameSortField.Completion
                ? this.SortDescending == true
                    ? games.OrderByDescending(g => g.Completion ?? -1.0).ThenBy(g => g.Name)
                    : games.OrderBy(g => g.Completion ?? 2.0).ThenBy(g => g.Name)
                : this.SortDescending == true
                    ? games.OrderByDescending(g => g.Name)
                    : games.OrderBy(g => g.Name);
        }

        private void RefreshGames()
        {
            var nameSearch = this.SearchText.Length > 0 ? this.SearchText : null;
            var keep = this.SelectedGame;

            this.FilteredGames.Clear();
            foreach (var info in this.ApplySort(this._Games.Values))
            {
                if (nameSearch != null &&
                    info.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                var inCollection = this.SelectedCollection switch
                {
                    GameCollection.WithAchievements => info.HasCompletion,
                    GameCollection.Perfect => info.IsPerfect,
                    _ => true,
                };
                if (inCollection == false)
                {
                    continue;
                }
                this.FilteredGames.Add(info);
            }

            this.StatusText = $"Displaying {this.FilteredGames.Count} games. Total {this._Games.Count} games.";

            // Keep the selection across a refresh; losing it every time progress lands
            // would be maddening.
            this.SelectedGame = keep != null && this.FilteredGames.Contains(keep) == true
                ? keep
                : this.FilteredGames.FirstOrDefault();

            foreach (var info in this.FilteredGames)
            {
                this.EnqueueLogo(info);
            }
        }

        #endregion

        #region Logos

        private void EnqueueLogo(StoreGameInfo info)
        {
            if (info.Logo != null || string.IsNullOrEmpty(info.ImageUrl) == true)
            {
                return;
            }
            if (this._LogosAttempted.Add(info.Id + "|" + info.ImageUrl) == false)
            {
                return;
            }

            if (this._LogosQueued == 0)
            {
                this._Sink.Begin("Loading game icons...");
            }
            this._LogosQueued++;
            this._LogoQueue.Writer.TryWrite(info);
        }

        /// <summary>
        /// One of <see cref="LogoWorkers"/> consumers. They all resume on the UI thread after
        /// each await, so the counters are only touched from it. Every way of finishing an
        /// item (cached, downloaded, failed) advances the bar by one.
        /// </summary>
        private async Task RunLogoWorkerAsync()
        {
            var token = this._Lifetime.Token;
            try
            {
                while (await this._LogoQueue.Reader.WaitToReadAsync(token))
                {
                    while (this._LogoQueue.Reader.TryRead(out var info) == true)
                    {
                        var logo = await this._Images.LoadAsync(this.Id, info.ImageUrl, token);
                        if (logo != null)
                        {
                            info.Logo = logo;
                        }

                        // Drained when everything queued has been finished. Not
                        // Reader.Count: a single-reader channel cannot count, and asking
                        // throws, which silently ended this worker after one image.
                        this._LogosCompleted++;
                        if (this._LogosCompleted == this._LogosQueued)
                        {
                            this._Sink.End();
                            this._LogosQueued = 0;
                            this._LogosCompleted = 0;
                            continue;
                        }
                        this._Sink.Report(
                            $"Loading game icons, {this._LogosCompleted:N0} of {this._LogosQueued:N0}...",
                            this._LogosCompleted,
                            this._LogosQueued);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The window is closing.
            }
        }

        #endregion

        #region Achievements

        [RelayCommand]
        private void OpenSelected()
        {
            var game = this.SelectedGame;
            if (game == null)
            {
                return;
            }

            this.AchievementsRequested?.Invoke(new StoreAchievementsViewModel(
                this._Provider,
                this._Achievements,
                game,
                this._Images,
                (earned, total) => this.ApplyProgress(game, earned, total)));
        }

        /// <summary>
        /// Records what a game's achievements came to, from the viewer or from
        /// <see cref="LoadProgressCommand"/>, so the meter fills in without another trip
        /// to the store.
        /// </summary>
        private void ApplyProgress(StoreGameInfo game, int earned, int total)
        {
            game.Progress = new StoreProgress(earned, total);
            this.RefreshCollectionCounts();
            this.SaveProgress(new[] { (game.Id, earned, total) });
        }

        private void SaveProgress(IReadOnlyCollection<(string Id, int Earned, int Total)> results)
        {
            // Fire and forget on purpose: the numbers are already on screen, and a cache
            // that cannot be written costs only a reload next time.
            _ = Task.Run(() => this._Library.SaveProgress(this._Provider, results));
        }

        private bool _IsLoadingProgress;
        public bool IsLoadingProgress
        {
            get => this._IsLoadingProgress;
            private set
            {
                if (this.SetProperty(ref this._IsLoadingProgress, value) == true)
                {
                    this.OnPropertyChanged(nameof(this.CanLoadProgress));
                    this.OnPropertyChanged(nameof(this.CanRefresh));
                }
            }
        }

        public bool CanLoadProgress => this.IsSignedIn == true && this.IsBusy == false && this.IsLoadingProgress == false;

        /// <summary>
        /// Reads the achievements of every game whose progress is not known yet, a few at
        /// a time, and keeps the results. It costs requests for every one of them, so it
        /// runs only when asked, can be cancelled, and keeps what it has when it is.
        /// </summary>
        [RelayCommand]
        private async Task LoadProgressAsync()
        {
            if (this.CanLoadProgress == false)
            {
                return;
            }

            var games = this._Games.Values.Where(g => g.Progress.HasValue == false).OrderBy(g => g.Name).ToList();
            var total = games.Count;
            if (total == 0)
            {
                this.StatusText = "Progress is already loaded for every game.";
                return;
            }

            this._ProgressRun = CancellationTokenSource.CreateLinkedTokenSource(this._Lifetime.Token);
            var token = this._ProgressRun.Token;
            this.IsLoadingProgress = true;
            this.ErrorText = "";

            this._Sink.Begin($"Loading {this.DisplayName} progress...");
            this._Sink.Report($"Loading {this.DisplayName} progress, 0 of {total:N0}...", 0, total);

            StoreProgressRun result = null;
            try
            {
                result = await this._Library.LoadProgressAsync(
                    this._Provider,
                    games.Select(g => (g.Id, g.Key)).ToList(),
                    (id, earned, count) =>
                    {
                        if (this._Games.TryGetValue(id, out var game) == true)
                        {
                            game.Progress = new StoreProgress(earned, count);
                        }
                    },
                    finished => this._Sink.Report(
                        $"Loading {this.DisplayName} progress, {finished:N0} of {total:N0}...", finished, total),
                    token);
            }
            finally
            {
                this._Sink.End();
                this._ProgressRun.Dispose();
                this._ProgressRun = null;
                this.IsLoadingProgress = false;

                this.RefreshCollectionCounts();
                this.RefreshGames();
            }

            if (result.SessionExpired == true)
            {
                this.OnSessionExpired();
                return;
            }

            var summary = result.Cancelled
                ? $"Stopped after {result.Done:N0} of {total:N0} games."
                : $"Read the achievements of {result.Done:N0} games.";
            if (result.Failed > 0)
            {
                summary += $" {result.Failed:N0} could not be read; run it again to retry them.";
            }
            this.StatusText = summary;
        }

        [RelayCommand]
        private void CancelProgress()
        {
            this._ProgressRun?.Cancel();
        }

        #endregion

        public void Shutdown()
        {
            this._Lifetime.Cancel();
            this._LogoQueue.Writer.TryComplete();
            // The provider belongs to the container, which disposes it.
        }
    }

    /// <summary>
    /// What the in-application sign-in page ended with: the text for the provider, or
    /// nothing because the user closed it, or <see cref="Unavailable"/> because the
    /// browser could not start and the user should be offered their own.
    /// </summary>
    internal readonly record struct BrowserSignInResult(string Pasted, bool Unavailable);
}
