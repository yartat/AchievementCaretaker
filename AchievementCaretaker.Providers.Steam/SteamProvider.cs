/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * Based on Steam Achievement Manager (https://github.com/gibbed/SteamAchievementManager)
 * by Rick (gibbed). This is an altered source version: see docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root and LICENSE.txt for the zlib notice that applies
 * to the parts derived from Steam Achievement Manager.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Steam.Protocol;
using Grpc.Core;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// Steam behind <see cref="IStoreProvider"/>. Every call starts a helper for the one app id
/// it is about (Steam's user stats are scoped to the app id a process was initialised
/// with), talks to it over gRPC on 127.0.0.1 and lets it go.
/// </summary>
/// <remarks>
/// <para>
/// Steam has no sign-in of its own: whoever is logged in to the Steam client is the account.
/// Listing the library is not done here; it comes from Steam's cache files.
/// </para>
/// <para>
/// A helper is kept for a game for a short while after its last call, so the read, the
/// statistics and the commit of one editing session share one start of Steam instead of
/// three. <see cref="GetAchievementsAsync"/> is the exception: it always starts a fresh
/// helper, because it is what a reload asks for and a kept helper would answer from the
/// stats it read when it started.
/// </para>
/// </remarks>
public sealed class SteamProvider : IStatsStoreProvider, ILibraryScanProvider, IDisposable, IAsyncDisposable
{
    public const string ProviderId = "steam";

    /// <summary>How long a helper outlives its last call.</summary>
    public static readonly TimeSpan DefaultIdle = TimeSpan.FromSeconds(30);

    private readonly ISteamWorkerLauncher _launcher;
    private readonly TimeSpan _idle;
    private readonly object _gate = new();
    private readonly Dictionary<long, Task<LiveWorker>> _live = new();
    private bool _disposed;

    public SteamProvider(ISteamWorkerLauncher launcher, TimeSpan? idle = null)
    {
        _launcher = launcher;
        _idle = idle ?? DefaultIdle;
    }

    public string Id => ProviderId;

    public string DisplayName => "Steam";

    public string SignInUrl => "";

    public string SignInHint => "";

    public SignInResultKind GetSignInResultKind(Uri address) => SignInResultKind.None;

    public bool IsSignedIn => true;

    public string AccountName => null;

    public string AccountId => null;

    public Task SignInAsync(string pasted, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Steam uses the account that is logged in to the Steam client.");

    public Task EnsureSignedInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void SignOut()
    {
    }

    public Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Steam does not list an account's games: use ScanLibraryAsync with the published list.");

    /// <summary>The library's session: app id 0, which is not a game but how Steam is asked about the whole account.</summary>
    private const string LibraryKey = "0";

    public async Task<LibraryScan> ScanLibraryAsync(
        IReadOnlyCollection<LibraryCandidate> candidates,
        IProgress<LibraryScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ScanLibraryRequest request = new();
        request.Candidates.AddRange(candidates.Select(c => new Candidate
        {
            Id = uint.Parse(c.Id, CultureInfo.InvariantCulture),
            Type = c.Type ?? "",
        }));

        // A scan always starts a helper of its own: a kept one may belong to a Steam that has
        // since quit, and a scan is long enough that starting one is not the cost.
        return await WithWorkerAsync(LibraryKey, async (channel, token) =>
        {
            using var call = channel.Client.ScanLibrary(request, channel.Headers, cancellationToken: token);
            await foreach (var scanEvent in call.ResponseStream.ReadAllAsync(token))
            {
                if (scanEvent.EventCase == ScanLibraryEvent.EventOneofCase.Progress)
                {
                    progress?.Report(new LibraryScanProgress(scanEvent.Progress.Examined, scanEvent.Progress.Total));
                }
                else if (scanEvent.EventCase == ScanLibraryEvent.EventOneofCase.Result)
                {
                    return new LibraryScan(
                        scanEvent.Result.Account.ToString(CultureInfo.InvariantCulture),
                        scanEvent.Result.Games.Select(ToGame).ToList(),
                        scanEvent.Result.Complete);
                }
            }
            throw new ProviderException("steam_error", "The Steam helper ended the scan without a result.");
        }, cancellationToken, fresh: true);
    }

    public async Task<string> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        return await WithWorkerAsync(LibraryKey, async (channel, token) =>
        {
            var reply = await channel.Client.GetAccountAsync(new GetAccountRequest(), channel.Headers, cancellationToken: token);
            return reply.Account.ToString(CultureInfo.InvariantCulture);
        }, cancellationToken, fresh: true);
    }

    public async Task<StoreGame> FindOwnedAsync(LibraryCandidate candidate, CancellationToken cancellationToken = default)
    {
        FindOwnedRequest request = new()
        {
            Candidate = new Candidate { Id = uint.Parse(candidate.Id, CultureInfo.InvariantCulture), Type = candidate.Type ?? "" },
        };

        return await WithWorkerAsync(LibraryKey, async (channel, token) =>
        {
            var reply = await channel.Client.FindOwnedAsync(request, channel.Headers, cancellationToken: token);
            return reply.Owned == true ? ToGame(reply.Game) : null;
        }, cancellationToken, fresh: true);
    }

    private static StoreGame ToGame(OwnedGame g) => new(
        ProviderId,
        g.Id.ToString(CultureInfo.InvariantCulture),
        g.Id.ToString(CultureInfo.InvariantCulture),
        string.IsNullOrEmpty(g.Name) ? null : g.Name,
        string.IsNullOrEmpty(g.ImageUrl) ? null : g.ImageUrl,
        string.IsNullOrEmpty(g.Type) ? "normal" : g.Type,
        g.HasReleaseUnix ? DateTimeOffset.FromUnixTimeSeconds(g.ReleaseUnix) : null,
        g.HasRatingPercent ? g.RatingPercent : null,
        g.HasRatingScore ? g.RatingScore : null,
        g.HasPlaytimeMinutes ? g.PlaytimeMinutes : null,
        g.HasLastPlayedUnix ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayedUnix) : null,
        g.HasAchievementsTotal ? g.AchievementsTotal : null,
        g.HasAchievementsEarned ? g.AchievementsEarned : null);

    public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
        string gameKey, string locale = "en", CancellationToken cancellationToken = default)
    {
        return await WithWorkerAsync(gameKey, async (channel, token) =>
        {
            var reply = await channel.Client.GetAchievementsAsync(
                new GetAchievementsRequest(), channel.Headers, cancellationToken: token);
            return (IReadOnlyList<StoreAchievement>)reply.Achievements.Select(ToModel).ToList();
        }, cancellationToken, fresh: true);
    }

    public Task SetAchievementsAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> changes,
        CancellationToken cancellationToken = default) =>
        CommitAsync(gameKey, changes, Array.Empty<KeyValuePair<string, double>>(), cancellationToken);

    public async Task<IReadOnlyList<StoreStat>> GetStatsAsync(string gameKey, CancellationToken cancellationToken = default)
    {
        return await WithWorkerAsync(gameKey, async (channel, token) =>
        {
            var reply = await channel.Client.GetStatsAsync(new GetStatsRequest(), channel.Headers, cancellationToken: token);
            return (IReadOnlyList<StoreStat>)reply.Stats.Select(s => new StoreStat(
                s.Id,
                s.DisplayName,
                s.Kind == StatKind.Float ? StoreStatKind.Float : StoreStatKind.Integer,
                s.Value,
                s.Min,
                s.Max,
                s.MaxChange,
                s.IncrementOnly,
                s.DefaultValue,
                s.IsProtected)).ToList();
        }, cancellationToken);
    }

    public async Task CommitAsync(
        string gameKey,
        IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
        IReadOnlyCollection<KeyValuePair<string, double>> statistics,
        CancellationToken cancellationToken = default)
    {
        CommitRequest request = new();
        request.Achievements.AddRange(achievements.Select(c => new AchievementChange { Id = c.Key, Unlocked = c.Value }));
        request.Stats.AddRange(statistics.Select(c => new StatChange { Id = c.Key, Value = c.Value }));

        await WithWorkerAsync(gameKey, async (channel, token) =>
        {
            await channel.Client.CommitAsync(request, channel.Headers, cancellationToken: token);
            return true;
        }, cancellationToken);
    }

    public async Task ResetAsync(string gameKey, bool includeAchievements, CancellationToken cancellationToken = default)
    {
        await WithWorkerAsync(gameKey, async (channel, token) =>
        {
            await channel.Client.ResetAsync(
                new ResetRequest { IncludeAchievements = includeAchievements }, channel.Headers, cancellationToken: token);
            return true;
        }, cancellationToken);
    }

    private async Task<T> WithWorkerAsync<T>(
        string gameKey,
        Func<SteamWorkerChannel, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken,
        bool fresh = false)
    {
        if (long.TryParse(gameKey, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) == false)
        {
            throw new ProviderException("bad_game", $"'{gameKey}' is not a Steam app id.");
        }

        var live = await GetLiveAsync(appId, fresh, cancellationToken);
        await live.Gate.WaitAsync(cancellationToken);
        try
        {
            live.StopIdleTimer();
            return await call(live.Channel, cancellationToken);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unauthenticated)
        {
            Drop(appId, live);
            throw new ProviderException("worker_denied", "The Steam helper refused the request.");
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.FailedPrecondition &&
                                     e.Trailers.GetValue(SteamWorkerHandshake.ErrorCodeTrailer) is { } code)
        {
            // The helper is fine; Steam said no to this request.
            throw new ProviderException(code, e.Status.Detail);
        }
        catch (RpcException e) when (e.StatusCode != StatusCode.Cancelled)
        {
            Drop(appId, live);
            throw new ProviderException("steam_error", e.Status.Detail);
        }
        catch (Exception)
        {
            // Anything else leaves the helper in an unknown state: the next call starts a new one.
            Drop(appId, live);
            throw;
        }
        finally
        {
            live.Gate.Release();
            live.StartIdleTimer(() => Drop(appId, live));
        }
    }

    private async Task<LiveWorker> GetLiveAsync(long appId, bool fresh, CancellationToken cancellationToken)
    {
        Task<LiveWorker> stale = null;
        Task<LiveWorker> result;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_live.TryGetValue(appId, out var existing) == true && fresh == false)
            {
                result = existing;
            }
            else
            {
                if (existing != null)
                {
                    stale = existing;
                }
                result = StartLiveAsync(appId, cancellationToken);
                _live[appId] = result;
            }
        }

        if (stale != null)
        {
            _ = DisposeLaterAsync(stale);
        }
        try
        {
            return await result;
        }
        catch
        {
            // A failed start must not be remembered, or every later call would get the same failure.
            lock (_gate)
            {
                if (_live.TryGetValue(appId, out var current) == true && ReferenceEquals(current, result) == true)
                {
                    _live.Remove(appId);
                }
            }
            throw;
        }
    }

    private async Task<LiveWorker> StartLiveAsync(long appId, CancellationToken cancellationToken)
    {
        var worker = await _launcher.StartAsync(appId, cancellationToken);
        return new LiveWorker(worker, new SteamWorkerChannel(worker.Endpoint), _idle);
    }

    private void Drop(long appId, LiveWorker live)
    {
        Task<LiveWorker> removed = null;
        lock (_gate)
        {
            if (_live.TryGetValue(appId, out var current) == true &&
                current.IsCompletedSuccessfully == true && ReferenceEquals(current.Result, live) == true)
            {
                _live.Remove(appId);
                removed = current;
            }
        }
        if (removed != null)
        {
            _ = DisposeLaterAsync(removed);
        }
    }

    private static async Task DisposeLaterAsync(Task<LiveWorker> live)
    {
        try
        {
            var worker = await live;
            await worker.DisposeAsync();
        }
        catch (Exception)
        {
            // It never started, or is already gone: nothing to let go of.
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Task<LiveWorker>> all;
        lock (_gate)
        {
            _disposed = true;
            all = _live.Values.ToList();
            _live.Clear();
        }
        foreach (var live in all)
        {
            await DisposeLaterAsync(live);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>A started helper with its connection, its idle timer and the gate that serialises calls to it.</summary>
    private sealed class LiveWorker : IAsyncDisposable
    {
        private readonly ISteamWorker _worker;
        private readonly TimeSpan _idle;
        private Timer _timer;

        public SteamWorkerChannel Channel { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public LiveWorker(ISteamWorker worker, SteamWorkerChannel channel, TimeSpan idle)
        {
            _worker = worker;
            Channel = channel;
            _idle = idle;
        }

        public void StopIdleTimer()
        {
            var timer = Interlocked.Exchange(ref _timer, null);
            timer?.Dispose();
        }

        public void StartIdleTimer(Action expired)
        {
            var timer = new Timer(_ => expired(), null, _idle, Timeout.InfiniteTimeSpan);
            var previous = Interlocked.Exchange(ref _timer, timer);
            previous?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            StopIdleTimer();
            Channel.Dispose();
            await _worker.DisposeAsync();
        }
    }

    private static StoreAchievement ToModel(Achievement a) => new(
        a.Id,
        a.DisplayName,
        a.Description,
        string.IsNullOrEmpty(a.IconUrl) ? null : a.IconUrl,
        a.Hidden,
        a.HasXp ? a.Xp : null,
        a.HasRarityPercent ? a.RarityPercent : null,
        a.Unlocked,
        a.HasProgress ? a.Progress : null,
        a.HasUnlockTimeUnix ? DateTimeOffset.FromUnixTimeSeconds(a.UnlockTimeUnix) : null,
        a.IsProtected);
}
