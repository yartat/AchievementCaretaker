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
using System.Runtime.InteropServices;

namespace AchievementCaretaker.SteamApi.Types;

/// <summary>A callback as <c>Steam_BGetCallback</c> hands it over: who it is for, which kind it is, and where its data is.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CallbackMessage
{
    /// <summary>The user the callback is for.</summary>
    public int User;

    /// <summary>The callback's id, which says what <see cref="ParamPointer"/> points to.</summary>
    public int Id;

    /// <summary>The address of the callback's data, valid until the callback is freed.</summary>
    public IntPtr ParamPointer;

    /// <summary>The size of the data in bytes.</summary>
    public int ParamSize;
}
