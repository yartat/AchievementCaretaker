/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * Written from the endpoints that the open-source launchers Legendary
 * (https://github.com/derrod/legendary) and Heroic document for Epic Games' services;
 * see docs/EPIC-RESEARCH.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Text.Json.Serialization;

namespace AchievementCaretaker.Providers.Epic
{
    /// <summary>
    /// The signed-in Epic account's tokens, as returned by the OAuth token
    /// endpoint. The field names are Epic's, so the response deserializes
    /// straight into this and the file on disk reads the same.
    /// </summary>
    /// <remarks>
    /// The refresh token is a credential for the whole account; nothing here
    /// should ever be logged or printed. See <see cref="SessionFile"/> for how
    /// the file it is saved to is protected.
    /// </remarks>
    public sealed class EpicSession
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("refresh_expires_at")]
        public DateTimeOffset RefreshExpiresAt { get; set; }

        [JsonPropertyName("account_id")]
        public string AccountId { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// A minute of slack, so a token is not used just as it expires in flight.
        /// </summary>
        [JsonIgnore]
        public bool IsAccessTokenValid =>
            string.IsNullOrEmpty(this.AccessToken) == false &&
            this.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

        [JsonIgnore]
        public bool IsRefreshTokenValid =>
            string.IsNullOrEmpty(this.RefreshToken) == false &&
            this.RefreshExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

        /// <summary>Returns null when there is no usable session file.</summary>
        public static EpicSession Load(string path)
        {
            return SessionFile.Load<EpicSession>(path);
        }

        public void Save(string path)
        {
            SessionFile.Save(path, this);
        }
    }
}
