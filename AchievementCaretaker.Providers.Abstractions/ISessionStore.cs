/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Providers;

/// <summary>
/// Where a provider keeps its saved sign-in between runs.
/// </summary>
/// <remarks>
/// A session holds a refresh token, which is a credential for the whole store account.
/// How it is protected on disk depends on the operating system, so that is decided
/// once, where the store is registered, and nothing that uses it asks which system it is
/// on. Nothing behind this interface logs or prints a token.
/// </remarks>
public interface ISessionStore
{
    /// <summary>Returns null when there is no usable session at <paramref name="path"/>.</summary>
    T Load<T>(string path)
        where T : class;

    void Save<T>(string path, T session);

    void Delete(string path);
}
