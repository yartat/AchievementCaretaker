/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.IO;
using System.Runtime.Versioning;

namespace AchievementCaretaker.Providers;

/// <summary>Writes a file so that it is either the old content or the new, never a part of one.</summary>
/// <remarks>
/// The content goes into <c>path + ".tmp"</c>, is flushed to the disk and then renamed over the
/// file, which is atomic on one volume. A crash leaves the old file. A symbolic link is followed,
/// so it stays a link and its target is replaced. The file is a new one: on Windows it takes the
/// folder's permissions, not those set on the file it replaces.
/// </remarks>
public static class AtomicFile
{
    /// <summary>Replaces the file's content.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="data">Its new content.</param>
    public static void WriteAllBytes(string path, byte[] data)
    {
        Write(path, data, new FileStreamOptions() { Mode = FileMode.CreateNew, Access = FileAccess.Write });
    }

    /// <summary>Replaces the file's content, the new file having these permissions from the moment it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="data">Its new content.</param>
    /// <param name="mode">The permissions the file is created with.</param>
    [UnsupportedOSPlatform("windows")]
    public static void WriteAllBytes(string path, byte[] data, UnixFileMode mode)
    {
        Write(path, data, new FileStreamOptions() { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = mode });
    }

    private static void Write(string path, byte[] data, FileStreamOptions options)
    {
        if (new FileInfo(path).LinkTarget != null)
        {
            path = File.ResolveLinkTarget(path, true)!.FullName;
        }

        var temporary = path + ".tmp";
        // One left by a crash: the create mode only applies to a file that is made now.
        File.Delete(temporary);
        try
        {
            using (FileStream stream = new(temporary, options))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        catch
        {
            if (File.Exists(temporary) == true)
            {
                File.Delete(temporary);
            }
            throw;
        }
    }
}
