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
using AchievementCaretaker.Providers.Steam.Models;
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
    private readonly SteamAppIdFile? _appIdFile;
    private readonly int? _processId;
    private LibraryStats? _libraryStats;
    private Task? _userStats;

    /// <inheritdoc />
    public long AppId { get; }

    /// <inheritdoc />
    public string Language { get; }

    /// <inheritdoc />
    public ulong SteamId { get; }

    /// <summary>Connects to Steam as the given app. Throws <see cref="ClientInitializeException"/> if it cannot.</summary>
    /// <param name="appId">The app id to act as; 0 for the library as a whole.</param>
    public SteamClientGateway(long appId)
    {
        AppId = appId;
        // App id 0 is not a game: it is the library's own session, which needs no file.
        _appIdFile = appId == 0 ? null : SteamAppIdFile.Create(appId);
        _client = new Client();
        try
        {
            _client.Initialize(appId);
            Language = _client.SteamApps008.GetCurrentGameLanguage() ?? "english";
            SteamId = _client.SteamUser.GetSteamId();
            _processId = SteamPlatform.GetRunningClientProcessId();
        }
        catch
        {
            _client.Dispose();
            _appIdFile?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public KeyValue? LoadSchema()
    {
        var installPath = AchievementCaretaker.SteamApi.Steam.GetInstallPath();
        if (installPath == null)
        {
            return null;
        }

        var path = Path.Combine(
            installPath, "appcache", "stats",
            string.Create(CultureInfo.InvariantCulture, $"UserGameStatsSchema_{AppId}.bin"));
        return File.Exists(path) == true ? KeyValue.LoadAsBinary(path) : null;
    }

    /// <inheritdoc />
    public bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetAchievementAndUnlockTime(id, out unlocked, out unlockTime);
        }
    }

    /// <inheritdoc />
    public bool SetAchievement(string id, bool unlocked)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetAchievement(id, unlocked);
        }
    }

    /// <inheritdoc />
    public bool TryGetIntStat(string id, out int value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetStatValue(id, out value);
        }
    }

    /// <inheritdoc />
    public bool TryGetFloatStat(string id, out float value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.GetStatValue(id, out value);
        }
    }

    /// <inheritdoc />
    public bool SetIntStat(string id, int value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetStatValue(id, value);
        }
    }

    /// <inheritdoc />
    public bool SetFloatStat(string id, float value)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.SetStatValue(id, value);
        }
    }

    /// <inheritdoc />
    public bool ResetAllStats(bool includeAchievements)
    {
        lock (_gate)
        {
            return _client.SteamUserStats.ResetAllStats(includeAchievements);
        }
    }

    /// <inheritdoc />
    public bool StoreStats()
    {
        lock (_gate)
        {
            return _client.SteamUserStats.StoreStats();
        }
    }

    /// <summary>Disconnects from Steam and removes the app id file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _client.Dispose();
        }
        _appIdFile?.Dispose();
    }

    #region Library

    /// <inheritdoc />
    public bool IsSubscribed(uint appId)
    {
        lock (_gate)
        {
            return _client.SteamApps008.IsSubscribedApp(appId);
        }
    }

    /// <inheritdoc />
    public string? GetAppData(uint appId, string key)
    {
        lock (_gate)
        {
            return _client.SteamApps001.GetAppData(appId, key);
        }
    }

    /// <inheritdoc />
    public GameStats? GetCachedStats(uint appId)
    {
        _libraryStats ??= LibraryStats.Create(SteamId);
        return _libraryStats?.TryGet(appId);
    }

    /// <inheritdoc />
    public bool IsSteamStillRunning()
    {
        // With no process id on record there is no way to tell, and a connection that exists is
        // taken to be held.
        return _processId == null || SteamPlatform.GetRunningClientProcessId() == _processId;
    }

    #endregion
}
