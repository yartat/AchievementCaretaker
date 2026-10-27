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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AchievementCaretaker.SteamApi;

/// <summary>
/// This class represents a key value pair in the Steam API. A key value pair consists of a name, a type, and a value. The type can be one of the following:
/// </summary>
public class KeyValue
{
    private static readonly KeyValue _invalid = new();

    /// <summary>
    /// The name of this key value. The root key value has the name "root".
    /// </summary>
    public string Name = "<root>";

    /// <summary>
    /// The type of this key value. If the type is None, then the value is a list of child key values.
    /// </summary>
    public KeyValueType Type = KeyValueType.None;

    /// <summary>
    /// The value of this key value. If the type is None, then this value is null.
    /// </summary>
    public object? Value;

    /// <summary>
    /// Is this key value valid? If the key value is invalid, then the name, type, and value are undefined.
    /// </summary>
    public bool Valid;

    /// <summary>
    /// The list of child key values. If the type is not None, then this list is null.
    /// </summary>
    public List<KeyValue>? Children = null;

    /// <summary>
    /// Indexer to get a child key value by name. If the child key value does not exist, then an invalid key value is returned.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>Returns the child key value with the specified name, or an invalid key value if the child key value does not exist.</returns>
    public KeyValue this[string key]
    {
        get
        {
            if (Children == null)
            {
                return _invalid;
            }

            var child = Children.SingleOrDefault(
                c => string.Compare(c.Name, key, StringComparison.InvariantCultureIgnoreCase) == 0);

            if (child == null)
            {
                return _invalid;
            }

            return child;
        }
    }

    /// <summary>
    /// Tries to get the value of this key value as a string. If the key value is invalid, then the default value is returned. If the key value is valid but the value is null, then the default value is returned. Otherwise, the value is converted to a string and returned.
    /// </summary>
    /// <param name="defaultValue">The default value to return if the key value is invalid or the value is null.</param>
    /// <returns>Returns the value of this key value as a string, or the default value if the key value is invalid or the value is null.</returns>
    public string? AsString(string defaultValue)
    {
        if (Valid == false)
        {
            return defaultValue;
        }

        if (Value == null)
        {
            return defaultValue;
        }

        return Value?.ToString();
    }

    /// <summary>
    /// Returns the value of this key value as an integer. If the key value is invalid, then the default value is returned. If the key value is valid but the value is null, then the default value is returned. Otherwise, the value is converted to an integer and returned.
    /// </summary>
    /// <param name="defaultValue">The default value to return if the key value is invalid or the value is null.</param>
    /// <returns>Returns the value of this key value as an integer, or the default value if the key value is invalid or the value is null.</returns>
    public int AsInteger(int defaultValue)
    {
        if (Valid == false)
        {
            return defaultValue;
        }

        switch (Type)
        {
            case KeyValueType.String:
            case KeyValueType.WideString:
            {
                return int.TryParse((string)Value!, out int value) == false
                    ? defaultValue
                    : value;
            }

            case KeyValueType.Int32:
            {
                return (int)Value!;
            }

            case KeyValueType.Float32:
            {
                return (int)((float)Value!);
            }

            case KeyValueType.UInt64:
            {
                return (int)((ulong)Value! & 0xFFFFFFFF);
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Returns the value of this key value as a float. If the key value is invalid, then the default value is returned. If the key value is valid but the value is null, then the default value is returned. Otherwise, the value is converted to a float and returned.
    /// </summary>
    /// <param name="defaultValue">The default value to return if the key value is invalid or the value is null.</param>
    /// <returns>Returns the value of this key value as a float, or the default value if the key value is invalid or the value is null.</returns>
    public float AsFloat(float defaultValue)
    {
        if (Valid == false)
        {
            return defaultValue;
        }

        switch (Type)
        {
            case KeyValueType.String:
            case KeyValueType.WideString:
            {
                return float.TryParse((string)Value!, out float value) == false
                    ? defaultValue
                    : value;
            }

            case KeyValueType.Int32:
            {
                return (int)Value!;
            }

            case KeyValueType.Float32:
            {
                return (float)Value!;
            }

            case KeyValueType.UInt64:
            {
                return (ulong)Value! & 0xFFFFFFFF;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Returns the value of this key value as a boolean. If the key value is invalid, then the default value is returned. If the key value is valid but the value is null, then the default value is returned. Otherwise, the value is converted to a boolean and returned.
    /// </summary>
    /// <param name="defaultValue">The default value to return if the key value is invalid or the value is null.</param>
    /// <returns>Returns the value of this key value as a boolean, or the default value if the key value is invalid or the value is null.</returns>
    public bool AsBoolean(bool defaultValue)
    {
        if (Valid == false)
        {
            return defaultValue;
        }

        switch (Type)
        {
            case KeyValueType.String:
            case KeyValueType.WideString:
            {
                return int.TryParse((string)Value!, out int value) == false
                    ? defaultValue
                    : value != 0;
            }

            case KeyValueType.Int32:
            {
                return ((int)Value!) != 0;
            }

            case KeyValueType.Float32:
            {
                return ((int)((float)Value!)) != 0;
            }

            case KeyValueType.UInt64:
            {
                return ((ulong)Value!) != 0;
            }
        }

        return defaultValue;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (Valid == false)
        {
            return "<invalid>";
        }

        if (Type == KeyValueType.None)
        {
            return Name;
        }

        return $"{Name} = {Value}";
    }

    /// <summary>
    /// Loads a key value pair from the specified file in binary format. If the file does not exist, then null is returned. If an error occurs while reading the file, then null is returned. Otherwise, the key value pair is returned.
    /// </summary>
    /// <param name="path">The path to the file to load.</param>
    /// <returns>The loaded key value pair, or null if an error occurs.</returns>
    public static KeyValue? LoadAsBinary(string path)
    {
        if (File.Exists(path) == false)
        {
            return null;
        }

        try
        {
            using (var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                KeyValue kv = new();
                if (!kv.ReadAsBinary(input))
                {
                    return null;
                }

                return kv;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the key value pair from the specified stream in binary format. The stream must be positioned at the beginning of the key value pair. The method will read until it reaches the end of the key value pair or the end of the stream. If the key value pair is read successfully, then the method returns true. If an error occurs while reading, then the method returns false and the key value pair is left in an undefined state.
    /// </summary>
    /// <param name="input">Stream to read from.</param>
    /// <returns>Returns true if the key value pair is read successfully; otherwise, false.</returns>
    public bool ReadAsBinary(Stream input)
    {
        Children = new();
        try
        {
            while (true)
            {
                var type = (KeyValueType)input.ReadValueU8();

                if (type == KeyValueType.End)
                {
                    break;
                }

                KeyValue current = new()
                {
                    Type = type,
                    Name = input.ReadStringUnicode(),
                };

                switch (type)
                {
                    case KeyValueType.None:
                    {
                        current.ReadAsBinary(input);
                        break;
                    }

                    case KeyValueType.String:
                    {
                        current.Valid = true;
                        current.Value = input.ReadStringUnicode();
                        break;
                    }

                    case KeyValueType.WideString:
                    {
                        throw new FormatException("wstring is unsupported");
                    }

                    case KeyValueType.Int32:
                    {
                        current.Valid = true;
                        current.Value = input.ReadValueS32();
                        break;
                    }

                    case KeyValueType.UInt64:
                    {
                        current.Valid = true;
                        current.Value = input.ReadValueU64();
                        break;
                    }

                    case KeyValueType.Float32:
                    {
                        current.Valid = true;
                        current.Value = input.ReadValueF32();
                        break;
                    }

                    case KeyValueType.Color:
                    {
                        current.Valid = true;
                        current.Value = input.ReadValueU32();
                        break;
                    }

                    case KeyValueType.Pointer:
                    {
                        current.Valid = true;
                        current.Value = input.ReadValueU32();
                        break;
                    }

                    default:
                    {
                        throw new FormatException();
                    }
                }

                if (input.Position >= input.Length)
                {
                    throw new FormatException();
                }

                Children.Add(current);
            }

            Valid = true;
            return input.Position == input.Length;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
