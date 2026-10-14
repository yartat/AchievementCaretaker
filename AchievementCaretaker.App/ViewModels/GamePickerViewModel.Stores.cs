/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.App.ViewModels
{
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

        private StoreLibraryViewModel _SelectedStoreLibrary;

        /// <summary>The store on screen, or null for Steam.</summary>
        public StoreLibraryViewModel SelectedStoreLibrary
        {
            get => this._SelectedStoreLibrary;
            private set
            {
                var previous = this._SelectedStoreLibrary;
                if (this.SetProperty(ref this._SelectedStoreLibrary, value) == false)
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
                    nameof(this.IsSteamSelected), nameof(this.IsStoreSelected),
                    nameof(this.ShowSteamTiles), nameof(this.ShowSteamContent),
                    nameof(this.ActiveStatusText), nameof(this.HeaderTitle), nameof(this.HeaderSummary),
                })
                {
                    this.OnPropertyChanged(name);
                }
            }
        }

        public bool IsSteamSelected => this.SelectedStoreLibrary == null;

        public bool IsStoreSelected => this.SelectedStoreLibrary != null;

        // The Steam tile and row views are shown only for Steam, and only in the chosen
        // mode; the store library draws its own.
        public bool ShowSteamTiles => this.IsSteamSelected == true && this.IsTileView == true;

        public bool ShowSteamContent => this.IsSteamSelected == true && this.IsContentView == true;

        // What the status bar and the header show: the selected source's.
        public string ActiveStatusText => this.SelectedStoreLibrary?.StatusText ?? this.StatusText;

        public string HeaderTitle => this.SelectedStoreLibrary?.CollectionTitle ?? this.CollectionTitle;

        public string HeaderSummary => this.SelectedStoreLibrary?.LibrarySummaryText ?? this.LibrarySummaryText;

        partial void OnStatusTextChanged(string value)
        {
            this.OnPropertyChanged(nameof(this.ActiveStatusText));
        }

        /// <summary>Raised when a store game's achievements should be shown; the window owns the window.</summary>
        public event Action<StoreAchievementsViewModel> StoreAchievementsRequested;

        [RelayCommand]
        private void SelectSteam()
        {
            this.SelectedStoreLibrary = null;
        }

        [RelayCommand]
        private void SelectStore(StoreLibraryViewModel store)
        {
            this.SelectedStoreLibrary = store;
        }

        private void CreateStoreLibraries()
        {
            // One image cache for all of them: it follows the icon cache's directory
            // as it moves, so it is built from that rather than from a path.
            StoreImageCache images = new(this._IconCache);

            List<StoreLibraryViewModel> stores = new();
            foreach (var provider in StoreProviders.CreateAll(AppSettings.HomeDirectory))
            {
                // The database can be replaced by a cache move, so hand over how to
                // reach it rather than the instance.
                StoreLibraryViewModel store = new(provider, () => this._Cache, images, this)
                {
                    IsContentView = this.IsContentView,
                };
                store.ErrorRaised += message => this.ErrorRaised?.Invoke(message);
                store.AchievementsRequested += viewer => this.StoreAchievementsRequested?.Invoke(viewer);
                store.PropertyChanged += this.OnStorePropertyChanged;
                stores.Add(store);
            }

            this.StoreLibraries = stores;
            this.OnPropertyChanged(nameof(this.StoreLibraries));
        }

        private void OnStorePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ReferenceEquals(sender, this.SelectedStoreLibrary) == false)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(StoreLibraryViewModel.StatusText):
                    this.OnPropertyChanged(nameof(this.ActiveStatusText));
                    break;
                case nameof(StoreLibraryViewModel.CollectionTitle):
                    this.OnPropertyChanged(nameof(this.HeaderTitle));
                    break;
                case nameof(StoreLibraryViewModel.LibrarySummaryText):
                    this.OnPropertyChanged(nameof(this.HeaderSummary));
                    break;
            }
        }

        private void OnViewModeChanged()
        {
            this.OnPropertyChanged(nameof(this.ShowSteamTiles));
            this.OnPropertyChanged(nameof(this.ShowSteamContent));
            foreach (var store in this.StoreLibraries)
            {
                store.IsContentView = this.IsContentView;
            }
        }

        private void ForwardSearchToStores(string text)
        {
            foreach (var store in this.StoreLibraries)
            {
                store.SearchText = text;
            }
        }

        private void ShutdownStoreLibraries()
        {
            foreach (var store in this.StoreLibraries)
            {
                store.Shutdown();
            }
        }

        void IProgressSink.Begin(string text)
        {
            this.BeginProgress(text);
        }

        void IProgressSink.Report(string text, int completed, int total)
        {
            this.ReportProgress(text, completed, total);
        }

        void IProgressSink.End()
        {
            this.EndProgress();
        }
    }
}
