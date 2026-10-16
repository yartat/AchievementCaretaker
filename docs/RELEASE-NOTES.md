# Release notes

> **Steam support is based on
> [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) (SAM) by
> Rick (gibbed)**, and is an altered source version of it, distributed under the same zlib
> license; GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.
> Every release carries that attribution. See [ATTRIBUTION.md](ATTRIBUTION.md).

Versions follow `MAJOR.MINOR.PATCH`. Caretaker restarts numbering at 1.0; it succeeds Steam
Achievement Manager 8.0.

---

## 2.1.0 — 2026-10-16

A small security release. No user-facing feature changes.

### Highlights

- **GOG and Epic session files are now encrypted at rest on Windows.** `~/.sac/epic.json`
  and `~/.sac/gog.json` hold a refresh token — a credential for the whole store account —
  and are now protected with DPAPI (`CurrentUser` scope) instead of being saved as plain
  JSON. On Linux and macOS, where there is no BCL equivalent of DPAPI, the file is still
  written user-only, as before.

### Downloads

| If you are on | Take |
|---|---|
| 64-bit Windows | `achievement-caretaker-2.1.0-win-x64-setup.exe` to install, or `…-win-x64-portable.zip` to unzip and run |
| 32-bit Windows | `…-win-x86-setup.exe` or `…-win-x86-portable.zip` |
| Debian or Ubuntu (amd64) | `achievement-caretaker_2.1.0_amd64.deb` — `sudo apt install ./achievement-caretaker_2.1.0_amd64.deb` |
| Windows on Arm | the x64 download — Valve ships no ARM build of the Steam client, and Windows emulates both |

Every download bundles the .NET runtime.

---

## 2.0.0 — 2026-10-15

A major release. Caretaker is no longer only a Steam tool: it shows your **Epic Games** and
**GOG** libraries beside Steam's, can **unlock and re-lock GOG achievements**, opens without
Steam running, and keeps a separate saved library for every account. The Steam interop layer is
unchanged; what is new is around it.

### Highlights

- **Epic Games and GOG in the library window.** Pick a source in the left panel: your games, and
  how far through each one's achievements you are. Epic is read-only; GOG achievements can be
  unlocked and locked from a window that works like the Steam editor.
- **Sign in without copying anything.** The store's own sign-in page opens in a window of
  Caretaker and closes by itself when you are in. Caretaker never reads your password.
- **The library opens without Steam.** It shows the games saved last time under a "No active
  Steam" banner and refreshes by itself as soon as Steam is available. If Steam quits, the
  window notices and carries on.
- **One saved library per account**, in Steam, GOG and Epic. Signing out or switching account
  deletes nothing.
- **Cached art loads in a fraction of the time.** Capsules and achievement icons are read,
  decoded and downloaded in parallel.

### Downloads

| If you are on | Take |
|---|---|
| 64-bit Windows | `achievement-caretaker-2.0.0-win-x64-setup.exe` to install, or `…-win-x64-portable.zip` to unzip and run |
| 32-bit Windows | `…-win-x86-setup.exe` or `…-win-x86-portable.zip` |
| Debian or Ubuntu (amd64) | `achievement-caretaker_2.0.0_amd64.deb` — `sudo apt install ./achievement-caretaker_2.0.0_amd64.deb` |
| Windows on Arm | the x64 download — Valve ships no ARM build of the Steam client, and Windows emulates both |

Every download bundles the .NET runtime. The portable archives unzip to a folder that has to
stay together. The installer is per user and installs into
`%LOCALAPPDATA%\Programs\AchievementCaretaker`.

### Upgrading from 1.1.0

- **The application was renamed** from SteamAchievementCaretaker to AchievementCaretaker. The
  2.0.0 installer therefore installs into its own folder with its own *Installed apps* entry, and
  **does not replace 1.1.0**. Uninstall 1.1.0 from *Installed apps* (or delete its unzipped
  folder) once 2.0.0 is running. Nothing in `~/.sac` is touched by either.
- **Your settings and your like/dislike ratings carry over.** The saved game library is rebuilt
  once: the library tables now record whose games they hold, and a database from before that
  cannot say. It was only ever a cache, so the next refresh fills it again, and the capsule art
  already on disk is reused.
- Whatever is in `~/.sam` from Steam Achievement Manager is still moved to `~/.sac` the first
  time, as before.

### New

- **Epic Games and GOG libraries**, one source at a time, with the same Tiles and Content views.
  A store has no playtime, last played or Steam rating, so it offers the collections All games,
  Has achievements and Perfect games, and sorting by name or completion.
- **Store achievements window**, in the same process, laid out like the Steam editor: filter chips,
  search, a row per achievement with its description and unlock date, and a detail pane. XP and
  rarity appear where the store has them (GOG has no XP).
- **Unlock and re-lock on GOG.** Tick achievements, or use the Lock, Invert and Unlock buttons
  for everything shown; changes collect in an **Uncommitted** bar and **Commit** sends them to
  GOG, then reads the list again. To do this Caretaker takes the game's own client credentials
  from GOGDB, a community archive, only at the moment you commit. Epic achievements cannot be
  re-locked, so Epic has none of this.
- **Achievement counts.** Right after you sign in, Caretaker reads the counts for the games it
  does not have yet, a few at a time (it shows in the status bar and can be cancelled); **Load
  progress** does it again on request. A game's meter stays absent until its count is known.
- **Sign-in inside the application**, with your own browser and a pasted code as the fallback if
  the built-in browser cannot start. It needs the WebView2 runtime on Windows and WebKitGTK or
  WPE on Linux.
- **The account is always in the left panel**, under its store: its name, and a menu to
  **Switch account** or **Sign out**. Both ask first. Switching signs in before it lets go of
  anything, so closing the page leaves the current account as it was.
- **Start without Steam.** The library opens at once, shows the cached games and capsules, and
  tells you why Steam is not available. The App ID box and opening the editor say Steam is
  needed instead of failing.
- **Steam is watched while Caretaker is open.** If it quits, the games stay on screen and the
  search for Steam resumes; an editor window for that Steam locks itself.
- **A saved library per account**, for Steam (by Steam account) and for each store (by account).
  Coming back to an account finds its games and progress where they were.

### Changed

- **Sign out no longer deletes the account's saved library or counts.** It forgets the session
  only.
- **Icons load in parallel**, off the UI thread, in the library, the store windows and the
  editor. On a library of 626 games, 624 cached capsules decoded in 27 ms instead of 212 ms
  (measured with a warm disk cache).
- **A new sign-in starts reading achievement counts by itself.** Nothing else does.

### Fixed

- **A refresh cut short by Steam quitting could wipe the library.** Once Steam is gone, every
  remaining ownership check answers "not owned", and one such run left 48 games of about 600.
  A refresh now only adds and updates what it found, and removes games Steam no longer lists
  only when the whole check ran to the end under the same Steam.
- **A failed download of the published game list replaced the library with one game** (Spacewar).
  It now changes nothing.
- A game name Steam failed to answer was replaced by an empty one; a known name is kept.

### Known issues

- **Not exercised:** Linux and macOS (the new store code and the embedded sign-in), the fallback
  when the WebView2 runtime is missing, switching between two real accounts, and whether a
  running Caretaker reconnects to a Steam that was started after it (and the Linux and macOS
  lookup of the Steam process). The Windows installers and the other artifacts of this version
  are built by the release pipeline and were not run before this was written.
- A Steam that is running but has not finished loading your licences answers incompletely and
  can pass for a complete check, so a refresh could remove games that return on the next one.
- With the WPE backend on Linux the sign-in window cannot be made private, so an earlier login
  may persist in it.
- Steam's write path (`SetAchievement`, `SetStatValue`, `StoreStats`, `ResetAllStats`) is still
  not re-verified against a live account. The rest is as listed under 1.0.0 below.

---

## 1.1.0 — 2026-09-23

A packaging and polish release. Windows now gets an installer and a portable archive, both
self-contained, and the library and editor windows lose a handful of layout faults. **The
interop layer is unchanged** — nothing new is sent to Steam, and nothing Caretaker does to
your account is different from 1.0.0.

### Highlights

- **A Windows installer**, for x64 and x86, alongside a portable zip for each.
- **Every download is self-contained.** Nothing to install first — no .NET runtime.
- **The achievement list shows each description** on a second line under the name, and
  the **unlock date** in a column of its own.
- The Content view's columns line up with their headers again, and the library window can
  no longer be shrunk until the game names disappear.

### Downloads

| If you are on | Take |
|---|---|
| 64-bit Windows | `steam-achievement-caretaker-1.1.0-win-x64-setup.exe` to install, or `…-win-x64-portable.zip` to unzip and run |
| 32-bit Windows | `…-win-x86-setup.exe` or `…-win-x86-portable.zip` |
| Debian or Ubuntu (amd64) | `steam-achievement-caretaker_1.1.0_amd64.deb` — `sudo apt install ./steam-achievement-caretaker_1.1.0_amd64.deb` |
| Windows on Arm | the x64 download — Valve ships no ARM build of the Steam client, and Windows emulates both |

Every download bundles the .NET runtime. The portable archives unzip to a folder that has to
stay together — the executable needs the Avalonia assemblies and the native Skia libraries
beside it.

The installer is **per user**: it needs no administrator, installs into
`%LOCALAPPDATA%\Programs\SteamAchievementCaretaker`, and adds a Start menu shortcut and an
entry in *Installed apps*. x64 and x86 share that one location, so installing one replaces
the other. Uninstalling removes only what was installed — your settings, cache and
like/dislike ratings in `~/.sac` stay put.

### Upgrading from 1.0.0

Nothing to do. Settings, caches and ratings in `~/.sac` are read as they are. If you
unzipped 1.0.0 somewhere, you can delete that folder once 1.1.0 is running; the installer
does not look for it.

### New

- **A Windows installer**, `…-setup.exe`, for x64 and x86. Built with NSIS and
  cross-compiled on Linux, like every other artifact, so the release still needs no Windows
  machine.
- **The Windows builds are self-contained**, and the archives are named `…-portable.zip`
  to say what they are. 1.0.0's zips were framework-dependent and would not start without
  the .NET 10 Desktop Runtime; the cost of dropping that requirement is about 34 MB per
  download instead of 12 MB.
- **Achievement descriptions in the list.** Each row carries the description on one
  trimmed line under the name, so it can be read without selecting the achievement. Hidden
  achievements, which have no description until unlocked, keep a single line.
- **Unlock date column.** Unlocked achievements show the day they were earned, in the same
  `d MMM yyyy` form as the library. A locked achievement shows nothing; one Steam reports
  unlocked but without a timestamp shows `—`, and ticking a box you have not committed yet
  does not invent a date for it.

### Fixed

- **Content view: the Steam column drifted right** and ran into the achievement meter,
  pushing every column after it out of line with its header. The header and the rows were
  inset by different amounts; they now agree, and the Steam value is aligned with its
  heading.
- **Completion meters were wider than their column.** Avalonia's progress bar has a built-in
  minimum width of 200, which beat the column width in the Content view and overhung the
  capsule in the Tiles view. Both meters now fit where they are placed.
- **The library window could be shrunk until the Name column vanished** and the dislike
  button was clipped. Its minimum width is now 1000, derived from the fixed columns rather
  than guessed.
- **Tiles view: the scrollbar sat away from the window's right edge**, and scrolling with
  the wheel over the empty space beside the tiles did nothing. The scrollbar is now at the
  edge, where you expect to find it, and the wheel works anywhere over the grid.

### Verified in this release

| Check | Result |
|---|---|
| `dotnet build` on `AnyCPU` and `x86`, warnings as errors | 0 warnings, 0 errors |
| Both windows constructed and rendered headless after every XAML change | pass |
| Library window, Tiles and Content, against live Steam (win-x64) | columns aligned with headers; scrollbar at the right edge; Name column kept at minimum width |
| Achievement editor against live Steam | description line and unlock dates shown |
| GitHub workflow shell logic run end to end in a container | all five artifacts, correct architectures |
| Both Windows installers: silent install, run, silent uninstall | installs, opens the library against live Steam, uninstall leaves no files, registry keys or shortcut, and `~/.sac` untouched |
| Both portable archives unzipped and run | open against live Steam |
| `packaging/check-version.sh 1.1.0` | tag, both projects, both window titles and these notes agree |

### Known issues

Unchanged from 1.0.0; the list is under that release below. The write path in particular is
still not re-verified against a live account.

---

## 1.0.0 — 2026-09-21

The first Caretaker release. It is SAM 8.0 restructured into a single application and
renamed, plus a theme setting and a new icon. **Nothing changed in what the tool does to
your Steam account** — the interop layer was carried over untouched.

### Highlights

- One executable, `SteamAchievementCaretaker.exe`, instead of `SAM.Picker.exe` and
  `SAM.Game.exe`.
- A theme setting: **Settings → Appearance** offers *Follow system*, *Light* or *Dark*.
- A new application icon — Steam's gear carrying an achievement star — with entries up to
  256px, so it no longer blurs in Explorer or Alt-Tab.
- Two projects and a `.slnx` solution in place of three projects and a `.sln`.
- Settings and caches now live in `~/.sac`. An existing Steam Achievement Manager `~/.sam`
  directory is moved across on first run, so nothing is lost.

### Downloads

Each download is a folder, not a single file: the executable needs the Avalonia assemblies
and the native Skia libraries beside it.

| If you are on | Take |
|---|---|
| 64-bit Windows | `win-x64` |
| 32-bit Windows | `win-x86` |
| Debian or Ubuntu (amd64) | the `.deb` — `sudo apt install ./steam-achievement-caretaker_1.0.0_amd64.deb` |
| Any other Linux | `linux-x64` |
| macOS (Intel) | `osx-x64` — builds, but has never been run |
| Windows on Arm, Apple Silicon | `win-x64` / `osx-x64`, **not** the arm64 bundles |

The arm64 bundles exist only so the build matrix is complete. Valve ships no ARM build of
the Steam client on any platform, and Caretaker loads that client into its own process, so
an arm64 build starts and then tells you it cannot talk to Steam. Emulation runs the x64
bundle fine — it is already emulating Steam itself.

A framework-dependent bundle needs the
[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). On Linux it
also needs three system libraries that a minimal install may not have:

```bash
sudo apt-get install libfontconfig1 libice6 libsm6
```

### Upgrading from Steam Achievement Manager

Caretaker keeps its settings and caches in `~/.sac`. **The first time it runs, an existing
`~/.sam` directory is moved there** — `settings.json`, `ratings.json`, `games.db` and
`icons/` — so your cached library, your cache locations and your own like/dislike ratings
carry over with nothing to do by hand. A cache location you had configured *inside* that
directory is repointed at the same time; one you had pointed somewhere else is left alone.

Coming from a build older than `~/.sam`, a `ratings.json` under
`%LOCALAPPDATA%\SteamAchievementManager\` is moved across as well.

Two things to know:

- **It is a move, not a copy.** If you keep Steam Achievement Manager installed, it will
  find `~/.sam` gone and start again with an empty cache. Nothing is destroyed — SAM will
  rebuild its own — but your like/dislike ratings now belong to Caretaker.
- If `~/.sac` already exists, nothing is moved and `~/.sam` is left exactly as it is. The
  two never merge.

Caretaker does not uninstall or modify a SAM install beyond that one move.

### Renamed and restructured

- **The project is now Steam Achievement Caretaker**, based on Steam Achievement Manager.
  The attribution is carried in every source file header, in the assembly `Copyright` and
  `Description` properties, and in all documentation.
- **One executable instead of two.** `SAM.Picker.exe` and `SAM.Game.exe` were merged into
  `SteamAchievementCaretaker.exe`, which shows the library window when started with no
  arguments and the achievement editor when started with an app ID.

  Picking a game still starts a **second process**, now a second copy of the same
  executable. That part was never a packaging choice: Steam's `ISteamUserStats` interface
  is scoped to the one app ID a process was initialised with, so one process cannot serve
  both windows.
- **Two projects instead of three.** `SAM.API` became
  `SteamAchievementCaretaker.SteamApi`; `SAM.Picker`, `SAM.Game` and the linked `Shared/`
  folder became `SteamAchievementCaretaker.App`, organised into `Common/`, `Library/`,
  `Achievements/`, `ViewModels/`, `Views/` and `Resources/`.

  The `Shared/*.cs` link machinery is gone — one assembly needs no links, and the shared
  icon set no longer has to be given a matching `avares` path in two places.
- **Namespaces** are now `SteamAchievementCaretaker.SteamApi*` and
  `SteamAchievementCaretaker.App*`.
- **The settings and cache directory is now `~/.sac`**, not `~/.sam`. An existing `~/.sam`
  is moved there on first run and any cache location that pointed inside it is repointed,
  so nothing has to be done by hand. See
  [Upgrading from Steam Achievement Manager](#upgrading-from-steam-achievement-manager).
- **The solution is `SteamAchievementCaretaker.slnx`**, in the XML `.slnx` format. The old
  `.sln` was removed. The solution carries a `Documentation` folder with the README, the
  licence and everything under `docs/`.
- **Duplicated files were merged**, not copied: one `MessageWindow` (the editor's version,
  which supports confirmation dialogs, plus the library's taskbar and null-owner handling),
  one `InvariantShorthand`, one `App.axaml`, one `GlobalSuppressions.cs`, one application
  icon and one manifest.
- **Documentation was split out** into `docs/ARCHITECTURE.md`, `docs/BUILD.md`,
  `docs/RELEASE-NOTES.md` and `docs/ATTRIBUTION.md`.
- Two unreferenced PNG assets left over from the Windows Forms era
  (`poop-smiley-sad.png`, `poop-smiley-sad-enlarged.png`) were dropped, along with the
  unused `Blank.ico` and `Pink.ico`.

### New

- **A Debian package.** `steam-achievement-caretaker_1.0.0_amd64.deb` installs to
  `/usr/lib/steam-achievement-caretaker` with a menu entry and an icon, and is
  **self-contained** — it bundles the .NET runtime, so it needs nothing from Microsoft's
  apt repository. The Windows zips stay framework-dependent and want the .NET 10 Desktop
  Runtime.
- **New application icon** — Steam's gear carrying an achievement star, in the gold the
  dark theme already uses. It replaces the inherited SAM icon, which had only 16, 32 and
  48px entries; the new one goes up to 256, so it stays sharp in Explorer's large-icon
  views and the Alt-Tab switcher. The 16px entry is drawn separately rather than
  downscaled, because the gear teeth do not survive at that size. The vector master is
  `docs/assets/app-icon.svg`.
- **Theme setting.** **Settings → Appearance** chooses *Follow system*, *Light* or *Dark*,
  stored in `~/.sac/settings.json` as `Theme`. The dialog applies the choice immediately so
  you can see it, and puts the previous one back if you cancel. *Follow system* is the
  default and is what the application did before.

  Because the library window and the achievement editor are separate processes, each reads
  the setting at startup: a change made in Settings reaches an editor window that is already
  open only when that window is reopened.

### Fixed

- **Publishing the 32-bit build produced a mixed-architecture bundle.** `dotnet publish
  -r win-x86` resolves 32-bit native libraries but still emits a *64-bit* apphost, so the
  result died inside SkiaSharp's type initializer with nothing printed — it looked like the
  application simply failed to start. Building 32-bit needs `-p:Platform=x86` as well, which
  is what the `bin/x86` build always did; only the publish path was wrong. CI now asserts
  the architecture of every Windows bundle before packaging it.
- Changing a cache location used to abandon the whole settings save when the move failed.
  The theme, which does not depend on the move, is now saved either way, and the error says
  the cache was left where it was.

### Unchanged on purpose

- **The interop layer was not rewritten.** The vtable marshalling, the interface layouts
  and the callback pump are SAM's, carried over with renames only.
- **Nothing is sent to Steam that was not sent before.** Your like/dislike stays local.

### Verified in this release

| Check | Result |
|---|---|
| `dotnet build` on `AnyCPU` and `x86`, warnings as errors | 0 warnings, 0 errors |
| Library window against a live, logged-in Steam client (win-x64) | opens, lists the real library |
| Achievement editor for one game (win-x64, app ID 105600) | opens |
| All three windows constructed and rendered headless | pass |
| Light and Dark render differently | pass, compared pixel by pixel |
| `Theme` read back from `settings.json` at startup | pass |
| Directory migration, four cases on Debian with `HOME` redirected | move, repoint, ratings and icons preserved; no-op when `~/.sac` exists; a cache pointed outside `~/.sam` left alone |
| Icon embedded in the executable | 32px entry identical to the master artwork |
| Whole pipeline run in the CI image | all three artifacts produced |
| GitHub Actions workflows linted with `actionlint` (shellcheck included) | clean |
| GitHub workflow shell logic run end to end in a container | same three artifacts, correct architectures |
| Both Windows bundles unzipped and launched against live Steam | win-x64 and win-x86 both open |
| `.deb` installed on a stock Debian 13 | installs with no extra packages, `dpkg -V` clean |
| Application launched from that install under Xvfb | runs, nothing on stderr |
| `lintian` | clean apart from tags inherent to a self-contained .NET bundle |

Not exercised: Steam interop on Linux — the Debian run had no Steam client, so it was only
proved to start. The macOS bundles were not built or run. See also the write path under
Known issues.

### Known issues

Carried forward and still open:

- The **write path** — `SetAchievement`, `SetStatValue`, `StoreStats`, `ResetAllStats` — has
  not been re-verified against a live account since the .NET 10 migration. It mutates a real
  Steam account, so exercise it on a throwaway title first.
- `SteamClient018.GetSteamUtils004` requests `"SteamUtils004"` but marshals the result as
  `ISteamUtils005`. It does not crash, and it predates every migration here, but it bends
  the rule that a version string must match the struct it is marshalled into.
- The capsule-art download queue is eager rather than visible-only, because Avalonia's
  `WrapPanel` does not virtualize.
- Orphaned `achievements` rows are never pruned when a game stops being owned.
- Moving the cache from **Settings** while an editor window is open can fail on Windows,
  because that process holds its own connection to the same database.
- The version appears in the two window titles as a literal rather than coming from the
  assembly version, so a release has to update both by hand. See the checklist in
  [BUILD.md](BUILD.md).
- The licence position is unresolved: the repository carries both an MIT `LICENSE` for the
  new work and the upstream zlib `LICENSE.txt`, which the derived code is bound by. See
  [ATTRIBUTION.md](ATTRIBUTION.md).

---

## Background: inherited from Steam Achievement Manager 8.0

These changes were made in SAM before the fork. They are listed here because they are what
Caretaker 1.0 is built on, not because they are new in it.

- **New Library layout.** The game list became a collections rail plus a grid showing how
  far through each game's achievements you are; the editor pairs the achievement list with
  a detail pane, and gathers uncommitted changes into one bar so you can see what is about
  to be sent to Steam before you send it.
- **All icons are vector.** The toolbars use drawn geometries rather than bitmaps, so they
  take the theme's foreground colour and stay legible in dark mode. Earlier releases used
  the Fugue Icons bitmaps.
- **Two view modes.** *Tiles* shows capsule art with a completion meter. *Content* shows a
  row per game with a small icon, the name, release date, when you last played, the Steam
  review score, earned/total achievements, and your own like/dislike. This data comes from
  Steam's local caches, so it is only available for games Steam has already fetched data
  for; anything else shows `—`.
- **Your own like/dislike,** stored in the home directory as `ratings.json`. It is *not* your Steam review
  — Steam does not expose that locally — and nothing is ever sent to Steam.
- **The library is cached, so the window opens straight away.** Games and their statistics
  go into a SQLite database at `games.db`, capsule art into `icons/`. The
  cached library is shown immediately while the list is re-checked against Steam in the
  background; games no longer owned are dropped and new ones added.
- **Achievements are cached too.** After the first visit to a game, its achievement list
  appears as soon as the window does, and is refreshed from Steam in the background. Icons
  are downloaded once.
- **Progress is shown while loading** in both windows, instead of appearing to hang on a
  large library.
- **Migrated from .NET Framework 4.8 to .NET 10.** The default `AnyCPU` build runs as a
  64-bit process and talks to the 64-bit Steam client; build the `x86` configuration if you
  need a 32-bit process.
- **Migrated from Windows Forms to [Avalonia](https://avaloniaui.net/).** The interop layer
  was unchanged; only the UI was rewritten.
- **Cross-platform builds.** The interop layer no longer depends on `kernel32` or the
  Windows registry: it uses `NativeLibrary` and per-OS Steam path discovery, so the projects
  target plain `net10.0` and publish for Windows, Linux and macOS.
- Support for the current `UserGameStatsSchema` format, alongside the older one.
- Achievement unlock times are shown in the editor.
- Fixed a long-standing bug in the `ISteamClient::GetISteamApps` interop signature, which
  was missing the `this` pointer. It went unnoticed for years in 32-bit builds but returns a
  null interface in 64-bit ones.
- Fixed `ISteamUser::GetSteamID` on Linux and macOS, where System V AMD64 returns a small
  struct in a register rather than through a hidden pointer. The Windows form silently
  returned 0, poisoning every lookup keyed off the account ID.
