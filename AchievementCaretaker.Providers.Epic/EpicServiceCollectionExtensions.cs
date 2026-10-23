/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * Written from the endpoints that the open-source launchers Legendary
 * (https://github.com/derrod/legendary) and Heroic document for Epic Games' services;
 * see docs/EPIC-RESEARCH.md.
 *
 * See LICENSE in the repository root.
 */

using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.Providers.Epic;

public static class EpicServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Epic provider as an <see cref="IStoreProvider"/>, with the named
    /// <c>HttpClient</c> it asks the factory for.
    /// </summary>
    /// <param name="homeDirectory">Where the session file lives: <c>epic.json</c> in it.</param>
    public static IServiceCollection AddEpicProvider(this IServiceCollection services, string homeDirectory)
    {
        services.AddSessionStore();
        services.AddHttpClient(EpicProvider.HttpClientName, EpicProvider.ConfigureHttpClient);
        services.AddSingleton<IStoreProvider>(sp => new EpicProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISessionStore>(),
            Path.Combine(homeDirectory, "epic.json")));
        return services;
    }
}
