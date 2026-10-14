/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.IO;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;

namespace AchievementCaretaker.Providers
{
    /// <summary>
    /// The providers that exist. Adding a store means adding it here, and nothing
    /// else has to change to see it.
    /// </summary>
    public static class StoreProviders
    {
        /// <param name="homeDirectory">
        /// Where session files live: <c>~/.sac</c>. Each provider keeps one, named
        /// after its <see cref="IStoreProvider.Id"/>.
        /// </param>
        public static IReadOnlyList<IStoreProvider> CreateAll(string homeDirectory)
        {
            return new IStoreProvider[]
            {
                new EpicProvider(Path.Combine(homeDirectory, "epic.json")),
                new GogProvider(Path.Combine(homeDirectory, "gog.json")),
            };
        }
    }
}
