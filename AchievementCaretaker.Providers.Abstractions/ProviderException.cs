/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Providers;

public sealed class ProviderException : Exception
{
    public string ErrorCode { get; }

    public ProviderException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
