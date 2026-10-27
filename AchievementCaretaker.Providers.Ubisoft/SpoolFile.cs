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
using System.Globalization;
using System.IO;
using System.Linq;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// A game's spool: the achievements the launcher has recorded as earned on this computer, in
/// <c>spool\&lt;user id&gt;\&lt;game id&gt;.spool</c>.
/// </summary>
/// <remarks>
/// <para>
/// Protocol buffers again: repeated records (field 1), each holding the achievement's id as a varint one
/// message down (field 1) and the time as a varint of its own (field 2). Only what was earned is in it:
/// a game with no file, or an achievement with no record, is not earned <em>here</em>, which cannot be
/// told from earned on another computer, and the launcher does not say.
/// </para>
/// <para>
/// This is read only. UbiSlot writes it to unlock achievements, and the launcher then syncs them to the
/// account; Caretaker does not.
/// </para>
/// </remarks>
public static class SpoolFile
{
    // A time below this is in seconds, above it in milliseconds: no moment this century is in the
    // seconds range that large, and none is in the milliseconds range that small.
    private const long MillisecondsThreshold = 10_000_000_000;

    /// <summary>
    /// The records in a spool, one per achievement (the earliest if it is there twice), oldest first. What
    /// cannot be read ends the list.
    /// </summary>
    /// <param name="file">The whole file.</param>
    public static IReadOnlyList<SpoolRecord> Read(ReadOnlySpan<byte> file)
    {
        Dictionary<uint, SpoolRecord> byId = new();
        var position = 0;
        try
        {
            while (position < file.Length)
            {
                var tag = Protobuf.ReadVarint(file, ref position);
                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);

                if (number == 1 && wireType == 2)
                {
                    var length = Protobuf.ReadVarint(file, ref position);
                    if (length > (ulong)(file.Length - position))
                    {
                        break;
                    }

                    var record = file.Slice(position, (int)length);
                    position += (int)length;

                    var id = Protobuf.FindFirstVarint(record, 1);
                    var time = Protobuf.FindFirstVarint(record, 2);
                    if (id is > 0 and <= uint.MaxValue && time is > 0 and <= long.MaxValue &&
                        TryToDate((long)time.Value, out var earned) == true)
                    {
                        var key = (uint)id.Value;
                        if (byId.TryGetValue(key, out var known) == false || earned < known.EarnedAt)
                        {
                            byId[key] = new SpoolRecord(key, earned);
                        }
                    }
                }
                else
                {
                    Protobuf.Skip(file, ref position, wireType);
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            // What was read before the file stopped making sense still counts.
        }

        return byId.Values.OrderBy(record => record.EarnedAt).ThenBy(record => record.AchievementId).ToList();
    }

    /// <summary>
    /// The current user's spool for a game, or an empty list when there is none (nothing earned on this
    /// computer) or it cannot be read.
    /// </summary>
    /// <param name="locations">Where the launcher keeps its files.</param>
    /// <param name="userId">The user the spool belongs to, as <see cref="OwnershipCache.UserId"/> gives it.</param>
    /// <param name="gameId">The game.</param>
    public static IReadOnlyList<SpoolRecord> ReadForGame(UbisoftLocations locations, string userId, uint gameId)
    {
        var path = Path.Combine(
            locations.SpoolDirectory,
            userId,
            gameId.ToString(CultureInfo.InvariantCulture) + ".spool");
        try
        {
            return File.Exists(path) == true ? Read(File.ReadAllBytes(path)) : Array.Empty<SpoolRecord>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<SpoolRecord>();
        }
    }

    // The last moment a DateTimeOffset can hold, in milliseconds.
    private const long LastMillisecond = 253_402_300_799_999;

    internal static bool TryToDate(long value, out DateTimeOffset date)
    {
        if (value < MillisecondsThreshold)
        {
            date = DateTimeOffset.FromUnixTimeSeconds(value);
            return true;
        }

        // Garbage that is no moment at all is no record.
        if (value > LastMillisecond)
        {
            date = default;
            return false;
        }

        date = DateTimeOffset.FromUnixTimeMilliseconds(value);
        return true;
    }
}
