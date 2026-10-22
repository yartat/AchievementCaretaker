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

namespace AchievementCaretaker.Providers.Steam
{
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
    public sealed class SteamProvider : IStatsStoreProvider, IDisposable, IAsyncDisposable
    {
        public const string ProviderId = "steam";

        /// <summary>How long a helper outlives its last call.</summary>
        public static readonly TimeSpan DefaultIdle = TimeSpan.FromSeconds(30);

        private readonly ISteamWorkerLauncher _Launcher;
        private readonly TimeSpan _Idle;
        private readonly object _Gate = new();
        private readonly Dictionary<long, Task<LiveWorker>> _Live = new();
        private bool _Disposed;

        public SteamProvider(ISteamWorkerLauncher launcher, TimeSpan? idle = null)
        {
            this._Launcher = launcher;
            this._Idle = idle ?? DefaultIdle;
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
            throw new NotSupportedException("Steam's library is read from Steam's own cache files.");

        public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey, string locale = "en", CancellationToken cancellationToken = default)
        {
            return await this.WithWorkerAsync(gameKey, async (channel, token) =>
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
            this.CommitAsync(gameKey, changes, Array.Empty<KeyValuePair<string, double>>(), cancellationToken);

        public async Task<IReadOnlyList<StoreStat>> GetStatsAsync(string gameKey, CancellationToken cancellationToken = default)
        {
            return await this.WithWorkerAsync(gameKey, async (channel, token) =>
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

            await this.WithWorkerAsync(gameKey, async (channel, token) =>
            {
                await channel.Client.CommitAsync(request, channel.Headers, cancellationToken: token);
                return true;
            }, cancellationToken);
        }

        public async Task ResetAsync(string gameKey, bool includeAchievements, CancellationToken cancellationToken = default)
        {
            await this.WithWorkerAsync(gameKey, async (channel, token) =>
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

            var live = await this.GetLiveAsync(appId, fresh, cancellationToken);
            await live.Gate.WaitAsync(cancellationToken);
            try
            {
                live.StopIdleTimer();
                return await call(live.Channel, cancellationToken);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Unauthenticated)
            {
                this.Drop(appId, live);
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
                this.Drop(appId, live);
                throw new ProviderException("steam_error", e.Status.Detail);
            }
            catch (Exception)
            {
                // Anything else leaves the helper in an unknown state: the next call starts a new one.
                this.Drop(appId, live);
                throw;
            }
            finally
            {
                live.Gate.Release();
                live.StartIdleTimer(() => this.Drop(appId, live));
            }
        }

        private async Task<LiveWorker> GetLiveAsync(long appId, bool fresh, CancellationToken cancellationToken)
        {
            Task<LiveWorker> stale = null;
            Task<LiveWorker> result;
            lock (this._Gate)
            {
                ObjectDisposedException.ThrowIf(this._Disposed, this);
                if (this._Live.TryGetValue(appId, out var existing) == true && fresh == false)
                {
                    result = existing;
                }
                else
                {
                    if (existing != null)
                    {
                        stale = existing;
                    }
                    result = this.StartLiveAsync(appId, cancellationToken);
                    this._Live[appId] = result;
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
                lock (this._Gate)
                {
                    if (this._Live.TryGetValue(appId, out var current) == true && ReferenceEquals(current, result) == true)
                    {
                        this._Live.Remove(appId);
                    }
                }
                throw;
            }
        }

        private async Task<LiveWorker> StartLiveAsync(long appId, CancellationToken cancellationToken)
        {
            var worker = await this._Launcher.StartAsync(appId, cancellationToken);
            return new LiveWorker(worker, new SteamWorkerChannel(worker.Endpoint), this._Idle);
        }

        private void Drop(long appId, LiveWorker live)
        {
            Task<LiveWorker> removed = null;
            lock (this._Gate)
            {
                if (this._Live.TryGetValue(appId, out var current) == true &&
                    current.IsCompletedSuccessfully == true && ReferenceEquals(current.Result, live) == true)
                {
                    this._Live.Remove(appId);
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
            lock (this._Gate)
            {
                this._Disposed = true;
                all = this._Live.Values.ToList();
                this._Live.Clear();
            }
            foreach (var live in all)
            {
                await DisposeLaterAsync(live);
            }
        }

        public void Dispose() => this.DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>A started helper with its connection, its idle timer and the gate that serialises calls to it.</summary>
        private sealed class LiveWorker : IAsyncDisposable
        {
            private readonly ISteamWorker _Worker;
            private readonly TimeSpan _Idle;
            private Timer _Timer;

            public SteamWorkerChannel Channel { get; }

            public SemaphoreSlim Gate { get; } = new(1, 1);

            public LiveWorker(ISteamWorker worker, SteamWorkerChannel channel, TimeSpan idle)
            {
                this._Worker = worker;
                this.Channel = channel;
                this._Idle = idle;
            }

            public void StopIdleTimer()
            {
                var timer = Interlocked.Exchange(ref this._Timer, null);
                timer?.Dispose();
            }

            public void StartIdleTimer(Action expired)
            {
                var timer = new Timer(_ => expired(), null, this._Idle, Timeout.InfiniteTimeSpan);
                var previous = Interlocked.Exchange(ref this._Timer, timer);
                previous?.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                this.StopIdleTimer();
                this.Channel.Dispose();
                await this._Worker.DisposeAsync();
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
}
