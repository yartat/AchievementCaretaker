/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.Services;

/// <summary>The <see cref="IStoreLibraryService"/>. Holds no state of its own beyond the repository, which is scoped, so this is too.</summary>
public sealed class StoreLibraryService : IStoreLibraryService
{
    // Few enough not to hammer a store, many enough that hundreds of games do not take an hour.
    private const int ProgressWorkers = 4;

    // Saved every so many games, so a run that is cut short keeps nearly all it read.
    private const int ProgressBatch = 25;

    private readonly IGameRepository _cache;

    /// <summary>Makes the service over the library cache.</summary>
    /// <param name="cache">The saved libraries.</param>
    public StoreLibraryService(IGameRepository cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public IReadOnlyList<CachedStoreGame> LoadCached(IStoreAccount account)
    {
        // Nobody signed in, nobody's library to show.
        var accountId = account.AccountId;
        if (accountId == null)
        {
            return Array.Empty<CachedStoreGame>();
        }

        try
        {
            return _cache.LoadStoreGames(account.Id, accountId);
        }
        catch (Exception)
        {
            return Array.Empty<CachedStoreGame>();
        }
    }

    /// <inheritdoc />
    public async Task<StoreLibraryRefresh> RefreshAsync(IStoreProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            var library = await provider.GetLibraryAsync(cancellationToken);
            return new StoreLibraryRefresh(library, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderException e) when (e.ErrorCode == "login_required")
        {
            return new StoreLibraryRefresh(null, true, null);
        }
        catch (ProviderException e)
        {
            return new StoreLibraryRefresh(null, false, $"Could not refresh the {provider.DisplayName} library: {e.Message}");
        }
        catch (Exception e)
        {
            return new StoreLibraryRefresh(null, false, $"Could not reach {provider.DisplayName}: {e.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<string?> SaveLibraryAsync(IStoreAccount account, IReadOnlyCollection<CachedStoreGame> games)
    {
        var accountId = account.AccountId;
        if (accountId == null)
        {
            return null;
        }

        try
        {
            await Task.Run(() => _cache.SyncStoreGames(account.Id, accountId, games));
            return null;
        }
        catch (Exception e)
        {
            return "Could not update the game cache:\n" + e.Message;
        }
    }

    /// <inheritdoc />
    public void SaveProgress(IStoreAccount account, IReadOnlyCollection<(string Id, int Earned, int Total)> results)
    {
        var accountId = account.AccountId;
        if (accountId == null || results.Count == 0)
        {
            return;
        }

        try
        {
            _cache.SaveStoreProgress(account.Id, accountId, results);
        }
        catch (Exception)
        {
            // The counts are already on screen; the next run reads them again.
        }
    }

    /// <inheritdoc />
    public async Task<StoreProgressRun> LoadProgressAsync(
        IStoreProvider provider,
        IStoreAccount account,
        IReadOnlyCollection<(string Id, string Key)> games,
        Action<string, int, int>? gameRead,
        Action<int>? stepped,
        CancellationToken cancellationToken)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = run.Token;
        var queue = new Queue<(string Id, string Key)>(games);
        var gate = new object();
        List<(string Id, int Earned, int Total)> pending = new();
        var done = 0;
        var failed = 0;
        var expired = false;

        async Task Worker()
        {
            while (token.IsCancellationRequested == false)
            {
                (string Id, string Key) game;
                lock (gate)
                {
                    if (queue.TryDequeue(out game) == false)
                    {
                        return;
                    }
                }

                List<(string Id, int Earned, int Total)>? batch = null;
                try
                {
                    var achievements = await provider.GetAchievementsAsync(game.Key, "en", token);
                    var earned = achievements.Count(a => a.Unlocked == true);
                    lock (gate)
                    {
                        gameRead?.Invoke(game.Id, earned, achievements.Count);
                        pending.Add((game.Id, earned, achievements.Count));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ProviderException e) when (e.ErrorCode == "login_required")
                {
                    expired = true;
                    run.Cancel();
                    return;
                }
                catch (Exception)
                {
                    // One game failing is counted, not raised: a run covers hundreds.
                    Interlocked.Increment(ref failed);
                }

                lock (gate)
                {
                    done++;
                    stepped?.Invoke(done);
                    if (pending.Count >= ProgressBatch)
                    {
                        batch = pending.ToList();
                        pending.Clear();
                    }
                }
                if (batch != null)
                {
                    SaveProgress(account, batch);
                }
            }
        }

        try
        {
            await Task.WhenAll(Enumerable.Range(0, ProgressWorkers).Select(_ => Worker()));
        }
        finally
        {
            List<(string Id, int Earned, int Total)> rest;
            lock (gate)
            {
                rest = pending.ToList();
                pending.Clear();
            }
            SaveProgress(account, rest);
        }

        return new StoreProgressRun(done, failed, expired, token.IsCancellationRequested && expired == false);
    }
}
