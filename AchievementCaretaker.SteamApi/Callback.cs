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

namespace AchievementCaretaker.SteamApi;

/// <summary>
/// The base class for all Steam API callbacks, providing a common interface for handling callback events.
/// </summary>
public abstract class Callback : ICallback
{
    /// <summary>
    /// Callback function delegate that defines the signature for callback methods.
    /// </summary>
    /// <param name="param">The parameter for the callback function.</param>
    public delegate void CallbackFunction(IntPtr param);

    /// <summary>
    /// Occurs when the callback is run, allowing subscribers to handle the callback event.
    /// </summary>
    public event CallbackFunction? OnRun;

    /// <summary>Callback identifier for the specific callback type.</summary>
    public abstract int Id { get; }

    /// <summary>Indicates whether the callback is associated with a server-side event.</summary>
    public abstract bool IsServer { get; }

    /// <summary>Runs the callback with the specified parameter, invoking the OnRun event to notify subscribers.</summary>
    /// <param name="param">The parameter for the callback function.</param>
    public void Run(IntPtr param)
    {
        OnRun?.Invoke(param);
    }
}

/// <summary>
/// Callback class that handles specific callback events with a parameter of type TParameter, providing a strongly-typed interface for handling callback events.
/// </summary>
/// <typeparam name="TParameter">The type of the parameter for the callback function.</typeparam>
public abstract class Callback<TParameter> : ICallback
    where TParameter : struct
{
    /// <summary>
    /// Callback function delegate that defines the signature for callback methods.
    /// </summary>
    /// <param name="arg">The parameter for the callback function.</param>
    public delegate void CallbackFunction(TParameter arg);

    /// <summary>
    /// Occurs when the callback is run, allowing subscribers to handle the callback event.
    /// </summary>
    public event CallbackFunction? OnRun;

    /// <summary>Callback identifier for the specific callback type.</summary>
    public abstract int Id { get; }

    /// <summary>Indicates whether the callback is associated with a server-side event.</summary>
    public abstract bool IsServer { get; }

    /// <summary>Runs the callback with the specified parameter, invoking the OnRun event to notify subscribers.</summary>
    /// <param name="pvParam">The parameter for the callback function.</param>
    public void Run(IntPtr pvParam)
    {
        var data = (TParameter)Marshal.PtrToStructure(pvParam, typeof(TParameter))!;
        OnRun?.Invoke(data);
    }
}
