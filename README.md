# Achievement Caretaker

Achievement Caretaker is a lightweight, portable application for your game achievements.
It edits achievements and statistics in **Steam**, and shows the library and achievements of
your **Epic Games** and **GOG** accounts, and of **Ubisoft Connect** from the launcher installed on
your computer (Windows). Epic is read-only; GOG achievements can be unlocked and locked, and
Ubisoft Connect achievements can be unlocked (not locked again), when you are signed in. Steam needs the
[Steam client](https://store.steampowered.com/about/), running and logged in, to refresh its
library and to open a game's achievements; Epic Games and GOG need only a network connection
and a sign-in. The library window opens without Steam, shows what it saved last time, and
starts refreshing as soon as Steam is available. See [Using the sources](#using-the-sources).

> **Steam support is based on [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager)
> (SAM) by Rick ([gibbed](https://github.com/gibbed)), and GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum, and
> Ubisoft Connect support on [UbiSlot](https://github.com/BalTor02/UbiSlot) by BalTor02,
> [Achievements](https://github.com/PSerban93/Achievements) by PSerban93,
> [galaxy-integration-uplay](https://github.com/melcom-creations/galaxy-integration-uplay) by melcom and
> [UplayKit](https://github.com/UplayDB/UplayKit) by UplayDB.**
> The Steam code is an altered source version of SAM, distributed under the same zlib
> license. SAM was released closed-source in 2008, saw its last major release in 2011, and
> was opened later so that those interested could do as they like with it. See
> [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) for the full lineage and licence position.

[Download latest release](https://github.com/yartat/AchievementCaretaker/releases/latest).

The Windows installers are not code-signed. Windows SmartScreen may warn when you run one: choose *More info*, then *Run anyway*. The Chocolatey and winget packages check each download against its SHA-256 hash before installing it.

## How it works

There is one executable, `AchievementCaretaker.exe`. Start it and you get **the library**: every
game on your account, with how far through its achievements you are. The toolbar's view button
switches between **Tiles** and **Content**. Picking a game opens its **achievements window**: the
achievements to tick, and for Steam the game's statistics too.

For Steam games, Caretaker starts a small hidden copy of itself for the game you opened. That is
not an oversight: Steam's `ISteamUserStats` interface is scoped to the single app ID a process was
initialised with, so one process cannot serve two games. You will see no window for it, and it
closes when you do. Older versions opened a separate editor window by giving the executable an
app ID; that still starts the library and the app ID is ignored.

## Downloads

| File | Use it when |
|---|---|
| `…-win-x64-setup.exe` | 64-bit Windows, and you want it in the Start menu |
| `…-win-x64-portable.zip` | 64-bit Windows, no installation — unzip and run |
| `…-win-x86-setup.exe` / `…-win-x86-portable.zip` | 32-bit Windows |
| `…_amd64.deb` | Debian or Ubuntu: `sudo apt install ./…deb`, or from the APT repository below |
| [Chocolatey](https://community.chocolatey.org/packages/achievement-caretaker) | Windows, from an elevated prompt: `choco install achievement-caretaker` |

Every download bundles the .NET runtime, so there is nothing to install first. The Windows
installer is per-user: it needs no administrator, and uninstalling leaves your settings and
ratings in `~/.sac` alone.

The Chocolatey package installs the **portable** build (the 64-bit or the 32-bit zip, whichever your
machine needs) and checks its SHA-256 hash before unpacking, and adds a Start menu shortcut. Upgrade it with
`choco upgrade achievement-caretaker`.

### APT repository (Debian and Ubuntu, amd64)

The package is also published in an APT repository, hosted on [Cloudsmith](https://cloudsmith.io/~yartat/repos/achievement-caretaker/),
so it is installed and updated like any other package. The repository is signed; the first command adds its
key and source, so read the script first if you prefer (`…/setup.deb.sh`).

```bash
curl -1sLf 'https://dl.cloudsmith.io/public/yartat/achievement-caretaker/setup.deb.sh' | sudo -E bash
sudo apt install achievement-caretaker
```

Update with `sudo apt update && sudo apt upgrade`. The package carries the .NET runtime; apt only adds a few system
libraries (`libice6`, `libsm6`, `libxrandr2` and the like) that a minimal install may lack.

## Requirements

- The [Steam client](https://store.steampowered.com/about/), running and logged in — for the
  Steam library and the Steam achievements; the Epic Games and GOG sources do not need it

### Linux

The UI needs a few system libraries that a minimal install may not have:

```bash
sudo apt-get install libfontconfig1 libice6 libsm6
```

## Platform support

Caretaker loads Steam's own client library into its process, so the two must share a CPU
architecture. Valve ships that library for x86 and x64 only — there is no ARM build of the
Steam client on any platform.

| Download | Status |
|---|---|
| `win-x64` | Supported and tested |
| `win-x86` | Supported and tested |
| `linux-x64` | Supported and tested (Debian 13) |
| `osx-x64` | Should work; not yet tested |
| `win-arm64`, `linux-arm64`, `osx-arm64` | Built, but **cannot talk to Steam** |

**On an Arm machine, download the x64 build.** Windows on Arm and Apple Silicon's Rosetta 2
will run it, and they are already emulating the Steam client itself. The arm64 bundles are
published only so the set is complete; they start up and tell you this rather than failing
in a confusing way.

## Where Caretaker keeps its files

Everything is stored under `~/.sac` — `C:\Users\<you>\.sac` on Windows,
`/home/<you>/.sac` on Linux, `/Users/<you>/.sac` on macOS.

If you are coming from Steam Achievement Manager, its `~/.sam` directory is **moved** to
`~/.sac` the first time Caretaker runs, so your cache and your like/dislike ratings carry
straight over. A cache location you had configured inside that directory is repointed at
the same time; one you had pointed somewhere else is left alone.

| File | What it holds | Movable |
|---|---|---|
| `settings.json` | where the database and icons are kept, and the chosen theme | no — it is read to find the others |
| `ratings.json` | your own like/dislike per game (Steam) | no |
| `epic.json`, `gog.json`, `ubisoft.json` | the sign-in to Epic Games, GOG and (if you signed in) Ubisoft Connect | no |
| `games.db` | the owned games of every account of every source, with their progress and statistics | yes, from **Settings** |
| `icons/` | capsule art and achievement icons; the other stores' art is in `icons/store/<store>/` | yes, from **Settings** |
| `ubisoft/` | Ubisoft Connect achievement icons and the achievement lists downloaded from Ubisoft's public content server | no |

**The sign-in files are credentials, not cache.** Each holds a token that stands for the whole store
account, so on Windows it is encrypted with DPAPI (readable only by your Windows user), and on Linux and
macOS it is written readable by you alone. Caretaker never sees or stores a password: you sign in on the store's own
page. Deleting one signs you out of that store (your saved library stays); keep these files private and out of
backups you share.

Everything else is a cache or a local preference: delete it and Caretaker rebuilds it on the next run, losing only
your like/dislike ratings. Libraries are saved per account, so signing out or switching accounts deletes nothing,
and signing back in finds the games and progress as they were. **Nothing here is ever sent to Steam.**

### Settings

The **Settings** button at the bottom of the library window's left rail opens a dialog with three things:

- **Theme** — Follow system, Light or Dark. It is previewed as you choose and kept only when you press **Save**;
  **Cancel** puts the old one back.
- **Game database** and **Icon cache folder** — where `games.db` and `icons/` live (**Browse…** to pick).
  Saving a new location moves the existing data there, and if the move fails the data stays where it was. Both are
  shared by every source. **Restore defaults** fills in the default locations under `~/.sac` and Follow system (nothing is moved until you press **Save**).

Your choices are written to `settings.json` only once you change something; without the file, the defaults
above apply.

Your like/dislike is *not* your Steam review — Steam keeps review recommendations
server-side and does not expose them locally. If you are coming from SAM, an existing
`ratings.json` under `%LOCALAPPDATA%\SteamAchievementManager\` is moved across the first
time Caretaker runs.

## Versioning

Current version: **2.2.0**. [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md) records what
changed in each release.

## Using the sources

The library shows one source at a time. Pick it in the **Source** section of the left rail:
Steam, Epic Games, GOG or Ubisoft Connect. Each has its own games and its own collections;
Epic and GOG have their own sign-in, and Ubisoft Connect is read from the launcher or, if you sign in, from Ubisoft (see below).
Search, the Tiles / Content switch and sorting work the same way in all of them.

### Steam

Steam is the only source that can edit statistics, and, with GOG, change achievements:
the Steam achievements window unlocks and re-locks them and edits the game's statistics. Everything else on this tab is read from Steam
and from the caches Steam keeps on disk.

**Starting without Steam is fine.** If Steam is not installed, not running, or running with no
account logged in, the library still opens. It shows the games saved last time, with their
capsule art, under a **No active Steam** banner that says why. In the background Caretaker
tries to reach Steam every few seconds; the moment it can, the banner goes and the library
refreshes by itself. While Steam is missing, Refresh is off, and typing an app ID or opening
opening a game tells you Steam is needed instead of failing.

**Steam going away is noticed.** If you quit Steam while Caretaker is open, the banner comes
back, the games stay on screen, and Caretaker starts looking for Steam again. An achievements
window that is already open will fail to send changes while Steam is gone; reopen the game once
Steam is back.

**A refresh that was cut short never deletes games.** Checking ownership asks Steam about
every published app ID, and once Steam stops answering, every remaining game looks "not owned".
So a refresh that finishes adds new games and updates known ones, but removes games Steam no
longer lists only when the whole check, including the statistics pass, ran to the end under
the same Steam. If Steam closed half-way, you keep every game you had and the missing ones are
checked again next time. A game whose name could not be read keeps its old name, and a failed
download of the published game list changes nothing.

Picking a game opens its achievements window. The list is read from Steam when the window opens
(about a second), and again when you press Reload.

### Epic Games and GOG

These two work the same way for browsing: you can see which games you own and which
achievements are unlocked. Neither needs the Steam client. **Epic is read-only. GOG
achievements can be changed**, see below.

1. **Sign in.** Select the source and press **Sign in**, here or under the source's name in
   the left panel. The store's own sign-in page opens in a window of Caretaker and closes by
   itself when you are signed in; nothing needs copying. Caretaker never sees your password,
   and that window keeps nothing afterwards. If the window cannot start (on Windows it needs
   the WebView2 runtime, on Linux WebKitGTK or WPE), Caretaker offers your own browser: open
   the page there and paste back the code (Epic shows a page with an `authorizationCode`) or the
   address the browser ends on (GOG). The session is kept in `~/.sac/epic.json` or
   `~/.sac/gog.json` (a token for the whole store account; treat it like a password).
2. **The library loads and is cached.** Your games are listed with their capsule art. They are
   saved in `games.db`, so the next start shows them at once, and then refreshes in the
   background. If a refresh fails, the cached list stays and the status bar says why. Epic's
   list leaves out Fab and Unreal Engine marketplace assets, which are not games.
3. **Achievement counts.** A store's game list carries no counts, and reading them costs one
   request or more per game. Right after you sign in, Caretaker reads them for every game whose
   count it does not have yet, a few at a time; for a large library that takes a few minutes,
   shows in the status bar and can be cancelled. Signing back in to an account it already knows
   reads only what is new. It never does this on its own at other times. A game's meter stays
   absent until its count is known, because "unknown" is not "none earned". You can also fill
   it in yourself:
   - **Open a game** (double-click, or Enter) to see its achievements: name, description,
     unlock date, and XP and rarity where the store has them. Opening it also records the
     game's count in the library.
   - **Load progress** does what the sign-in did, again: it reads the counts that are still
     missing, for example after a cancel or for games that failed. The counts already read are
     kept, and a restart does not lose them. If your session has expired the run stops and
     asks you to sign in again.
4. **Browse.** Stores offer the collections All games, Has achievements and Perfect games, and
   sorting by name or completion. They have no playtime, last played, review score, likes or
   demo/mod filters, so those do not appear.
5. **Account.** Under each store's name in the left panel is the signed-in account, with a
   **⋯** menu. **Sign out…** asks first, and forgets the session only: the account's games and
   achievement counts stay saved on this computer and are there again when you sign in. **Switch
   account…** asks first, opens the sign-in window, and shows the new account's library once you
   have signed in; closing the window leaves the current account as it was. Each account, in
   each store and in Steam, keeps its own saved library, and one is never shown under another.

What each store gives you differs. GOG has no XP and no progress values, so those columns are
absent for GOG games. Epic achievements cannot be re-locked by anyone, so Epic stays read-only.

**Changing GOG achievements.** In a GOG game's window, tick or untick achievements, or use the
Lock, Invert and Unlock buttons for everything shown. The changes collect in an **Uncommitted**
bar; **Discard** drops them and **Commit** sends them to GOG, then reads the list again from
GOG. To do this Caretaker takes the game's own client credentials from GOGDB, a community
archive, only at the moment you commit. A game GOGDB has no build for cannot be changed. Like
Steam's editor, this changes your real account. Unlocking and re-locking were checked against
a live GOG account.

If a session is refused later, Caretaker forgets it and shows the sign-in panel again.

### Ubisoft Connect

Windows only. There are two ways to read an account, and the second is optional:

- **From the launcher on this computer.** Caretaker reads the files the Ubisoft Connect launcher keeps,
  so there is nothing to sign in to: whoever is logged in to the launcher here is the account, shown in
  the left panel as *this computer*. The launcher must be installed and have been logged in to at least
  once; if it is not found the window says so, and you install it, log in, and press **Look again**.
- **With your Ubisoft account.** Sign in with Ubisoft's own page, in a browser window inside
  Caretaker (**Sign in** in the left panel; it needs the WebView2 runtime). Caretaker never sees your
  password. It then reads your games and what you earned from Ubisoft, so the launcher is not needed,
  and achievements earned on other computers show. Signing out returns to the launcher's files.

- **Which games.** Owned products that are games, with the names and the wide background picture the
  launcher's catalog (or, signed in, Ubisoft) gives them.
- **Achievements.** Names, descriptions and icons come from the game's list. If the launcher has not
  saved it, Caretaker downloads it from Ubisoft's public content server (the same file, no sign-in)
  and keeps it. A game that has no list says so.
- **What you earned.** From the launcher's own record, with dates, for games played on this computer; an
  achievement not recorded there is shown as not earned, which cannot be told from earned on another
  computer. Signed in, it is what Ubisoft has recorded for your account.
- Counts appear in the library for games whose list is already on this computer; **Load progress**
  reads the rest. There is no XP or rarity.

**Unlocking.** Signed in with your Ubisoft account, a game's achievements window has checkboxes and a
**Commit** button, and ticking an achievement and committing records it as earned on your account (the
launcher shows it too). **It cannot be undone**: Ubisoft's service only takes what is earned, so there is
no Lock or Invert, achievements already earned are shown as fixed, and Caretaker asks before it sends.
It changes your real account, and UbiSlot, which Caretaker's Ubisoft support is based on, warns that this
may carry risks for it; the first one was tried on a live account and the launcher showed it at once.
Without a sign-in nothing can be changed. See [docs/UBISOFT-RESEARCH.md](docs/UBISOFT-RESEARCH.md).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); the exact
version is pinned in `global.json`. No Visual Studio installation is required.

```bash
dotnet build AchievementCaretaker.slnx -c Release
```

The build is expected to finish with 0 warnings and 0 errors: nullable reference types and
XML documentation are enabled, and CI treats warnings as errors.

The executable is written to `bin\` alongside the Avalonia assemblies and native libraries —
the whole directory is needed to run, not just the executable. Full instructions, including
32-bit, cross-platform and self-contained builds, are in [docs/BUILD.md](docs/BUILD.md).

## Documentation

| Document | Covers |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | how the application is put together |
| [docs/BUILD.md](docs/BUILD.md) | building, publishing and packaging a release |
| [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md) | what changed, per version |
| [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) | the Steam Achievement Manager lineage and licensing |
| [PRIVACY.md](PRIVACY.md) | what is stored on your computer, and which services the application contacts |
| [CLAUDE.md](CLAUDE.md) | working notes for contributors and coding agents |

## Attribution and licence

**Steam support is based on
[Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by
[gibbed](https://github.com/gibbed)**, released under the zlib license — see
[LICENSE.txt](LICENSE.txt). That part is an altered source version of the software and is
plainly marked as such in the files derived from it.

**GOG support is based on
[GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.**
It reimplements the calls that tool makes to GOG's services, for listing, unlocking and
re-locking, and its files say so. Epic Games support is written from the endpoints documented by the open-source
launchers [Legendary](https://github.com/derrod/legendary) and Heroic.

**Ubisoft Connect support is based on four open-source tools, all under the MIT licence:**

| Project | Author | Licence | Used for |
|---|---|---|---|
| [UbiSlot](https://github.com/BalTor02/UbiSlot) | BalTor02 | MIT, © 2026 Balkrishna Toraskar | where the launcher keeps its ownership cache, achievement archives and spool; the shipped list of game names (a copy of UbiSlot's, whose ids it credits to NikitaGareev) |
| [Achievements](https://github.com/PSerban93/Achievements) | PSerban93 | MIT, © 2025 JokerVerse | how the spool and the archives are read |
| [galaxy-integration-uplay](https://github.com/melcom-creations/galaxy-integration-uplay) | melcom | MIT, © 2019 FriendsOfGalaxy and © 2026 melcom | the launcher's configuration catalog, Ubisoft's content server, and the sign-in session |
| [UplayKit](https://github.com/UplayDB/UplayKit) and [Uplay-Protobufs](https://github.com/UplayDB/Uplay-Protobufs) | UplayDB | MIT, © 2022-2025 UplayDB | the socket protocol and the field numbers of its messages |

The code is written for this project, not copied. MIT asks that the authors' copyright and permission
notices travel with substantial portions of their work; they are reproduced in full in
[docs/ATTRIBUTION.md](docs/ATTRIBUTION.md#the-ubisoft-connect-provider-is-based-on-ubislot-achievements-galaxy-integration-uplay-and-uplaykit).

None of this is the original software of those authors, and it is not endorsed by or
affiliated with them.

Steam is a trademark of Valve Corporation, GOG is a trademark of GOG sp. z o.o., Epic
Games is a trademark of Epic Games, Inc., and Ubisoft and Ubisoft Connect are trademarks of Ubisoft
Entertainment. This project is not affiliated with any of them.

The toolbar and status icons are vector geometries drawn for this project.

See [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) for the full statement.
