/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers;

/// <summary>
/// An <see cref="IStoreAccount"/> that is not signed in to by Caretaker but found on this
/// computer: the store's own client is signed in, and its files say who to.
/// </summary>
/// <remarks>
/// There is no page to sign in on and nothing to sign out of, so the library window shows
/// <see cref="UnavailableMessage"/> and a way to look again where it would otherwise offer a sign-in,
/// and offers neither Switch account nor Sign out. Ubisoft Connect is one: its ownership cache, its
/// achievement archives and its spool are read where the launcher keeps them.
/// </remarks>
public interface ILocalStoreAccount : IStoreAccount
{
    /// <summary>
    /// Why the store cannot be read, in words for the user and what to do about it. Shown while
    /// <see cref="IStoreAccount.IsSignedIn"/> is false.
    /// </summary>
    string UnavailableMessage { get; }

    /// <summary>
    /// Whether the account has also been signed in to by Caretaker, for what the store's files do not
    /// have (what was earned on other computers). Signing in is optional for such a store: false means it
    /// is read from this computer only, and <see cref="IStoreAccount.SignInUrl"/> is empty when there is no
    /// way to sign in at all.
    /// </summary>
    bool IsSignedInOnline => false;
}
