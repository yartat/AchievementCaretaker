/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */
using System;

namespace AchievementCaretaker.SteamApi;

/// <summary>Steam could not be reached: <see cref="Failure"/> says at which step.</summary>
public class ClientInitializeException : Exception
{
    /// <summary>The step that failed.</summary>
    public readonly ClientInitializeFailure Failure;

    /// <summary>A failure with no message of its own.</summary>
    /// <param name="failure">The step that failed.</param>
    public ClientInitializeException(ClientInitializeFailure failure)
    {
        Failure = failure;
    }

    /// <summary>A failure with a message.</summary>
    /// <param name="failure">The step that failed.</param>
    /// <param name="message">What went wrong.</param>
    public ClientInitializeException(ClientInitializeFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>A failure with a message and its cause.</summary>
    /// <param name="failure">The step that failed.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused it.</param>
    public ClientInitializeException(ClientInitializeFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
