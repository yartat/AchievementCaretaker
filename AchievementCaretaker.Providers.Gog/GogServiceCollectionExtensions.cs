/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on GOG Achievements Manager by Timendum
 * https://github.com/timendum/gog_achievements
 * https://github.com/timendum/gog-achievements-gui
 *
 * It reimplements in C# the calls those tools make to GOG's services: listing, and
 * unlocking and re-locking. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.Providers.Gog;

public static class GogServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GOG provider, with the named <c>HttpClient</c> it asks the factory for,
    /// as an <see cref="IStoreProvider"/> and as an <see cref="IStoreAccount"/>: two roles of
    /// the one object, so the account signed in is the one the provider reads for.
    /// </summary>
    /// <param name="homeDirectory">Where the session file lives: <c>gog.json</c> in it.</param>
    public static IServiceCollection AddGogProvider(this IServiceCollection services, string homeDirectory)
    {
        services.AddSessionStore();
        services.AddHttpClient(GogProvider.HttpClientName, GogProvider.ConfigureHttpClient);
        services.AddSingleton(sp => new GogProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISessionStore>(),
            Path.Combine(homeDirectory, "gog.json")));
        services.AddSingleton<IStoreProvider>(sp => sp.GetRequiredService<GogProvider>());
        services.AddSingleton<IStoreAccount>(sp => sp.GetRequiredService<GogProvider>());
        return services;
    }
}
