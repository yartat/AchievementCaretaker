/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Services
{
    /// <summary>The <see cref="IStoreAchievementsService"/>. Stateless.</summary>
    public sealed class StoreAchievementsService : IStoreAchievementsService
    {
        public bool CanModify(IStoreProvider provider) => provider is IWritableStoreProvider;

        public async Task<StoreAchievementsResult> LoadAsync(
            IStoreProvider provider, string gameKey, CancellationToken cancellationToken)
        {
            try
            {
                var list = await provider.GetAchievementsAsync(gameKey, "en", cancellationToken);
                return new StoreAchievementsResult(list, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return new StoreAchievementsResult(Array.Empty<StoreAchievement>(), Describe(provider, e));
            }
        }

        public async Task<string> CommitAsync(
            IStoreProvider provider,
            string gameKey,
            IReadOnlyCollection<KeyValuePair<string, bool>> changes,
            CancellationToken cancellationToken)
        {
            if (provider is not IWritableStoreProvider writer)
            {
                return $"{provider.DisplayName} achievements cannot be changed.";
            }

            try
            {
                await writer.SetAchievementsAsync(gameKey, changes, cancellationToken);
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return Describe(provider, e);
            }
        }

        private static string Describe(IStoreProvider provider, Exception e) =>
            e is ProviderException p
                ? p.ErrorCode == "login_required"
                    ? $"You are signed out of {provider.DisplayName}. Sign in again from the library."
                    : p.Message
                : $"Could not reach {provider.DisplayName}: {e.Message}";
    }
}
