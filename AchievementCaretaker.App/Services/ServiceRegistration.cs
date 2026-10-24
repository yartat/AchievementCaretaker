/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.IO;
using AchievementCaretaker.App.Common;
using AchievementCaretaker.App.ViewModels;
using AchievementCaretaker.Services;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Steam;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.App.Services;

/// <summary>
/// The application's composition root: what exists, and for how long.
/// </summary>
/// <remarks>
/// <para>
/// <b>Singleton</b> is what holds state that belongs to the process: the store providers
/// keep a signed-in session and an <c>HttpClient</c>, so there is one of each. The
/// container disposes them when it is disposed; nothing else should.
/// </para>
/// <para>
/// <b>Scoped</b> is what belongs to one window. A desktop application has no request to
/// scope to, so the unit is the window: the library window gets a scope, resolves its
/// view model from it and disposes it when the window closes.
/// </para>
/// <para>
/// Adding a store means registering its provider here, both as an <see cref="IStoreProvider"/>
/// and as an <see cref="IStoreAccount"/> (two roles of one singleton), and nothing else:
/// everything that lists the stores asks for <c>IEnumerable&lt;IStoreProvider&gt;</c> and
/// <c>IEnumerable&lt;IStoreAccount&gt;</c>.
/// </para>
/// </remarks>
internal static class ServiceRegistration
{
    public static IServiceCollection AddCaretaker(this IServiceCollection services)
    {
        // Each provider keeps one session file in the home directory, named after its id,
        // and asks the HTTP client factory for a client per request.
        // The default client, for the capsule art and the published game list.
        services.AddHttpClient();

        services.AddEpicProvider(AppSettings.HomeDirectory);
        services.AddGogProvider(AppSettings.HomeDirectory);

        // The library database: a scoped connection and repository, made from the path in the
        // settings. It can be moved from the settings dialog, and it can fail to be prepared at
        // start: that is a repository that says so, not an exception.
        services.AddLibraryDatabase(AppSettings.Load().DatabasePath);

        // Steam is not one of the stores in the rail (it is the library itself), so it is
        // registered as itself and not as an IStoreProvider: only the achievements window
        // uses it. The container disposes it, which lets go of any helper still running.
        services.AddSingleton<ISteamWorkerLauncher>(_ => new SteamWorkerProcessLauncher(Program.GetExecutablePath()));
        services.AddSingleton<SteamProvider>();
        services.AddSingleton<ILibraryScanProvider>(provider => provider.GetRequiredService<SteamProvider>());
        services.AddSingleton<ISteamEnvironment, SteamPlatformEnvironment>();
        services.AddSingleton<ISteamLibraryService, SteamLibraryService>();

        services.AddSingleton<IStoreAchievementsService, StoreAchievementsService>();
        // Scoped, because it works through the scoped repository.
        services.AddScoped<IStoreLibraryService, StoreLibraryService>();
        services.AddSingleton<IStoreSessionService, StoreSessionService>();

        services.AddScoped<SteamConnector>();
        services.AddScoped<GamePickerViewModel>();

        return services;
    }
}
