/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

namespace AchievementCaretaker.Services.Models;

/// <summary>A question to put to the user before something they may not want.</summary>
/// <param name="Title">The dialog's title.</param>
/// <param name="Message">What is about to happen and what it costs.</param>
public sealed record StoreConfirmation(string Title, string Message);
