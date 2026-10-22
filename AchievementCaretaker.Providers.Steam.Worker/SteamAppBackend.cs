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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>
    /// The helper's work: read one game's achievements from Steam, and set and store them.
    /// Steam is reached through an <see cref="ISteamAppGateway"/>.
    /// </summary>
    public sealed class SteamAppBackend : ISteamAppBackend
    {
        private readonly ISteamAppGateway _Gateway;

        public SteamAppBackend(ISteamAppGateway gateway)
        {
            this._Gateway = gateway;
        }

        public async Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(CancellationToken cancellationToken)
        {
            await this._Gateway.EnsureUserStatsAsync(cancellationToken);

            var schema = this._Gateway.LoadSchema()
                ?? throw new ProviderException("no_schema", "Steam has no achievement schema cached for this game yet.");

            List<StoreAchievement> result = new();
            foreach (var def in SteamSchemaReader.ReadAchievements(schema, this._Gateway.AppId, this._Gateway.Language))
            {
                if (string.IsNullOrEmpty(def.Id) == true ||
                    this._Gateway.TryGetAchievement(def.Id, out var unlocked, out var unlockTime) == false)
                {
                    continue;
                }

                var icon = unlocked == true || string.IsNullOrEmpty(def.IconLocked) == true ? def.IconNormal : def.IconLocked;
                result.Add(new StoreAchievement(
                    def.Id,
                    def.Name,
                    def.Description,
                    string.IsNullOrEmpty(icon)
                        ? null
                        : string.Create(CultureInfo.InvariantCulture,
                            $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{this._Gateway.AppId}/{icon}"),
                    def.Hidden,
                    null,
                    null,
                    unlocked,
                    null,
                    unlocked == true && unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime) : null,
                    (def.Permission & 3) != 0));
            }
            return result;
        }

        public async Task<IReadOnlyList<StoreStat>> GetStatsAsync(CancellationToken cancellationToken)
        {
            await this._Gateway.EnsureUserStatsAsync(cancellationToken);

            var schema = this._Gateway.LoadSchema()
                ?? throw new ProviderException("no_schema", "Steam has no schema cached for this game yet.");

            List<StoreStat> result = new();
            foreach (var def in SteamSchemaReader.ReadStats(schema, this._Gateway.AppId, this._Gateway.Language))
            {
                double value;
                if (def.Kind == StoreStatKind.Integer)
                {
                    if (this._Gateway.TryGetIntStat(def.Id, out var whole) == false)
                    {
                        continue;
                    }
                    value = whole;
                }
                else
                {
                    if (this._Gateway.TryGetFloatStat(def.Id, out var fraction) == false)
                    {
                        continue;
                    }
                    value = fraction;
                }
                result.Add(new StoreStat(
                    def.Id, def.DisplayName, def.Kind, value, def.Min, def.Max, def.MaxChange,
                    def.IncrementOnly, def.DefaultValue, def.Protected));
            }
            return result;
        }

        public async Task CommitAsync(
            IReadOnlyCollection<KeyValuePair<string, bool>> achievements,
            IReadOnlyCollection<KeyValuePair<string, double>> statistics,
            CancellationToken cancellationToken)
        {
            if (achievements.Count == 0 && statistics.Count == 0)
            {
                return;
            }

            await this._Gateway.EnsureUserStatsAsync(cancellationToken);

            if (achievements.Count > 0)
            {
                var schema = this._Gateway.LoadSchema();
                var protectedIds = schema == null
                    ? new HashSet<string>()
                    : SteamSchemaReader.ReadAchievements(schema, this._Gateway.AppId, this._Gateway.Language)
                        .Where(d => (d.Permission & 3) != 0).Select(d => d.Id).ToHashSet();
                foreach (var change in achievements)
                {
                    if (protectedIds.Contains(change.Key) == true)
                    {
                        throw new ProviderException(
                            "protected", $"{change.Key} is protected by Steam; nothing has been stored.");
                    }
                    if (this._Gateway.SetAchievement(change.Key, change.Value) == false)
                    {
                        throw new ProviderException(
                            "set_failed", $"Steam would not change {change.Key}; nothing has been stored.");
                    }
                }
            }

            if (statistics.Count > 0)
            {
                var schema = this._Gateway.LoadSchema()
                    ?? throw new ProviderException("no_schema", "Steam has no schema cached for this game yet.");
                var definitions = SteamSchemaReader.ReadStats(schema, this._Gateway.AppId, this._Gateway.Language)
                    .ToDictionary(d => d.Id);
                foreach (var change in statistics)
                {
                    if (definitions.TryGetValue(change.Key, out var def) == false)
                    {
                        throw new ProviderException("unknown_stat", $"{change.Key} is not a statistic of this game.");
                    }
                    if (def.Protected == true)
                    {
                        throw new ProviderException(
                            "protected", $"{change.Key} is protected by Steam; nothing has been stored.");
                    }

                    var accepted = def.Kind == StoreStatKind.Integer
                        ? this._Gateway.SetIntStat(change.Key, checked((int)Math.Round(change.Value)))
                        : this._Gateway.SetFloatStat(change.Key, (float)change.Value);
                    if (accepted == false)
                    {
                        throw new ProviderException(
                            "set_failed", $"Steam would not change {change.Key}; nothing has been stored.");
                    }
                }
            }

            if (this._Gateway.StoreStats() == false)
            {
                throw new ProviderException("store_failed", "Steam would not store the changes.");
            }
        }

        public async Task ResetAsync(bool includeAchievements, CancellationToken cancellationToken)
        {
            await this._Gateway.EnsureUserStatsAsync(cancellationToken);
            if (this._Gateway.ResetAllStats(includeAchievements) == false)
            {
                throw new ProviderException("reset_failed", "Steam would not reset the statistics.");
            }
        }
    }
}
