/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 * whose protocol definitions (Uplay-Protobufs) it follows by hand, field number by field number.
 * See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>One field of a protocol buffers message: a varint or a length-delimited value (a string, bytes or a message).</summary>
/// <param name="Number">The field number.</param>
/// <param name="Varint">The value, for a varint field.</param>
/// <param name="Bytes">The value, for a length-delimited field; null for a varint.</param>
internal readonly record struct ProtoField(int Number, ulong Varint, byte[]? Bytes)
{
    /// <summary>The value as text, for a string field.</summary>
    public string Text => Bytes == null ? "" : Encoding.UTF8.GetString(Bytes);
}

/// <summary>
/// Writes and reads the few messages of Ubisoft's service protocols that are used, by field number: the
/// definitions are small and fixed, so there is no generated code and no package for them.
/// </summary>
internal sealed class ProtoMessage
{
    private readonly MemoryStream _bytes = new();

    /// <summary>Adds a varint field.</summary>
    public ProtoMessage Varint(int number, ulong value)
    {
        WriteVarint(((ulong)number << 3) | 0);
        WriteVarint(value);
        return this;
    }

    /// <summary>Adds a string field.</summary>
    public ProtoMessage Text(int number, string value) => Bytes(number, Encoding.UTF8.GetBytes(value));

    /// <summary>Adds a length-delimited field.</summary>
    public ProtoMessage Bytes(int number, ReadOnlySpan<byte> value)
    {
        WriteVarint(((ulong)number << 3) | 2);
        WriteVarint((ulong)value.Length);
        _bytes.Write(value);
        return this;
    }

    /// <summary>Adds a nested message.</summary>
    public ProtoMessage Message(int number, ProtoMessage value) => Bytes(number, value.ToArray());

    /// <summary>The message so far.</summary>
    public byte[] ToArray() => _bytes.ToArray();

    /// <summary>
    /// The fields of a message, in order. Fixed-width fields are skipped: none of the messages read here has one
    /// that matters. What cannot be read ends the list.
    /// </summary>
    /// <param name="message">The message.</param>
    public static List<ProtoField> Read(ReadOnlySpan<byte> message)
    {
        List<ProtoField> fields = new();
        var position = 0;
        try
        {
            while (position < message.Length)
            {
                var tag = Protobuf.ReadVarint(message, ref position);
                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);

                if (wireType == 0)
                {
                    fields.Add(new ProtoField(number, Protobuf.ReadVarint(message, ref position), null));
                }
                else if (wireType == 2)
                {
                    var length = Protobuf.ReadVarint(message, ref position);
                    if (length > (ulong)(message.Length - position))
                    {
                        break;
                    }
                    fields.Add(new ProtoField(number, 0, message.Slice(position, (int)length).ToArray()));
                    position += (int)length;
                }
                else
                {
                    Protobuf.Skip(message, ref position, wireType);
                }
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            // What was read before the message stopped making sense still counts.
        }

        return fields;
    }

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _bytes.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _bytes.WriteByte((byte)value);
    }
}
