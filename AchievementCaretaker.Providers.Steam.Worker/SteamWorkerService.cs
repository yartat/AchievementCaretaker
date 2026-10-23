/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Steam.Protocol;
using Grpc.Core;
using Grpc.Core.Interceptors;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>The gRPC face of an <see cref="ISteamAppBackend"/>.</summary>
internal sealed class SteamWorkerService : SteamWorker.SteamWorkerBase
{
    private readonly ISteamAppBackend _backend;
    private readonly Action _requestExit;

    public SteamWorkerService(ISteamAppBackend backend, Action requestExit)
    {
        _backend = backend;
        _requestExit = requestExit;
    }

    public override async Task<GetAchievementsReply> GetAchievements(GetAchievementsRequest request, ServerCallContext context)
    {
        var list = await Guarded(() => _backend.GetAchievementsAsync(context.CancellationToken));
        GetAchievementsReply reply = new();
        reply.Achievements.AddRange(list.Select(a =>
        {
            Achievement item = new()
            {
                Id = a.Id,
                DisplayName = a.DisplayName ?? "",
                Description = a.Description ?? "",
                IconUrl = a.IconUrl ?? "",
                Hidden = a.Hidden,
                Unlocked = a.Unlocked,
                IsProtected = a.Protected,
            };
            if (a.UnlockDate.HasValue)
            {
                item.UnlockTimeUnix = a.UnlockDate.Value.ToUnixTimeSeconds();
            }
            if (a.XP.HasValue)
            {
                item.Xp = a.XP.Value;
            }
            if (a.RarityPercent.HasValue)
            {
                item.RarityPercent = a.RarityPercent.Value;
            }
            if (a.Progress.HasValue)
            {
                item.Progress = a.Progress.Value;
            }
            return item;
        }));
        return reply;
    }

    public override async Task<GetStatsReply> GetStats(GetStatsRequest request, ServerCallContext context)
    {
        var list = await Guarded(() => _backend.GetStatsAsync(context.CancellationToken));
        GetStatsReply reply = new();
        reply.Stats.AddRange(list.Select(s => new Stat
        {
            Id = s.Id,
            DisplayName = s.DisplayName ?? "",
            Kind = s.Kind == StoreStatKind.Float ? StatKind.Float : StatKind.Integer,
            Value = s.Value,
            Min = s.Min,
            Max = s.Max,
            MaxChange = s.MaxChange,
            IncrementOnly = s.IncrementOnly,
            DefaultValue = s.DefaultValue,
            IsProtected = s.Protected,
        }));
        return reply;
    }

    public override async Task<CommitReply> Commit(CommitRequest request, ServerCallContext context)
    {
        await Guarded(() => _backend.CommitAsync(
            request.Achievements.Select(c => new KeyValuePair<string, bool>(c.Id, c.Unlocked)).ToList(),
            request.Stats.Select(c => new KeyValuePair<string, double>(c.Id, c.Value)).ToList(),
            context.CancellationToken));
        return new CommitReply();
    }

    public override async Task<ResetReply> Reset(ResetRequest request, ServerCallContext context)
    {
        await Guarded(() => _backend.ResetAsync(request.IncludeAchievements, context.CancellationToken));
        return new ResetReply();
    }

    /// <summary>Carries a <see cref="ProviderException"/>'s code across the wire, which a bare exception would lose.</summary>
    private static async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ProviderException e)
        {
            throw new RpcException(
                new Status(StatusCode.FailedPrecondition, e.Message),
                new Metadata { { SteamWorkerHandshake.ErrorCodeTrailer, e.ErrorCode } });
        }
    }

    private static async Task Guarded(Func<Task> call) => await Guarded(async () =>
    {
        await call();
        return true;
    });

    public override async Task ScanLibrary(
        ScanLibraryRequest request, IServerStreamWriter<ScanLibraryEvent> responseStream, ServerCallContext context)
    {
        // Progress is reported from the scan's thread; one task owns the stream, which takes one writer.
        var events = Channel.CreateUnbounded<ScanProgress>();
        var writer = Task.Run(async () =>
        {
            await foreach (var progress in events.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(new ScanLibraryEvent { Progress = progress });
            }
        });

        LibraryScan scan;
        try
        {
            scan = await Guarded(() => _backend.ScanLibraryAsync(
                request.Candidates.Select(c => new LibraryCandidate(c.Id.ToString(CultureInfo.InvariantCulture), c.Type)).ToList(),
                new ChannelProgress(events.Writer),
                context.CancellationToken));
        }
        finally
        {
            events.Writer.TryComplete();
            await writer;
        }

        ScanResult result = new()
        {
            Account = uint.Parse(scan.Account, CultureInfo.InvariantCulture),
            Complete = scan.Complete,
        };
        result.Games.AddRange(scan.Games.Select(ToOwnedGame));
        await responseStream.WriteAsync(new ScanLibraryEvent { Result = result });
    }

    public override async Task<GetAccountReply> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        var account = await Guarded(() => _backend.GetAccountAsync(context.CancellationToken));
        return new GetAccountReply { Account = uint.Parse(account, CultureInfo.InvariantCulture) };
    }

    public override async Task<FindOwnedReply> FindOwned(FindOwnedRequest request, ServerCallContext context)
    {
        var game = await Guarded(() => _backend.FindOwnedAsync(
            new LibraryCandidate(request.Candidate.Id.ToString(CultureInfo.InvariantCulture), request.Candidate.Type),
            context.CancellationToken));
        return game == null ? new FindOwnedReply { Owned = false } : new FindOwnedReply { Owned = true, Game = ToOwnedGame(game) };
    }

    private static OwnedGame ToOwnedGame(StoreGame game)
    {
        OwnedGame owned = new()
        {
            Id = uint.Parse(game.Id, CultureInfo.InvariantCulture),
            Type = game.Type ?? "",
            Name = game.Title ?? "",
            ImageUrl = game.ImageUrl ?? "",
        };
        if (game.ReleaseDate.HasValue)
        {
            owned.ReleaseUnix = game.ReleaseDate.Value.ToUnixTimeSeconds();
        }
        if (game.RatingPercent.HasValue)
        {
            owned.RatingPercent = game.RatingPercent.Value;
        }
        if (game.RatingScore.HasValue)
        {
            owned.RatingScore = game.RatingScore.Value;
        }
        if (game.PlaytimeMinutes.HasValue)
        {
            owned.PlaytimeMinutes = game.PlaytimeMinutes.Value;
        }
        if (game.LastPlayed.HasValue)
        {
            owned.LastPlayedUnix = game.LastPlayed.Value.ToUnixTimeSeconds();
        }
        if (game.AchievementsTotal.HasValue)
        {
            owned.AchievementsTotal = game.AchievementsTotal.Value;
        }
        if (game.AchievementsEarned.HasValue)
        {
            owned.AchievementsEarned = game.AchievementsEarned.Value;
        }
        return owned;
    }

    /// <summary>An <see cref="IProgress{T}"/> that hands reports to the one task writing the stream.</summary>
    private sealed class ChannelProgress : IProgress<LibraryScanProgress>
    {
        private readonly ChannelWriter<ScanProgress> _writer;

        public ChannelProgress(ChannelWriter<ScanProgress> writer)
        {
            _writer = writer;
        }

        public void Report(LibraryScanProgress value) =>
            _writer.TryWrite(new ScanProgress { Examined = value.Examined, Total = value.Total });
    }

    public override Task<ShutdownReply> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        _requestExit();
        return Task.FromResult(new ShutdownReply());
    }
}

/// <summary>Refuses every call that does not carry the token the helper was started with.</summary>
internal sealed class TokenInterceptor : Interceptor
{
    private readonly byte[] _token;

    public TokenInterceptor(string token)
    {
        _token = Encoding.UTF8.GetBytes(token);
    }

    private void Check(ServerCallContext context)
    {
        var presented = context.RequestHeaders.GetValue(SteamWorkerHandshake.TokenHeader);
        // Compared in constant time: the token is the only thing between a local process and Steam.
        if (presented == null || CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _token) == false)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Bad token."));
        }
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Check(context);
        return continuation(request, context);
    }
}
