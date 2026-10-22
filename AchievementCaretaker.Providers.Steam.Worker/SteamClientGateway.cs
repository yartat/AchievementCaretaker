/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.SteamApi;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// <see cref="ISteamAppGateway"/> over the real Steam client, initialised with the one app id
/// this process serves. Every native call is made under one lock: the gRPC server calls from
/// pool threads, and Steam's interfaces are not thread-safe.
/// </summary>
public sealed class SteamClientGateway : ISteamAppGateway
{
    private static readonly TimeSpan _statsTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Client _client;
    private Task _userStats;

    public long AppId { get; }

    public string Language { get; }

    /// <summary>Connects to Steam as the given app. Throws <see cref="ClientInitializeException"/> if it cannot.</summary>
    public SteamClientGateway(long appId)
    {
        AppId = appId;
        _client = new Client();
        try
        {
            _client.Initialize(appId);
            Language = _client.SteamApps008.GetCurrentGameLanguage();
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public Task EnsureUserStatsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _userStats ??= RequestUserStatsAsync();
            return _userStats.WaitAsync(cancellationToken);
        }
    }

    private async Task RequestUserStatsAsync()
    {
        TaskCompletionSource<int> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = _client.CreateAndRegisterCallback<SteamApi.Callbacks.UserStatsReceived>();
        callback.OnRun += param => answer.TrySetResult(param.Result);

        lock (_gate)
        {
            var steamId = _client.SteamUser.GetSteamId();
            if (_client.SteamUserStats.RequestUserStats(steamId) == CallHandle.Invalid)
            {
                throw new InvalidOperationException("Steam would not accept the request for the user's stats.");
            }
        }

        using CancellationTokenSource timeout = new(_statsTimeout);
        try
        {
            while (answer.Task.IsCompleted == false)
            {
                lock (_gate)
                {
                    _client.RunCallbacks(false);
                }
                await Task.Delay(50, timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Steam did not answer the request for the user's stats.");
        }

        if (answer.Task.Result != 1)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Steam refused the user's stats (result {answer.Task.Result})."));
        }
    }

    public KeyValue LoadSchema()
    {
        var path = Path.Combine(
            AchievementCaretaker.SteamApi.Steam.GetInstallPath(), "appcache", "stats",
            string.Create(CultureInfo.InvariantCulture, $"UserGameStatsSchema_{AppId}.bin"));
        return File.Exists(path) == true ? KeyValue.LoadAsBinary(path) : null;
    }

    public bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetAchievementAndUnlockTime(id, out unlocked, out unlockTime);
        }
    }

    public bool SetAchievement(string id, bool unlocked)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetAchievement(id, unlocked);
        }
    }

    public bool TryGetIntStat(string id, out int value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetStatValue(id, out value);
        }
    }

    public bool TryGetFloatStat(string id, out float value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetStatValue(id, out value);
        }
    }

    public bool SetIntStat(string id, int value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetStatValue(id, value);
        }
    }

    public bool SetFloatStat(string id, float value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetStatValue(id, value);
        }
    }

    public bool ResetAllStats(bool includeAchievements)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.ResetAllStats(includeAchievements);
        }
    }

    public bool StoreStats()
    {
        lock (_gate)
        {
            return _client.SteamUserStats.StoreStats();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _client.Dispose();
        }
    }
}
