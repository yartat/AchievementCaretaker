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
using System.Globalization;
using System.Threading.Tasks;
using AchievementCaretaker.SteamApi;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>What <c>AchievementCaretaker.exe --steam-worker &lt;appid&gt;</c> runs.</summary>
public static class SteamWorkerEntry
{
    private static string Describe(ClientInitializeException e)
    {
        const string Prefix = "Steam is not running. Please start Steam and try again.";

        // Family Share only locks a specific game, so the hint is only worth showing for
        // the failure that says the account is not available to this one.
        if (e.Failure == ClientInitializeFailure.ConnectToGlobalUser)
        {
            return Prefix + " If you have the game through Family Share, the game may be locked "
                + "while the Family Share account is playing a game. (" + e.Message + ")";
        }

        return string.IsNullOrEmpty(e.Message) == false ? Prefix + " (" + e.Message + ")" : Prefix;
    }

    /// <summary>
    /// Connects to Steam as the app, serves it over gRPC and returns when asked to leave.
    /// The exit code is 0 for a clean stop and 1 when Steam could not be reached.
    /// </summary>
    public static async Task<int> RunAsync(string appIdText)
    {
        if (long.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) == false || appId == 0)
        {
            await Console.Error.WriteLineAsync("The Steam helper needs an app id.");
            return 1;
        }

        SteamClientGateway gateway;
        try
        {
            gateway = new SteamClientGateway(appId);
        }
        catch (ClientInitializeException e)
        {
            await Console.Error.WriteLineAsync(Describe(e));
            return 1;
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync("Steam is not available: " + e.Message);
            return 1;
        }

        using (gateway)
        {
            await SteamWorkerHost.RunAsync(new SteamAppBackend(gateway), Console.In, Console.Out);
        }
        return 0;
    }
}
