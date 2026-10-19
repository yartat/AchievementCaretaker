/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Services
{
    /// <summary>What a sign-in came to: signed in, or a sentence saying why not.</summary>
    public sealed record StoreSignInResult(bool SignedIn, string Error);

    /// <summary>A question to put to the user before something they may not want.</summary>
    public sealed record StoreConfirmation(string Title, string Message);

    /// <summary>
    /// Signing in to, and out of, a store: what the provider is asked, what is said when it
    /// goes wrong, and what the user is asked first. The window owns the browser, the
    /// dialogs and what is on screen.
    /// </summary>
    public interface IStoreSessionService
    {
        /// <summary>
        /// Signs in with what the store's page ended on, or what the user pasted. A problem
        /// comes back as <see cref="StoreSignInResult.Error"/>; cancellation throws.
        /// </summary>
        Task<StoreSignInResult> SignInAsync(IStoreProvider provider, string pasted, CancellationToken cancellationToken);

        /// <summary>Drops the session. Nothing saved for the account is touched.</summary>
        void SignOut(IStoreProvider provider);

        StoreConfirmation ConfirmSignOut(IStoreProvider provider);

        StoreConfirmation ConfirmSwitchAccount(IStoreProvider provider);

        /// <summary>What to say when a request finds the session no longer valid.</summary>
        string SessionExpiredMessage(IStoreProvider provider);
    }
}
