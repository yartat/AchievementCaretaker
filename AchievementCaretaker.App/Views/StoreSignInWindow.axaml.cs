/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using AchievementCaretaker.App.ViewModels;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.App.Views
{
    /// <summary>
    /// A store's own sign-in page in a browser inside the application, so the user does
    /// not have to copy a code back. Caretaker only watches the addresses the page
    /// moves through: it never reads the password field, and the browser keeps nothing
    /// afterwards (it runs in private mode where the platform has one).
    /// </summary>
    public partial class StoreSignInWindow : Window
    {
        /// <summary>
        /// How long the browser gets to start before it is called unavailable. A missing
        /// WebView2 runtime or WebKitGTK reports nothing; the page simply never loads.
        /// </summary>
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(8);

        private readonly IStoreProvider _Provider;
        private bool _BrowserStarted;
        private bool _Finished;

        /// <summary>Only for the XAML loader; the window needs a provider.</summary>
        public StoreSignInWindow()
        {
            this.InitializeComponent();
        }

        internal StoreSignInWindow(IStoreProvider provider)
        {
            this._Provider = provider;
            this.InitializeComponent();

            this.Title = $"Sign in to {provider.DisplayName}";
            this.Note.Text = "Caretaker never sees your password. Closing this window signs nothing in.";

            this.Browser.EnvironmentRequested += OnEnvironmentRequested;
            this.Browser.AdapterCreated += (_, _) => this._BrowserStarted = true;
            this.Browser.NavigationStarted += this.OnNavigationStarted;
            this.Browser.NavigationCompleted += this.OnNavigationCompleted;
            this.Opened += this.OnOpened;
        }

        /// <summary>Set when the browser could not start and the caller should fall back.</summary>
        internal bool BrowserUnavailable { get; private set; }

        /// <summary>
        /// Shows the sign-in page and returns what the page ended on, ready for
        /// <see cref="IStoreProvider.SignInAsync"/>.
        /// </summary>
        internal static async System.Threading.Tasks.Task<BrowserSignInResult> RunAsync(Window owner, IStoreProvider provider)
        {
            StoreSignInWindow window = new(provider);
            var pasted = await window.ShowDialog<string>(owner);
            return new(pasted, window.BrowserUnavailable);
        }

        private static void OnEnvironmentRequested(object sender, WebViewEnvironmentRequestedEventArgs e)
        {
            // Nothing from this window may outlive it: a second account has to be able
            // to sign in, and the page must not log the previous one straight back in.
            switch (e)
            {
                case WindowsWebView2EnvironmentRequestedEventArgs windows:
                    windows.IsInPrivateModeEnabled = true;
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    gtk.EphemeralDataManager = true;
                    break;
                case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                    apple.NonPersistentDataStore = true;
                    break;
            }
        }

        private void OnOpened(object sender, EventArgs e)
        {
            this.Browser.Source = new Uri(this._Provider.SignInUrl);

            DispatcherTimer.RunOnce(() =>
            {
                if (this._BrowserStarted == false && this._Finished == false)
                {
                    this.ShowUnavailable();
                }
            }, StartTimeout);
        }

        private void OnNavigationStarted(object sender, WebViewNavigationStartingEventArgs e)
        {
            if (this._Provider.GetSignInResultKind(e.Request) == SignInResultKind.Address)
            {
                // The redirect target is not a page worth loading: the address is the answer.
                e.Cancel = true;
                this.Finish(e.Request.ToString());
            }
        }

        private async void OnNavigationCompleted(object sender, WebViewNavigationCompletedEventArgs e)
        {
            this._BrowserStarted = true;
            if (this._Finished == true || e.IsSuccess == false)
            {
                return;
            }

            switch (this._Provider.GetSignInResultKind(e.Request))
            {
                case SignInResultKind.Address:
                    this.Finish(e.Request.ToString());
                    break;

                case SignInResultKind.PageText:
                    try
                    {
                        this.Finish(DecodeScriptResult(await this.Browser.InvokeScript("document.body.innerText")));
                    }
                    catch (Exception)
                    {
                        this.ShowUnavailable();
                    }
                    break;
            }
        }

        /// <summary>
        /// A script's result may come back as a JSON string literal, quotes and
        /// escapes included, which would hide the document the provider looks for.
        /// </summary>
        private static string DecodeScriptResult(string result)
        {
            if (string.IsNullOrEmpty(result) == false && result.StartsWith('"') == true)
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(result);
                }
                catch (JsonException)
                {
                }
            }
            return result;
        }

        private void Finish(string result)
        {
            if (this._Finished == true)
            {
                return;
            }
            this._Finished = true;
            this.Close(result);
        }

        private void ShowUnavailable()
        {
            this.BrowserUnavailable = true;
            this.UnavailableText.Text =
                "The browser built into Caretaker could not start here. " +
                "On Windows it needs the WebView2 runtime, on Linux WebKitGTK or WPE. " +
                "You can sign in with your own browser instead and paste the result back.";
            this.Unavailable.IsVisible = true;
        }

        private void OnUseOwnBrowserClicked(object sender, RoutedEventArgs e)
        {
            this._Finished = true;
            this.Close(null);
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            this._Finished = true;
            this.BrowserUnavailable = false;
            this.Close(null);
        }
    }
}
