/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
