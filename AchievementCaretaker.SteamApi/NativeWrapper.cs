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
using System.Runtime.InteropServices;

namespace AchievementCaretaker.SteamApi;

/// <summary>
/// A C++ object of Steam's, called through its vtable: <typeparamref name="TNativeFunctions"/> is
/// the vtable's layout, one field per virtual method, in declaration order.
/// </summary>
/// <typeparam name="TNativeFunctions">The vtable layout of the interface being wrapped.</typeparam>
public abstract class NativeWrapper<TNativeFunctions> : INativeWrapper
{
    /// <summary>The address of the C++ object, passed as <c>this</c> to every call.</summary>
    protected IntPtr ObjectAddress;

    /// <summary>The object's vtable, read by <see cref="SetupFunctions"/>.</summary>
    protected TNativeFunctions Functions = default!;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Steam Interface<{typeof(TNativeFunctions)}> #{ObjectAddress.ToInt64():X8}";
    }

    /// <inheritdoc />
    public void SetupFunctions(IntPtr objectAddress)
    {
        ObjectAddress = objectAddress;

        var iface = Marshal.PtrToStructure<NativeClass>(ObjectAddress);

        Functions = Marshal.PtrToStructure<TNativeFunctions>(iface.VirtualTable)
            ?? throw new InvalidOperationException($"The vtable of {typeof(TNativeFunctions)} could not be read.");
    }

    private readonly Dictionary<IntPtr, Delegate> _functionCache = new();

    /// <summary>The managed delegate for a vtable slot, made once and then kept.</summary>
    /// <typeparam name="TDelegate">The delegate type that matches the native signature.</typeparam>
    /// <param name="pointer">The vtable slot.</param>
    protected Delegate GetDelegate<TDelegate>(IntPtr pointer)
    {
        if (_functionCache.TryGetValue(pointer, out var function) == false)
        {
            function = Marshal.GetDelegateForFunctionPointer(pointer, typeof(TDelegate));
            _functionCache[pointer] = function;
        }
        return function;
    }

    /// <summary>The vtable slot as a typed delegate, for calls that cannot go through <see cref="Call{TDelegate}"/>.</summary>
    /// <typeparam name="TDelegate">The delegate type that matches the native signature.</typeparam>
    /// <param name="pointer">The vtable slot.</param>
    protected TDelegate GetFunction<TDelegate>(IntPtr pointer)
        where TDelegate : class
    {
        return (TDelegate)((object)GetDelegate<TDelegate>(pointer));
    }

    /// <summary>Calls a native method that returns nothing.</summary>
    /// <typeparam name="TDelegate">The delegate type that matches the native signature.</typeparam>
    /// <param name="pointer">The vtable slot.</param>
    /// <param name="args">The arguments, <see cref="ObjectAddress"/> first.</param>
    protected void Call<TDelegate>(IntPtr pointer, params object?[] args)
    {
        GetDelegate<TDelegate>(pointer).DynamicInvoke(args);
    }

    /// <summary>Calls a native method and returns what it returns.</summary>
    /// <typeparam name="TReturn">The managed type of the native return value.</typeparam>
    /// <typeparam name="TDelegate">The delegate type that matches the native signature.</typeparam>
    /// <param name="pointer">The vtable slot.</param>
    /// <param name="args">The arguments, <see cref="ObjectAddress"/> first.</param>
    protected TReturn Call<TReturn, TDelegate>(IntPtr pointer, params object?[] args)
    {
        return (TReturn)GetDelegate<TDelegate>(pointer).DynamicInvoke(args)!;
    }
}
