/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Providers.Models;

/// <summary>
/// One owned game on a store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> identifies the game within its store. <see cref="Key"/> is
/// what that store's achievement service is addressed by, and the two are not
/// always the same thing: on Epic a namespace can hold several games, and on
/// GOG the product id is only turned into the client id its achievements are
/// addressed by when they are asked for. Callers hand <see cref="Key"/> back
/// to <see cref="IStoreProvider.GetAchievementsAsync"/> without looking inside
/// it.
/// </para>
/// <para>
/// The details after <see cref="ImageUrl"/> are what a store happens to know about a game when it
/// lists it, and are null when it does not: unknown is not zero. Epic and GOG list a title and art
/// and nothing more; Steam, whose client and cache files know a good deal, fills in the rest.
/// </para>
/// </remarks>
/// <param name="Provider">The id of the provider the game belongs to (<see cref="IStoreProvider.Id"/>).</param>
/// <param name="Id">The game's identifier within its store, stable across refreshes.</param>
/// <param name="Key">What the store's achievement service is addressed by; opaque to callers.</param>
/// <param name="Title">The name to show.</param>
/// <param name="ImageUrl">The address of the game's capsule art, or null when the store has none.</param>
/// <param name="Type">What kind of app it is ("normal", "demo", "mod", "junk"), or null when the store does not sort them.</param>
/// <param name="ReleaseDate">When it was released, if the store says.</param>
/// <param name="RatingPercent">The percentage of positive reviews, if the store has reviews.</param>
/// <param name="RatingScore">The store's own band for the reviews (Steam's 1-9), if it has one.</param>
/// <param name="PlaytimeMinutes">Minutes the account has played it, if the store knows.</param>
/// <param name="LastPlayed">When it was last played, if the store knows.</param>
/// <param name="AchievementsTotal">How many achievements it has, if the listing already says.</param>
/// <param name="AchievementsEarned">How many of them the account has earned, if the listing already says.</param>
public sealed record StoreGame(
    string Provider,
    string Id,
    string Key,
    string Title,
    string ImageUrl,
    string Type = null,
    DateTimeOffset? ReleaseDate = null,
    int? RatingPercent = null,
    int? RatingScore = null,
    int? PlaytimeMinutes = null,
    DateTimeOffset? LastPlayed = null,
    int? AchievementsTotal = null,
    int? AchievementsEarned = null);
