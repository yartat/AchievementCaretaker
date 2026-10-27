/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UbiSlot by BalTor02 (MIT)
 * https://github.com/BalTor02/UbiSlot
 * and on Achievements by PSerban93 (MIT)
 * https://github.com/PSerban93/Achievements
 *
 * It reads the same local Ubisoft Connect files they do: the ownership cache, the
 * achievement archives and the spool. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// The launcher's ownership cache: a file per user, named by the user's id, in
/// <see cref="UbisoftLocations.OwnershipDirectory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The file is a fixed-size header of 264 bytes that is not understood and not needed, then protocol
/// buffers: repeated records (field 1), each with two varints (fields 1 and 2) that are ids. Which of
/// the two is the product and which something it belongs to is not known, so both are taken, and the
/// ones that are games are told from the rest by whether a name is known for them.
/// </para>
/// <para>
/// This is what the launcher keeps for itself, not an interface, and a launcher update can change it.
/// </para>
/// </remarks>
public static class OwnershipFile
{
    private const int HeaderLength = 264;

    /// <summary>Every id the cache mentions, sorted and without repeats. Empty for a file that is not one.</summary>
    /// <param name="file">The whole file.</param>
    public static IReadOnlyList<uint> ReadIds(ReadOnlySpan<byte> file)
    {
        SortedSet<uint> ids = new();
        if (file.Length <= HeaderLength)
        {
            return ids.ToList();
        }

        var data = file[HeaderLength..];
        var position = 0;
        try
        {
            while (position < data.Length)
            {
                var tag = Protobuf.ReadVarint(data, ref position);
                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);

                if (number == 1 && wireType == 2)
                {
                    var length = Protobuf.ReadVarint(data, ref position);
                    if (length > (ulong)(data.Length - position))
                    {
                        break;
                    }

                    ReadRecord(data.Slice(position, (int)length), ids);
                    position += (int)length;
                }
                else
                {
                    Protobuf.Skip(data, ref position, wireType);
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            // What was read before the file stopped making sense still counts.
        }

        return ids.ToList();
    }

    /// <summary>
    /// The current user's ownership cache: the file in <paramref name="locations"/> whose name is a
    /// user id (a GUID), the newest if there are several, or null when there is none or it cannot be read.
    /// </summary>
    /// <param name="locations">Where the launcher keeps its files.</param>
    public static OwnershipCache? Find(UbisoftLocations locations)
    {
        string? newest = null;
        var newestWritten = DateTime.MinValue;
        try
        {
            if (Directory.Exists(locations.OwnershipDirectory) == false)
            {
                return null;
            }

            foreach (var file in Directory.EnumerateFiles(locations.OwnershipDirectory))
            {
                if (Guid.TryParse(Path.GetFileName(file), out _) == false)
                {
                    continue;
                }

                var written = File.GetLastWriteTimeUtc(file);
                if (newest == null || written > newestWritten)
                {
                    newest = file;
                    newestWritten = written;
                }
            }

            return newest == null
                ? null
                : new OwnershipCache(Path.GetFileName(newest), ReadIds(File.ReadAllBytes(newest)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The launcher may have the file open, or it is not ours to read: no cache to go by.
            return null;
        }
    }

    private static void ReadRecord(ReadOnlySpan<byte> record, SortedSet<uint> ids)
    {
        var position = 0;
        while (position < record.Length)
        {
            var tag = Protobuf.ReadVarint(record, ref position);
            var number = (int)(tag >> 3);
            var wireType = (int)(tag & 7);

            if (wireType == 0 && number is 1 or 2)
            {
                var value = Protobuf.ReadVarint(record, ref position);
                if (value <= uint.MaxValue)
                {
                    ids.Add((uint)value);
                }
            }
            else
            {
                Protobuf.Skip(record, ref position, wireType);
            }
        }
    }
}
