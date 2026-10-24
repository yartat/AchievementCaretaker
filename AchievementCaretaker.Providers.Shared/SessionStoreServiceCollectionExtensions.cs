/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AchievementCaretaker.Providers;

public static class SessionStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the session store for this operating system. Safe to call from every
    /// provider's registration: the first one wins.
    /// </summary>
    public static IServiceCollection AddSessionStore(this IServiceCollection services)
    {
        // The only place that asks which system this is. Everything after this works with
        // an ISessionStore and does not know.
        if (OperatingSystem.IsWindows())
        {
            services.TryAddSingleton<ISessionStore, DpapiSessionStore>();
        }
        else
        {
            services.TryAddSingleton<ISessionStore, PlainSessionStore>();
        }
        return services;
    }
}
