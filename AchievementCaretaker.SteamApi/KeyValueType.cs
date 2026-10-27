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

namespace AchievementCaretaker.SteamApi;

/// <summary>The type byte that starts an entry in a binary Valve KeyValues file.</summary>
public enum KeyValueType : byte
{
    /// <summary>A section: the entries that follow are its children, up to <see cref="End"/>.</summary>
    None = 0,

    /// <summary>A null-terminated UTF-8 string.</summary>
    String = 1,

    /// <summary>A 32-bit signed integer.</summary>
    Int32 = 2,

    /// <summary>A 32-bit floating-point number.</summary>
    Float32 = 3,

    /// <summary>A 32-bit pointer value.</summary>
    Pointer = 4,

    /// <summary>A null-terminated UTF-16 string.</summary>
    WideString = 5,

    /// <summary>A 32-bit color.</summary>
    Color = 6,

    /// <summary>A 64-bit unsigned integer.</summary>
    UInt64 = 7,

    /// <summary>The end of a section.</summary>
    End = 8,
}
