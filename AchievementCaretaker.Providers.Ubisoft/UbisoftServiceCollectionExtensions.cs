/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Net.Http;
using AchievementCaretaker.Providers.Ubisoft.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>Registration of the Ubisoft Connect provider.</summary>
public static class UbisoftServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ubisoft Connect provider as an <see cref="IStoreProvider"/> and as an
    /// <see cref="IStoreAccount"/>: two roles of the one object. Registers nothing off Windows, where
    /// Ubisoft Connect does not run and there is nothing to read, so the library window has no such source.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="homeDirectory">Where Caretaker keeps its files: the icons and archives go under <c>ubisoft</c> in it, and a sign-in is <c>ubisoft.json</c>.</param>
    public static IServiceCollection AddUbisoftProvider(this IServiceCollection services, string homeDirectory)
    {
        // The only place that asks which system this is.
        if (OperatingSystem.IsWindows() == false)
        {
            return services;
        }

        services.AddSessionStore();
        services.AddSingleton<IUbisoftConnector, UbisoftConnector>();
        services.AddHttpClient(UbisoftProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AchievementCaretaker");
        });
        services.AddSingleton(sp => new UbisoftProvider(
            UbisoftLocations.ForCurrentUser(),
            Path.Combine(homeDirectory, "ubisoft", "icons"),
            Path.Combine(homeDirectory, "ubisoft", "archives"),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ISessionStore>(),
            Path.Combine(homeDirectory, "ubisoft.json"),
            sp.GetRequiredService<IUbisoftConnector>()));
        services.AddSingleton<IStoreProvider>(sp => sp.GetRequiredService<UbisoftProvider>());
        services.AddSingleton<IStoreAccount>(sp => sp.GetRequiredService<UbisoftProvider>());
        return services;
    }
}
