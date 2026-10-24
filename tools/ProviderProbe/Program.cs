/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Epic;
using AchievementCaretaker.Providers.Gog;
using Microsoft.Extensions.DependencyInjection;

// Read-only probe of the store providers. Nothing here writes to an account;
// the only files it writes are the sessions, in ~/.sac/<provider>.json.

// Titles are not ASCII, and the console's default code page turns them into '?'.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sac");
// The same registrations as the application, without the application.
using var services = new ServiceCollection()
    .AddEpicProvider(home)
    .AddGogProvider(home)
    .BuildServiceProvider();
var providers = services.GetServices<IStoreProvider>().ToArray();
var accounts = services.GetServices<IStoreAccount>().ToArray();

var provider = args.Length > 0 ? providers.FirstOrDefault(p => p.Id == args[0]) : null;
var account = provider == null ? null : accounts.Single(a => a.Id == provider.Id);
var command = args.Length > 1 ? args[1] : "help";

try
{
    if (provider == null)
    {
        Help(providers);
        return args.Length == 0 ? 0 : 1;
    }

    switch (command)
    {
        case "login":
            await LoginAsync(account);
            break;

        case "logout":
            account.SignOut();
            Console.WriteLine($"Signed out of {account.DisplayName}; session file removed.");
            break;

        case "whoami":
            await account.EnsureSignedInAsync();
            Console.WriteLine($"Signed in to {account.DisplayName} as {account.AccountName ?? "(name unknown)"}.");
            break;

        case "installed" when provider.Id == "epic":
            foreach (var game in EpicManifests.GetInstalled().OrderBy(g => g.Title))
            {
                Console.WriteLine($"{game.Namespace,-34} {game.Title}");
                Console.WriteLine($"{"",-34} {game.InstallLocation}");
            }
            break;

        case "library":
            var library = await provider.GetLibraryAsync();
            foreach (var game in library)
            {
                Console.WriteLine($"{game.Key,-34} {game.Title}");
            }
            Console.WriteLine($"{library.Count} games.");
            break;

        case "achievements" when args.Length > 2:
            var locale = args.Length > 3 ? args[3] : "en";
            var achievements = await provider.GetAchievementsAsync(args[2], locale);
            if (achievements.Count == 0)
            {
                Console.WriteLine($"No {provider.DisplayName} achievements for that game.");
                break;
            }

            foreach (var a in achievements.OrderByDescending(a => a.Unlocked).ThenBy(a => a.DisplayName))
            {
                // A field the store does not have is blank, not zero.
                var date = a.UnlockDate?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "";
                var xp = a.XP == null ? "" : a.XP.Value.ToString(CultureInfo.InvariantCulture) + " XP";
                var rarity = a.RarityPercent == null ? "" : a.RarityPercent.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                Console.WriteLine(
                    $"{(a.Unlocked == true ? "[x]" : "[ ]")} {a.DisplayName,-40} {date,-12} {xp,7} {rarity,7}" +
                    (a.Hidden == true ? "  hidden" : ""));
                if (string.IsNullOrWhiteSpace(a.Description) == false)
                {
                    Console.WriteLine($"    {a.Description}");
                }
            }
            Console.WriteLine($"{achievements.Count(a => a.Unlocked)} of {achievements.Count} unlocked.");
            break;

        default:
            Help(providers);
            break;
    }
    return 0;
}
catch (ProviderException e)
{
    Console.Error.WriteLine($"{provider?.DisplayName ?? "Provider"}: {e.Message} ({e.ErrorCode})");
    return 1;
}

static void Help(System.Collections.Generic.IReadOnlyList<IStoreProvider> providers)
{
    Console.WriteLine($"""
        ProviderProbe - read-only game-store libraries and achievements

          <provider> login                        sign in through the browser
          <provider> logout                       forget the saved session
          <provider> whoami                       check the session still works
          <provider> library                      owned games and their keys
          <provider> achievements <key> [locale]  a game's achievements, e.g. epic achievements rosemallow
          epic installed                          games installed by the Epic launcher (no sign-in)

        providers: {string.Join(", ", providers.Select(p => p.Id))}
        """);
}

static async Task LoginAsync(IStoreAccount account)
{
    Console.WriteLine($"Opening the {account.DisplayName} sign-in page. After signing in, paste {account.SignInHint}:");
    Console.WriteLine();
    Console.WriteLine(account.SignInUrl);
    Console.WriteLine();
    try
    {
        Process.Start(new ProcessStartInfo(account.SignInUrl) { UseShellExecute = true });
    }
    catch (Exception)
    {
        // No browser to hand; the URL is printed above.
    }

    Console.Write("> ");
    await account.SignInAsync(Console.ReadLine());
    Console.WriteLine($"Signed in to {account.DisplayName} as {account.AccountName ?? "(name unknown)"}.");
}
