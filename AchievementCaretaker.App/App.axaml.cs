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
using AchievementCaretaker.App.Services;
using AchievementCaretaker.App.ViewModels;
using AchievementCaretaker.App.Views;
using AchievementCaretaker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.App;

/// <summary>The application: its styles and resources, and the choice of the first window.</summary>
public partial class App : Application
{
    /// <summary>
    /// The root of the dependency graph. Validated when built, so a singleton that
    /// reaches for something scoped fails at start rather than on first use.
    /// </summary>
    private ServiceProvider? _services;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = AppThemes.ToVariant(AppSettings.Load().Theme);

        var services = new ServiceCollection()
            .AddCaretaker()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _services = services;

        // The library database is prepared once, here, before any window asks for it.
        services.MigrateLibraryDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow(services, desktop.Args);
            desktop.ShutdownRequested += OnShutdownRequested;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Chooses the window: the library, or an error window when the command line or the place
    /// it is run from is wrong. Avalonia has no equivalent of a message box shown before the
    /// application loop starts, so the error is a window of its own.
    /// </summary>
    /// <remarks>
    /// The library does not do the Steam handshake here. It has other sources than Steam, so it
    /// opens at once and its <see cref="SteamConnector"/> looks for Steam in the background.
    /// A game's achievements open in a window of their own, served by a helper process (see
    /// <c>SteamProvider</c>); an application id on the command line, which used to open the
    /// editor for that game, is accepted and ignored.
    /// </remarks>
    private static Window CreateMainWindow(ServiceProvider services, string[]? args)
    {
        if (Program.IsRunningFromSteamDirectory() == true)
        {
            return MessageWindow.CreateError("This tool declines to being run from the Steam directory.");
        }

        if (Program.TryParseAppId(args, out _) == false)
        {
            return MessageWindow.CreateError(
                "Could not parse application ID from command line argument.");
        }

        // One scope for the library window: its view model comes out of it, and it
        // goes when the window closes.
        var scope = services.CreateScope();
        GamePickerWindow library = new() { DataContext = scope.ServiceProvider.GetRequiredService<GamePickerViewModel>() };
        library.Closed += (_, _) => scope.Dispose();
        return library;
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Disposes the store providers, which the container owns.
        _services?.Dispose();
        _services = null;
    }
}
