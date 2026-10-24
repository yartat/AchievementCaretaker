/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers;

/// <summary>
/// The account a store is used as: signing in and out, and who is signed in. What the store
/// has to offer that account is <see cref="IStoreProvider"/>'s business, not this one's.
/// </summary>
/// <remarks>
/// One object per store implements both, so the account that is signed in here is the one
/// the provider reads for. Whatever only signs in (the sign-in window, the session service)
/// asks for this interface alone; whatever only reads asks for the provider alone.
/// </remarks>
public interface IStoreAccount : IStore
{
    /// <summary>Where the user signs in, in their own browser.</summary>
    string SignInUrl { get; }

    /// <summary>What to copy back from the browser after signing in.</summary>
    string SignInHint { get; }

    /// <summary>
    /// What an address the sign-in page lands on means, so a browser embedded in the
    /// application can finish the sign-in without the user pasting anything. Each
    /// store ends its sign-in differently, so each says how.
    /// </summary>
    SignInResultKind GetSignInResultKind(Uri address);

    /// <summary>True when a saved session exists that can still be refreshed.</summary>
    bool IsSignedIn { get; }

    /// <summary>The signed-in account's name, or null.</summary>
    string AccountName { get; }

    /// <summary>
    /// A stable id for the signed-in account, or null when nobody is. What a library
    /// and its progress are saved under, so each account keeps its own.
    /// </summary>
    string AccountId { get; }

    /// <summary>
    /// Completes sign-in from what the user pasted: the bare code, or the whole
    /// page or address the store ended on.
    /// </summary>
    Task SignInAsync(string pasted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure there is a usable access token, refreshing it if needed.
    /// Throws <see cref="ProviderException"/> with <c>login_required</c> when
    /// the user has to sign in again.
    /// </summary>
    Task EnsureSignedInAsync(CancellationToken cancellationToken = default);

    void SignOut();
}
