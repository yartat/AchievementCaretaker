/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Providers
{
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
    public sealed record StoreGame(
        string Provider,
        string Id,
        string Key,
        string Title,
        string ImageUrl);

    /// <summary>
    /// One achievement: the definition merged with the signed-in player's state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fields a store does not have are null rather than zero, because unknown and
    /// zero are different answers. Epic has XP and progress; GOG has neither.
    /// </para>
    /// <para>
    /// <see cref="Protected"/> marks an achievement the store will not let anyone set (Steam's
    /// permission bits); the window shows it but does not offer to change it.
    /// </para>
    /// <para>
    /// <see cref="Unlocked"/> false with a null <see cref="UnlockDate"/> means "not
    /// earned". A game the store has no record for comes back with every
    /// achievement in that state, which cannot be told from a game played without
    /// earning anything; neither store says which.
    /// </para>
    /// </remarks>
    public sealed record StoreAchievement(
        string Id,
        string DisplayName,
        string Description,
        string IconUrl,
        bool Hidden,
        int? XP,
        double? RarityPercent,
        bool Unlocked,
        double? Progress,
        DateTimeOffset? UnlockDate,
        bool Protected = false);

    /// <summary>What an address reached while signing in means.</summary>
    public enum SignInResultKind
    {
        /// <summary>Just a page on the way.</summary>
        None = 0,

        /// <summary>The address itself carries the result (GOG's redirect with <c>code=</c>).</summary>
        Address = 1,

        /// <summary>The page's text carries the result (Epic's small JSON document).</summary>
        PageText = 2,
    }

    public sealed class ProviderException : Exception
    {
        public string ErrorCode { get; }

        public ProviderException(string errorCode, string message)
            : base(message)
        {
            this.ErrorCode = errorCode;
        }
    }
}
