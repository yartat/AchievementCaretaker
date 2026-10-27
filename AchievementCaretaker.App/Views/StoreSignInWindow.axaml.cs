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
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.App.Views;

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
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(8);

    // Only the constructor the XAML loader uses leaves it unset, and that window is never shown.
    private readonly IStoreAccount _account = null!;
    private bool _browserStarted;
    private bool _finished;

    /// <summary>Only for the XAML loader; the window needs an account.</summary>
    public StoreSignInWindow()
    {
        InitializeComponent();
    }

    internal StoreSignInWindow(IStoreAccount account)
    {
        _account = account;
        InitializeComponent();

        Title = $"Sign in to {account.DisplayName}";
        Note.Text = "Caretaker never sees your password. Closing this window signs nothing in.";

        Browser.EnvironmentRequested += OnEnvironmentRequested;
        Browser.AdapterCreated += (_, _) => _browserStarted = true;
        Browser.NavigationStarted += OnNavigationStarted;
        Browser.NavigationCompleted += OnNavigationCompleted;
        Opened += OnOpened;
    }

    /// <summary>Set when the browser could not start and the caller should fall back.</summary>
    internal bool BrowserUnavailable { get; private set; }

    /// <summary>
    /// Shows the sign-in page and returns what the page ended on, ready for
    /// <see cref="IStoreAccount.SignInAsync"/>.
    /// </summary>
    internal static async System.Threading.Tasks.Task<BrowserSignInResult> RunAsync(Window owner, IStoreAccount account)
    {
        StoreSignInWindow window = new(account);
        var pasted = await window.ShowDialog<string?>(owner);
        return new(pasted, window.BrowserUnavailable);
    }

    private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
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

    private void OnOpened(object? sender, EventArgs e)
    {
        Browser.Source = new Uri(_account.SignInUrl);

        DispatcherTimer.RunOnce(() =>
        {
            if (_browserStarted == false && _finished == false)
            {
                ShowUnavailable();
            }
        }, _startTimeout);
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_account.GetSignInResultKind(e.Request) == SignInResultKind.Address)
        {
            // The redirect target is not a page worth loading: the address is the answer.
            e.Cancel = true;
            Finish(e.Request?.ToString());
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _browserStarted = true;
        if (_finished == true || e.IsSuccess == false)
        {
            return;
        }

        // A store that leaves its sign-in in the page's storage gets its script run on its own pages only.
        if (_account.SignInScript is { } script && e.Request?.Host == new Uri(_account.SignInUrl).Host)
        {
            try
            {
                await Browser.InvokeScript(script);
            }
            catch (Exception)
            {
                ShowUnavailable();
            }
        }

        switch (_account.GetSignInResultKind(e.Request))
        {
            case SignInResultKind.Address:
                Finish(e.Request?.ToString());
                break;

            case SignInResultKind.PageText:
                try
                {
                    Finish(DecodeScriptResult(await Browser.InvokeScript("document.body.innerText")));
                }
                catch (Exception)
                {
                    ShowUnavailable();
                }
                break;
        }
    }

    /// <summary>
    /// A script's result may come back as a JSON string literal, quotes and
    /// escapes included, which would hide the document the provider looks for.
    /// </summary>
    private static string? DecodeScriptResult(string? result)
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

    private void Finish(string? result)
    {
        if (_finished == true)
        {
            return;
        }
        _finished = true;
        Close(result);
    }

    private void ShowUnavailable()
    {
        BrowserUnavailable = true;
        UnavailableText.Text =
            "The browser built into Caretaker could not start here. " +
            "On Windows it needs the WebView2 runtime, on Linux WebKitGTK or WPE. " +
            "You can sign in with your own browser instead and paste the result back.";
        Unavailable.IsVisible = true;
    }

    private void OnUseOwnBrowserClicked(object sender, RoutedEventArgs e)
    {
        _finished = true;
        Close(null);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        _finished = true;
        BrowserUnavailable = false;
        Close(null);
    }
}
