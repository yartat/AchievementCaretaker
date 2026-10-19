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
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.App.Services
{
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
    /// Adding a store means registering its provider here as an <see cref="IStoreProvider"/>
    /// and nothing else: everything that lists the stores asks for
    /// <c>IEnumerable&lt;IStoreProvider&gt;</c>.
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

            // One cache for the process. It can be moved from the settings dialog, and it can fail
            // to open: that is a cache that says so, not an exception at start.
            services.AddSingleton<ILibraryCache>(_ => new LibraryCache(AppSettings.Load().DatabasePath));

            services.AddSingleton<IStoreAchievementsService, StoreAchievementsService>();
            services.AddSingleton<IStoreLibraryService, StoreLibraryService>();
            services.AddSingleton<IStoreSessionService, StoreSessionService>();

            services.AddScoped(_ => new SteamConnector());
            services.AddScoped<GamePickerViewModel>();

            return services;
        }
    }
}
