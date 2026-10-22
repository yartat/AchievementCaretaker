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

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>
    /// <see cref="ISteamAppGateway"/> over the real Steam client, initialised with the one app id
    /// this process serves. Every native call is made under one lock: the gRPC server calls from
    /// pool threads, and Steam's interfaces are not thread-safe.
    /// </summary>
    public sealed class SteamClientGateway : ISteamAppGateway
    {
        private static readonly TimeSpan StatsTimeout = TimeSpan.FromSeconds(30);

        private readonly object _Gate = new();
        private readonly Client _Client;
        private Task _UserStats;

        public long AppId { get; }

        public string Language { get; }

        /// <summary>Connects to Steam as the given app. Throws <see cref="ClientInitializeException"/> if it cannot.</summary>
        public SteamClientGateway(long appId)
        {
            this.AppId = appId;
            this._Client = new Client();
            try
            {
                this._Client.Initialize(appId);
                this.Language = this._Client.SteamApps008.GetCurrentGameLanguage();
            }
            catch
            {
                this._Client.Dispose();
                throw;
            }
        }

        public Task EnsureUserStatsAsync(CancellationToken cancellationToken)
        {
            lock (this._Gate)
            {
                this._UserStats ??= this.RequestUserStatsAsync();
                return this._UserStats.WaitAsync(cancellationToken);
            }
        }

        private async Task RequestUserStatsAsync()
        {
            TaskCompletionSource<int> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = this._Client.CreateAndRegisterCallback<SteamApi.Callbacks.UserStatsReceived>();
            callback.OnRun += param => answer.TrySetResult(param.Result);

            lock (this._Gate)
            {
                var steamId = this._Client.SteamUser.GetSteamId();
                if (this._Client.SteamUserStats.RequestUserStats(steamId) == CallHandle.Invalid)
                {
                    throw new InvalidOperationException("Steam would not accept the request for the user's stats.");
                }
            }

            using CancellationTokenSource timeout = new(StatsTimeout);
            try
            {
                while (answer.Task.IsCompleted == false)
                {
                    lock (this._Gate)
                    {
                        this._Client.RunCallbacks(false);
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
                string.Create(CultureInfo.InvariantCulture, $"UserGameStatsSchema_{this.AppId}.bin"));
            return File.Exists(path) == true ? KeyValue.LoadAsBinary(path) : null;
        }

        public bool TryGetAchievement(string id, out bool unlocked, out uint unlockTime)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.GetAchievementAndUnlockTime(id, out unlocked, out unlockTime);
            }
        }

        public bool SetAchievement(string id, bool unlocked)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.SetAchievement(id, unlocked);
            }
        }

        public bool TryGetIntStat(string id, out int value)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.GetStatValue(id, out value);
            }
        }

        public bool TryGetFloatStat(string id, out float value)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.GetStatValue(id, out value);
            }
        }

        public bool SetIntStat(string id, int value)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.SetStatValue(id, value);
            }
        }

        public bool SetFloatStat(string id, float value)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.SetStatValue(id, value);
            }
        }

        public bool ResetAllStats(bool includeAchievements)
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.ResetAllStats(includeAchievements);
            }
        }

        public bool StoreStats()
        {
            lock (this._Gate)
            {
                return this._Client.SteamUserStats.StoreStats();
            }
        }

        public void Dispose()
        {
            lock (this._Gate)
            {
                this._Client.Dispose();
            }
        }
    }
}
