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
using System.IO;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>
/// The little of the protocol buffers wire format that the launcher's files need: varints, and skipping
/// what is not wanted. The files have no schema to hand, so they are read by field number.
/// </summary>
internal static class Protobuf
{
    private const int MaximumNesting = 4;

    /// <summary>Reads a varint, moving <paramref name="position"/> past it.</summary>
    /// <exception cref="InvalidDataException">The varint is longer than 64 bits.</exception>
    /// <exception cref="EndOfStreamException">The data ends inside it.</exception>
    public static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong result = 0;
        var shift = 0;
        while (position < data.Length)
        {
            var current = data[position++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift >= 64)
            {
                throw new InvalidDataException("Invalid varint.");
            }
        }

        throw new EndOfStreamException("The data ends inside a varint.");
    }

    /// <summary>Moves <paramref name="position"/> past a field of the given wire type whose tag has been read.</summary>
    /// <exception cref="InvalidDataException">The wire type is not one this format uses.</exception>
    /// <exception cref="EndOfStreamException">The field runs past the end of the data.</exception>
    public static void Skip(ReadOnlySpan<byte> data, ref int position, int wireType)
    {
        switch (wireType)
        {
            case 0:
                ReadVarint(data, ref position);
                break;
            case 1:
                Advance(data, ref position, 8);
                break;
            case 2:
                var length = ReadVarint(data, ref position);
                if (length > int.MaxValue)
                {
                    throw new InvalidDataException("Invalid length.");
                }
                Advance(data, ref position, (int)length);
                break;
            case 5:
                Advance(data, ref position, 4);
                break;
            default:
                throw new InvalidDataException($"Unsupported wire type {wireType}.");
        }
    }

    /// <summary>
    /// The first varint whose field number is <paramref name="field"/>, looking inside nested messages
    /// too (a few levels deep), or null. A record's id can sit one message down from its time, and
    /// which depends on the file, so it is found by number rather than by position.
    /// </summary>
    public static ulong? FindFirstVarint(ReadOnlySpan<byte> data, int field, int depth = 0)
    {
        var position = 0;
        while (position < data.Length)
        {
            var tag = ReadVarint(data, ref position);
            var number = (int)(tag >> 3);
            var wireType = (int)(tag & 7);

            if (wireType == 0)
            {
                var value = ReadVarint(data, ref position);
                if (number == field)
                {
                    return value;
                }
            }
            else if (wireType == 2)
            {
                var length = ReadVarint(data, ref position);
                if (length > (ulong)(data.Length - position))
                {
                    throw new EndOfStreamException("A field runs past the end of the data.");
                }

                if (depth < MaximumNesting)
                {
                    var nested = FindFirstVarint(data.Slice(position, (int)length), field, depth + 1);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
                position += (int)length;
            }
            else
            {
                Skip(data, ref position, wireType);
            }
        }

        return null;
    }

    private static void Advance(ReadOnlySpan<byte> data, ref int position, int count)
    {
        if (count < 0 || position > data.Length - count)
        {
            throw new EndOfStreamException("A field runs past the end of the data.");
        }
        position += count;
    }
}
