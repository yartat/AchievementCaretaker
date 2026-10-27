/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Providers;

/// <summary>
/// Represents an error that occurred while interacting with a game store provider.
/// </summary>
/// <param name="errorCode">The error code associated with the exception, which can be used to identify the specific error condition.</param>
/// <param name="message">The message that describes the error.</param>
public sealed class ProviderException(string errorCode, string message) : Exception(message)
{
    /// <summary>
    /// The error code associated with the exception, which can be used to identify the specific error condition.
    /// </summary>
    public string ErrorCode { get; } = errorCode;
}
