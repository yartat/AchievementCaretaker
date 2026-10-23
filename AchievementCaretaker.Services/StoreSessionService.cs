/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Services.Models;

namespace AchievementCaretaker.Services;

/// <summary>The <see cref="IStoreSessionService"/>. Stateless.</summary>
public sealed class StoreSessionService : IStoreSessionService
{
    public async Task<StoreSignInResult> SignInAsync(
        IStoreProvider provider, string pasted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pasted) == true)
        {
            return new StoreSignInResult(false, $"Paste {provider.SignInHint} first.");
        }

        try
        {
            await provider.SignInAsync(pasted, cancellationToken);
            return new StoreSignInResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProviderException e)
        {
            return new StoreSignInResult(false, e.Message);
        }
        catch (Exception e)
        {
            return new StoreSignInResult(false, $"Could not reach {provider.DisplayName}: {e.Message}");
        }
    }

    public void SignOut(IStoreProvider provider) => provider.SignOut();

    public StoreConfirmation ConfirmSignOut(IStoreProvider provider) => new(
        "Sign out",
        $"Sign out of {provider.DisplayName}{(provider.AccountName == null ? "" : " as " + provider.AccountName)}? " +
        "Its games and achievement counts stay saved on this computer and come back when you sign in again. " +
        $"Nothing changes on {provider.DisplayName}.");

    public StoreConfirmation ConfirmSwitchAccount(IStoreProvider provider) => new(
        "Switch account",
        $"Sign in to a different {provider.DisplayName} account? Its games and achievements replace " +
        $"{provider.AccountName ?? "the current account"}'s on screen once you have signed in. " +
        "What is saved for each account stays on this computer, and the current account stays signed in " +
        "until the new sign-in succeeds.");

    public string SessionExpiredMessage(IStoreProvider provider) =>
        $"Your {provider.DisplayName} session has expired. Sign in again.";
}
