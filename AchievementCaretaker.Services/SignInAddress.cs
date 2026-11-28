/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;

namespace AchievementCaretaker.Services;

/// <summary>What the sign-in window says about the page it is showing.</summary>
public static class SignInAddress
{
    /// <summary>
    /// The host of the page, marked by whether it is encrypted. A name in other letters is shown in its
    /// ASCII (<c>xn--</c>) form, so it cannot pass for the one it imitates.
    /// </summary>
    /// <param name="address">Where the page is.</param>
    public static string Describe(Uri? address)
    {
        if (address == null || address.IsAbsoluteUri == false || string.IsNullOrEmpty(address.Host) == true)
        {
            return "";
        }
        return address.Scheme == Uri.UriSchemeHttps
            ? "\U0001F512 " + address.IdnHost
            : "Not secure: " + address.IdnHost;
    }
}
