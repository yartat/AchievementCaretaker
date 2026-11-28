/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;

namespace AchievementCaretaker.Services;

/// <summary>Which image addresses may be read, and how.</summary>
/// <remarks>
/// An address comes from a store's server, the Steam helper or a saved row. A <c>file:</c> one
/// naming another computer would make Windows sign in to it, and one naming a local file would
/// read whatever it says. So files are read only for Ubisoft Connect, which extracts its icons
/// itself, and only from that folder; everything else is downloaded, and only over https.
/// </remarks>
public static class ImageUrlPolicy
{
    private const string LocalProvider = "ubisoft";

    /// <summary>The file behind an address, when it may be read from this computer.</summary>
    /// <param name="provider">The store the address came from.</param>
    /// <param name="url">The address.</param>
    /// <param name="localIconDirectory">The one folder local images are read from.</param>
    /// <param name="path">The file.</param>
    public static bool TryGetLocalFile(string provider, string url, string localIconDirectory, out string path)
    {
        path = "";
        if (provider != LocalProvider ||
            Uri.TryCreate(url, UriKind.Absolute, out var address) == false ||
            address.IsFile == false ||
            address.IsUnc == true)
        {
            return false;
        }

        var full = Path.GetFullPath(address.LocalPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localIconDirectory)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.StartsWith(root, comparison) == false)
        {
            return false;
        }

        path = full;
        return true;
    }

    /// <summary>The address, when it may be downloaded.</summary>
    /// <param name="url">The address.</param>
    /// <param name="address">The address, parsed.</param>
    public static bool TryGetDownload(string url, out Uri address)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out address!) == true && address.Scheme == Uri.UriSchemeHttps;
    }
}
