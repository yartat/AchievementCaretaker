/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services.Models;

/// <summary>What a sign-in came to: signed in, or a sentence saying why not.</summary>
/// <param name="SignedIn">The store accepted the sign-in.</param>
/// <param name="Error">A sentence for the user when <paramref name="SignedIn"/> is false; otherwise null.</param>
public sealed record StoreSignInResult(bool SignedIn, string Error);
