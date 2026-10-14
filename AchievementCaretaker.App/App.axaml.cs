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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.ViewModels;
using AchievementCaretaker.App.Views;

namespace AchievementCaretaker.App
{
    public partial class App : Application
    {
        private SteamApi.Client _SteamClient;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Both windows run in their own process, so each one reads the
            // stored theme for itself. A change made in Settings reaches an
            // editor window that is already open only when it is relaunched.
            this.RequestedThemeVariant = AppThemes.ToVariant(AppSettings.Load().Theme);

            if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = this.CreateMainWindow(desktop.Args);
                desktop.ShutdownRequested += this.OnShutdownRequested;
            }
            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Does the Steam handshake and chooses the window: the library when
        /// there is no application id on the command line, the achievement
        /// editor when there is, and an error window when either step fails.
        /// Avalonia has no equivalent of a message box shown before the
        /// application loop starts, so the error is a window of its own.
        /// </summary>
        private Window CreateMainWindow(string[] args)
        {
            if (SteamApi.SteamPlatform.IsArchitectureSupported == false)
            {
                return MessageWindow.CreateError(SteamApi.SteamPlatform.DescribeUnsupportedArchitecture());
            }

            if (Program.IsRunningFromSteamDirectory() == true)
            {
                return MessageWindow.CreateError("This tool declines to being run from the Steam directory.");
            }

            if (Program.TryParseAppId(args, out var appId) == false)
            {
                return MessageWindow.CreateError(
                    "Could not parse application ID from command line argument.");
            }

            SteamApi.Client client = new();
            try
            {
                client.Initialize(appId);
            }
            catch (SteamApi.ClientInitializeException e)
            {
                client.Dispose();
                return MessageWindow.CreateError(DescribeInitializeFailure(e, appId));
            }
            catch (DllNotFoundException)
            {
                client.Dispose();
                return MessageWindow.CreateError("You've caused an exceptional error!");
            }

            this._SteamClient = client;

            // Application id 0 is not a game; it is how the library window asks
            // Steam for an ownership-wide session.
            return appId == 0
                ? new GamePickerWindow() { DataContext = new GamePickerViewModel(client) }
                : new ManagerWindow() { DataContext = new ManagerViewModel(appId, client) };
        }

        private static string DescribeInitializeFailure(SteamApi.ClientInitializeException e, long appId)
        {
            const string Prefix = "Steam is not running. Please start Steam then run this tool again.";

            // Family Share only locks a specific game, so the hint is only
            // worth showing when one was asked for.
            if (appId != 0 && e.Failure == SteamApi.ClientInitializeFailure.ConnectToGlobalUser)
            {
                return
                    Prefix + "\n\n" +
                    "If you have the game through Family Share, the game may be locked due to\n" +
                    "the Family Share account actively playing a game.\n\n" +
                    "(" + e.Message + ")";
            }

            return string.IsNullOrEmpty(e.Message) == false
                ? Prefix + "\n\n(" + e.Message + ")"
                : Prefix;
        }

        private void OnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
        {
            this._SteamClient?.Dispose();
            this._SteamClient = null;
        }
    }
}
