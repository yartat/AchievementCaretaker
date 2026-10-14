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
using AchievementCaretaker.SteamApi.Interfaces;

namespace AchievementCaretaker.SteamApi.Wrappers
{
    public class SteamApps008 : NativeWrapper<ISteamApps008>
    {
        #region IsSubscribed
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool NativeIsSubscribedApp(IntPtr self, uint gameId);

        public bool IsSubscribedApp(uint gameId)
        {
            return this.Call<bool, NativeIsSubscribedApp>(this.Functions.IsSubscribedApp, this.ObjectAddress, gameId);
        }
        #endregion

        #region GetCurrentGameLanguage
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr NativeGetCurrentGameLanguage(IntPtr self);

        public string GetCurrentGameLanguage()
        {
            var languagePointer = this.Call<IntPtr, NativeGetCurrentGameLanguage>(
                this.Functions.GetCurrentGameLanguage,
                this.ObjectAddress);
            return NativeStrings.PointerToString(languagePointer);
        }
        #endregion
    }
}
