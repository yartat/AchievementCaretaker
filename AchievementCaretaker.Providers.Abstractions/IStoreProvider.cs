/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.Providers;

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
/// that is not part of it. Signing in and out is <see cref="IStoreAccount"/>; a
/// call here that finds the session gone throws <see cref="ProviderException"/>
/// with <c>login_required</c>.
/// </para>
/// </remarks>
public interface IStoreProvider : IStore
{
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
