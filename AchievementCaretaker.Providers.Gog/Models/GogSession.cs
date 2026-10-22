/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on GOG Achievements Manager by Timendum
 * https://github.com/timendum/gog_achievements
 * https://github.com/timendum/gog-achievements-gui
 *
 * It reimplements in C# the calls those tools make to GOG's services: listing, and
 * unlocking and re-locking. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Text.Json.Serialization;

namespace AchievementCaretaker.Providers.Gog.Models;

/// <summary>
/// The signed-in GOG account's tokens. The field names are GOG's, so the
/// token response deserializes straight into this and the file on disk reads
/// the same; <see cref="ExpiresAt"/> and <see cref="DisplayName"/> are ours.
/// </summary>
/// <remarks>
/// GOG rotates the refresh token on every refresh, so the saved one must be
/// replaced each time or the next refresh fails. See
/// <see cref="ISessionStore"/> for how the file is protected.
/// </remarks>
public sealed record GogSession
{
    // A record prints every property in ToString, and these hold tokens: print nothing.
    private bool PrintMembers(System.Text.StringBuilder builder) => false;

    /// <summary>The token that authorises requests to GOG's services.</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    /// <summary>The credential that gets a new access token; replaced on every refresh.</summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }

    /// <summary>The GOG account's id, which a saved library is keyed by.</summary>
    [JsonPropertyName("user_id")]
    public string UserId { get; set; }

    /// <summary>
    /// Seconds the access token lasts, as the token response says. Only used
    /// to compute <see cref="ExpiresAt"/>, which is what gets saved and read.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>When <see cref="AccessToken"/> stops working.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The account's name, for the rail.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    /// <summary>Whether <see cref="AccessToken"/> can be used now, with a minute of slack so a token is not used just as it expires in flight.</summary>
    [JsonIgnore]
    public bool IsAccessTokenValid =>
        string.IsNullOrEmpty(AccessToken) == false &&
        ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

    /// <summary>
    /// GOG does not say when a refresh token expires, so the only thing known
    /// is whether there is one. A stale one shows up as a failed refresh.
    /// </summary>
    [JsonIgnore]
    public bool HasRefreshToken => string.IsNullOrEmpty(RefreshToken) == false;
}
