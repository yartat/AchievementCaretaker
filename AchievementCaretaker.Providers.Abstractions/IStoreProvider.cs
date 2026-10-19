/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers
{
    /// <summary>
    /// A game store whose library and achievements can be read: Epic Games, GOG.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Steam is deliberately not a provider. Its interface is native, scoped to
    /// one app id per process, and can write; a store reached over HTTPS with the
    /// user's own sign-in has none of those properties. Putting Steam behind this
    /// interface would hide exactly the constraints that shape how it is used.
    /// </para>
    /// <para>
    /// The interface is read-only. Whatever a store would let a client do beyond
    /// that is not part of it.
    /// </para>
    /// </remarks>
    public interface IStoreProvider
    {
        /// <summary>Stable lower-case id: names the session file and the command line.</summary>
        string Id { get; }

        string DisplayName { get; }

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

        /// <summary>Every owned game; DLC and non-game items are left out.</summary>
        Task<IReadOnlyList<StoreGame>> GetLibraryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// A game's achievements with the signed-in player's progress, given a
        /// <see cref="StoreGame.Key"/>. An empty list means the game has none.
        /// </summary>
        Task<IReadOnlyList<StoreAchievement>> GetAchievementsAsync(
            string gameKey,
            string locale = "en",
            CancellationToken cancellationToken = default);
    }
}
