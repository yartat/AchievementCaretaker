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

namespace AchievementCaretaker.Providers.Epic.Models;

/// <summary>
/// The signed-in Epic account's tokens, as returned by the OAuth token
/// endpoint. The field names are Epic's, so the response deserializes
/// straight into this and the file on disk reads the same.
/// </summary>
/// <remarks>
/// The refresh token is a credential for the whole account; nothing here
/// should ever be logged or printed. See <see cref="ISessionStore"/> for how
/// the file it is saved to is protected.
/// </remarks>
public sealed record EpicSession
{
    // A record prints every property in ToString, and these hold tokens: print nothing.
    private bool PrintMembers(System.Text.StringBuilder builder) => false;

    /// <summary>The token that authorises requests to Epic's services.</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    /// <summary>When <see cref="AccessToken"/> stops working.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The credential that gets a new access token without signing in again.</summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }

    /// <summary>When <see cref="RefreshToken"/> stops working; after that the user signs in again.</summary>
    [JsonPropertyName("refresh_expires_at")]
    public DateTimeOffset RefreshExpiresAt { get; set; }

    /// <summary>The Epic account's id, which a saved library is keyed by.</summary>
    [JsonPropertyName("account_id")]
    public string AccountId { get; set; }

    /// <summary>The account's name, for the rail.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    /// <summary>
    /// Whether <see cref="AccessToken"/> can be used now, with a minute of slack so a token is
    /// not used just as it expires in flight.
    /// </summary>
    [JsonIgnore]
    public bool IsAccessTokenValid =>
        string.IsNullOrEmpty(AccessToken) == false &&
        ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

    /// <summary>Whether <see cref="RefreshToken"/> can still be exchanged, with the same minute of slack.</summary>
    [JsonIgnore]
    public bool IsRefreshTokenValid =>
        string.IsNullOrEmpty(RefreshToken) == false &&
        RefreshExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);
}
