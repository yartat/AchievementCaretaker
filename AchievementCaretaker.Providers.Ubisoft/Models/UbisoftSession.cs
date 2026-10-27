/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on galaxy-integration-uplay by melcom (MIT)
 * https://github.com/melcom-creations/galaxy-integration-uplay
 * which shows what Ubisoft's sign-in page leaves behind and how it is renewed. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Text.Json.Serialization;

namespace AchievementCaretaker.Providers.Ubisoft.Models;

/// <summary>
/// The Ubisoft account Caretaker is signed in to: the session Ubisoft's sign-in page made, and the
/// longer-lived "remember me" ticket that makes the next one. The field names are the ones Ubisoft's
/// services use, so a session response deserializes straight into this and the file reads the same.
/// </summary>
/// <remarks>
/// A ticket is a credential for the whole account. See <see cref="ISessionStore"/> for how the file is
/// protected; a record prints every property in <c>ToString</c>, so this prints none.
/// </remarks>
public sealed record UbisoftSession
{
    private bool PrintMembers(System.Text.StringBuilder builder) => false;

    /// <summary>The session's ticket, which authorises requests and the socket service.</summary>
    [JsonPropertyName("ticket")]
    public string? Ticket { get; set; }

    /// <summary>The session's id, sent with the ticket.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>The account's id, which a saved library is keyed by.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>The account's name, for the rail.</summary>
    [JsonPropertyName("nameOnPlatform")]
    public string? UserName { get; set; }

    /// <summary>The ticket that makes a new session when this one has expired; replaced whenever Ubisoft sends another.</summary>
    [JsonPropertyName("rememberMeTicket")]
    public string? RememberMeTicket { get; set; }

    /// <summary>When <see cref="Ticket"/> stops working, as Ubisoft's clock and ours agree.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Whether <see cref="Ticket"/> can be used now, with five minutes of slack for the requests that follow.</summary>
    [JsonIgnore]
    public bool IsTicketValid =>
        string.IsNullOrEmpty(Ticket) == false &&
        string.IsNullOrEmpty(SessionId) == false &&
        ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);

    /// <summary>Whether there is a way to get a new session without the user.</summary>
    [JsonIgnore]
    public bool CanRenew => string.IsNullOrEmpty(RememberMeTicket) == false || string.IsNullOrEmpty(Ticket) == false;
}
