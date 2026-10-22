/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers.Models;

/// <summary>
/// One owned game on a store.
/// </summary>
/// <remarks>
/// <see cref="Id"/> identifies the game within its store. <see cref="Key"/> is
/// what that store's achievement service is addressed by, and the two are not
/// always the same thing: on Epic a namespace can hold several games, and on
/// GOG the product id is only turned into the client id its achievements are
/// addressed by when they are asked for. Callers hand <see cref="Key"/> back
/// to <see cref="IStoreProvider.GetAchievementsAsync"/> without looking inside
/// it.
/// </remarks>
/// <param name="Provider">The id of the provider the game belongs to (<see cref="IStoreProvider.Id"/>).</param>
/// <param name="Id">The game's identifier within its store, stable across refreshes.</param>
/// <param name="Key">What the store's achievement service is addressed by; opaque to callers.</param>
/// <param name="Title">The name to show.</param>
/// <param name="ImageUrl">The address of the game's capsule art, or null when the store has none.</param>
public sealed record StoreGame(
    string Provider,
    string Id,
    string Key,
    string Title,
    string ImageUrl);
