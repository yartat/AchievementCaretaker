# Achievement Caretaker

Achievement Caretaker is a lightweight, portable application used to manage
achievements and statistics in the PC gaming platform Steam. It requires the
[Steam client](https://store.steampowered.com/about/), a Steam account and network access.
Steam must be running and you must be logged in.

> **Steam support is based on [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager)
> (SAM) by Rick ([gibbed](https://github.com/gibbed)), and GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.**
> The Steam code is an altered source version of SAM, distributed under the same zlib
> license. SAM was released closed-source in 2008, saw its last major release in 2011, and
> was opened later so that those interested could do as they like with it. See
> [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) for the full lineage and licence position.

Caretaker carries a different name because it has diverged far enough to be a different
program: the two SAM executables were merged into one, the Windows Forms UI was replaced by
[Avalonia](https://avaloniaui.net/), the library and achievement lists are cached in SQLite
so neither window starts empty, and the game list was rebuilt around achievement
completion.

[Download latest release](https://github.com/yartat/AchievementCaretaker/releases/latest).

## How it works

There is one executable, `AchievementCaretaker.exe`, and it has two faces:

| Start it… | and you get |
|---|---|
| with no arguments | **the library** — every game on your account, with how far through its achievements you are. The toolbar's view button switches between **Tiles** and **Content**. |
| with an app ID | **the editor** — the achievement and statistics editor for that one game. |

Picking a game in the library starts a second copy of the executable for it. That is not an
oversight: Steam's `ISteamUserStats` interface is scoped to the single app ID a process was
initialised with, so one process genuinely cannot serve both windows.

## Downloads

| File | Use it when |
|---|---|
| `…-win-x64-setup.exe` | 64-bit Windows, and you want it in the Start menu |
| `…-win-x64-portable.zip` | 64-bit Windows, no installation — unzip and run |
| `…-win-x86-setup.exe` / `…-win-x86-portable.zip` | 32-bit Windows |
| `…_amd64.deb` | Debian or Ubuntu: `sudo apt install ./…deb` |

Every download bundles the .NET runtime, so there is nothing to install first. The Windows
installer is per-user: it needs no administrator, and uninstalling leaves your settings and
ratings in `~/.sac` alone.

## Requirements

- The [Steam client](https://store.steampowered.com/about/), running and logged in

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
| `ratings.json` | your own like/dislike per game | no |
| `games.db` | owned games, their statistics, and each game's achievements | yes, from **Settings** |
| `icons/` | capsule art and achievement icons | yes, from **Settings** |

All of it is a cache or a local preference: delete any of it and Caretaker rebuilds it from
Steam on the next run, losing only your like/dislike ratings. **Nothing here is ever sent to
Steam.**

Your like/dislike is *not* your Steam review — Steam keeps review recommendations
server-side and does not expose them locally. If you are coming from SAM, an existing
`ratings.json` under `%LOCALAPPDATA%\SteamAchievementManager\` is moved across the first
time Caretaker runs.

## Versioning

Current version: **1.1.0**. Caretaker restarts version numbering at 1.0; it succeeds Steam
Achievement Manager 8.0, and [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md) records what
changed relative to SAM.

## What is different from Steam Achievement Manager

- **One executable instead of two.** `SAM.Picker.exe` and `SAM.Game.exe` are now a single
  `AchievementCaretaker.exe` that chooses its window from the command line, and
  `SAM.API.dll` became `AchievementCaretaker.SteamApi.dll`.
- **New Library layout.** The library window is a collections rail plus a grid that shows
  how far through each game's achievements you are; the editor pairs the achievement list
  with a detail pane, and gathers uncommitted changes into one bar so you can see what is
  about to be sent to Steam before you send it. Both follow the theme chosen in Settings.
- **All icons are vector,** so they take the theme's foreground colour and stay legible in
  dark mode. Earlier SAM releases used the Fugue Icons bitmaps.
- **Two library view modes.** *Tiles* shows capsule art with a completion meter. *Content*
  shows a row per game with a small icon, the name, release date, when you last played, the
  Steam review score, earned/total achievements, and your own like/dislike. Sort by any of
  those from the toolbar or by clicking a column header. This data is read from Steam's
  local caches, so it is only available for games Steam has already fetched data for;
  anything else shows `—`.
- **The library is cached, so the window opens straight away.** Games and their statistics
  go into a SQLite database at `~/.sac/games.db`, capsule art into `~/.sac/icons`. On launch
  the cached library is shown immediately while the list is re-checked against Steam in the
  background.
- **Achievements are cached too.** The first time you open a game, Caretaker waits for Steam
  and then stores that game's achievement list and its icons. After that the list appears as
  soon as the window does. Icons are only downloaded once.
- **Progress is shown while loading,** in both windows, instead of appearing to hang on a
  large library.
- **A theme setting.** **Settings → Appearance** offers *Follow system*, *Light* or *Dark*.
  The choice is previewed as you make it and remembered between runs; SAM always followed
  the system.
- **Migrated from .NET Framework 4.8 to .NET 10** and from Windows Forms to Avalonia. The
  interop layer was not rewritten.
- **64-bit support.** `steamclient64.dll` is loaded when running as a 64-bit process, and
  the projects build for `AnyCPU` as well as `x86`.
- **Cross-platform builds.** The interop layer no longer depends on `kernel32` or the
  Windows registry: it uses `NativeLibrary` and per-OS Steam path discovery, so the projects
  target plain `net10.0` and publish for Windows, Linux and macOS.
- Support for the current `UserGameStatsSchema` format, alongside the older one.
- The achievement list shows each description under its name, and the date it was unlocked.
- Fixed a long-standing bug in the `ISteamClient::GetISteamApps` interop signature, which
  was missing the `this` pointer. It went unnoticed for years in 32-bit builds but returns a
  null interface in 64-bit ones.

The full list, per release, is in [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); the exact
version is pinned in `global.json`. No Visual Studio installation is required.

```bash
dotnet build AchievementCaretaker.slnx -c Release
```

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
| [CLAUDE.md](CLAUDE.md) | working notes for contributors and coding agents |

## Attribution and licence

**Steam support is based on
[Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by
[gibbed](https://github.com/gibbed)**, released under the zlib license — see
[LICENSE.txt](LICENSE.txt). That part is an altered source version of the software and is
plainly marked as such in the files derived from it.

**GOG support is based on
[GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.**
It reimplements the calls that tool makes to GOG's services, for reading only, and its
files say so. Epic Games support is written from the endpoints documented by the open-source
launchers [Legendary](https://github.com/derrod/legendary) and Heroic.

None of this is the original software of those authors, and it is not endorsed by or
affiliated with them.

Steam is a trademark of Valve Corporation, GOG is a trademark of GOG sp. z o.o., and Epic
Games is a trademark of Epic Games, Inc. This project is not affiliated with any of them.

The toolbar and status icons are vector geometries drawn for this project. Earlier SAM
releases used the [Fugue Icons](https://p.yusukekamiyamane.com/) set.

See [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) for the full statement.
