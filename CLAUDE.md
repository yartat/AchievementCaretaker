# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

**Achievement Caretaker** — a portable desktop tool that reads and writes
achievements and stats for games in a Steam library. It works by loading Steam's own
`steamclient.dll` in-process and calling its **C++ COM-like vtable interfaces** directly
through hand-rolled P/Invoke. There is no Steamworks SDK dependency: the entire native
surface is reimplemented in `AchievementCaretaker.SteamApi`, which has no package
references at all.

**What the project is based on differs by part, and each file says which.** The Steam
support (`AchievementCaretaker.SteamApi` and the Steam half of the application) is based on
[Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) (SAM) by Rick
(gibbed) and is an *altered source version* of it in the sense the zlib license means. That
attribution is not decoration: it is a licence condition, and it must be preserved in those
files' headers, the README, the release notes and `docs/ATTRIBUTION.md`. The GOG provider is
based on [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by
Timendum. The Epic provider, the rest of the store code and the CI and packaging files are
this project's own work. **Do not strip an attribution when editing a file, and give a new
file the header for what it actually derives from: see Conventions.**

Caretaker diverges from SAM far enough to carry its own name: the two executables were
merged into one, the Windows Forms UI was replaced by Avalonia, the library and achievement
lists are cached in SQLite, and the game list was rebuilt around completion.

The UI is **Avalonia 12**. `AchievementCaretaker.SteamApi` carries no UI dependency
and no package reference of any kind.

## Language

Code, comments, identifiers, commit messages and every document in this repository are
written in **English**. Model output to the user in chat is in **Russian**; nothing written
to a file is.

## Projects

| Project | Output | Role |
|---|---|---|
| `AchievementCaretaker.SteamApi` | `AchievementCaretaker.SteamApi.dll` (class library) | All native interop. Loads `steamclient.dll`, resolves vtables, marshals calls, pumps callbacks. Also owns the Valve KeyValues parsers (`KeyValue` binary, `TextKeyValue` text). |
| `AchievementCaretaker.App` | `bin/AchievementCaretaker.exe` (WinExe) | The whole application: the library window and the per-game achievement editor, in one binary. |
| `AchievementCaretaker.Providers` | `AchievementCaretaker.Providers.dll` (class library) | **Branch `feature/epic-achievements` only.** Read-only library and achievements of other stores (Epic Games, GOG) over HTTPS, behind `IStoreProvider`. Shown by the library window as the Epic and GOG sources. |

`AchievementCaretaker.slnx` is the solution, in the **`.slnx`** format. There is no
`.sln`; do not add one back. Its `Documentation` folder carries `README.md`, `CLAUDE.md`,
`LICENSE.txt` and everything under `docs/`.

### Store providers (research branch)

`AchievementCaretaker.Providers` reads the library and achievements of other stores,
each behind `IStoreProvider`: `Epic/EpicProvider` and `Gog/GogProvider`. It and the
throwaway `tools/ProviderProbe` (listed in the `.slnx` so it builds with everything, never
published) are research, written up in `docs/EPIC-RESEARCH.md` and `docs/GOG-RESEARCH.md`.
The rules:

- **Steam is not a provider.** Its interface is native, scoped to one app id per process,
  and can write. A store reached over HTTPS with the user's own sign-in has none of those
  properties, and hiding them behind one interface would hide what shapes how Steam is used.
- **Epic is read-only; GOG can write.** Epic: no unlocking, no re-locking, no EOS SDK, no
  reading a game's client secret. Epic cannot re-lock an achievement anyway, and the tools that
  unlock inject into the game or need that secret. GOG: changing an achievement needs a token
  issued to the *game's* client, so `GogProvider.SetAchievementsAsync` reads the client secret
  from GOGDB's build record, **only when something is written**. Listing reads the client id
  and nothing else. The secret and the game token are never logged, kept on disk or put in an
  error message, and the account's own session is not touched by the game token. Writing is a
  separate interface, `IWritableStoreProvider`, which only GOG implements; the achievements
  window shows the checkboxes and the Lock / Invert / Unlock buttons only for a provider that
  has it, and sends the pending changes with Commit as the Steam editor does. Verified against a
  live GOG account (unlock and re-lock), unlike Steam's write path.
- **Every unofficial endpoint lives in its store's one `*Provider.cs`.** They are the
  stores' own and can change; keep them in one file each.
- **A session file holds a refresh token**, a credential for the whole store account:
  `~/.sac/epic.json`, `~/.sac/gog.json`. They go through `SessionFile`, which writes them
  user-only on Unix. Never log one, print one or put one in an error message.
  `ThrowIfEpicError` and `ThrowIfError` deliberately do not echo response bodies, and
  GOG's token request carries the code or refresh token in its URL, so that URL is never
  part of a message either.
- **Sign in with the store's own page, in the user's browser.** Caretaker never sees a
  password and never reads another application's stored credentials. GOG Galaxy keeps its
  refresh token in the registry and the reference tools take it from there; this does not.
- **Unknown is null, not zero.** `StoreAchievement.XP`, `Progress` and `RarityPercent` are
  nullable because a store may not have them: GOG has no XP and no progress.
- **Adding a store** means implementing `IStoreProvider` and listing it in
  `StoreProviders.CreateAll`. Nothing else has to change to see it.

How the library window shows them. One source is on screen at a time, chosen in the rail's
SOURCE section; Steam is the default and is `SelectedStoreLibrary == null`, not an entry.

- **Store games are their own types, not `GameInfo`.** `GameInfo`, the `games` table,
  `RatingStore` and `IconCache` are all keyed by a Steam app id, and a store's id is a string.
  The counterparts are `StoreGameInfo`, the `store_games` table, `StoreImageCache` and
  `StoreLibraryViewModel`, which is a parallel of the Steam half of `GamePickerViewModel`,
  not an extension of it. `GamePickerViewModel.Stores.cs` is the one place the two meet. A
  store has no playtime, last played, review rating or demo/mod types, so it offers only the
  All / Has achievements / Perfect collections and sorting by name or completion. No likes
  and no Recently played yet.
- **`store_games` lives in `GameCache`, not in a second class.** `GameCache` owns the one
  connection to `games.db` and its `MoveTo`; a second connection would block a cache move on
  Windows. The Steam library saves from a pool thread while the stores read and write from
  the UI thread, so every public `GameCache` member takes `_Gate`. The store libraries reach
  the cache through a `Func<GameCache>`, because `MoveTo` replaces the instance.
- **Store images live in `<icon cache>/store/<provider>/`, named by a hash of the URL**
  (`StoreImageCache`). The subfolder is what keeps `IconCache.Prune`, which parses app ids out
  of file names and only walks the top level, away from them. `IconCache.MoveTo` moves the
  subfolder along with the files; keep it doing that. Nothing prunes these images.
- **A store game's achievements open in a window of this process**
  (`StoreAchievementsWindow`), not through `Program.LaunchForApp`. The one-process-per-game
  rule exists because `ISteamUserStats` is scoped to the app id a process initialised with;
  a store's HTTPS service has no such scope.
- **Progress is read on request, never automatically.** A library listing carries no
  achievement counts, and reading them costs a request or more per game (GOG's two more to
  GOGDB, which is run by volunteers). A game's counts are known once its window has been
  opened or **Load progress** has covered it; until then the meter is absent, not empty.
  Unknown is `null`, never 0.
- **Sign-out and an expired session forget the account's library**, including the cached
  progress: progress belongs to an account, and the next account to sign in must not be shown
  the previous one's numbers. A dead session is forgotten too, or the sign-in panel would
  never come back; `EpicProvider` and `GogProvider` both report a refused refresh as
  `login_required` for that.
- **An element's own bindings read from its own `DataContext`.** The store view is hosted
  with `DataContext="{Binding SelectedStoreLibrary}"`, so its visibility is on a wrapper
  `Panel`, not on the view itself, or `IsVisible="{Binding IsStoreSelected}"` is looked up on
  the store.
- `packaging/check-version.sh` checks the App and SteamApi projects only. When this ships,
  add `AchievementCaretaker.Providers`, or its version silently stays behind.

### One executable, two windows

SAM shipped `SAM.Picker.exe` and `SAM.Game.exe` as separate programs. Caretaker ships one
`AchievementCaretaker.exe`, but it still starts **one process per game**, because that
part was never a packaging choice:

> **`ISteamUserStats` is scoped to the one app id the process was initialised with.**

A process that called `Initialize(0)` to enumerate the library cannot then ask Steam for
one game's achievements. So the executable dispatches on its command line:

| Command line | Window | Steam initialised with |
|---|---|---|
| *(no arguments)* | `GamePickerWindow` — the library | app id `0`, once Steam is there |
| `<appid>` | `ManagerWindow` — the achievement editor | that app id |

`Program.LaunchForApp` starts a second copy of **this same executable**
(`Environment.ProcessPath`) with the app id, and `App.CreateMainWindow` chooses the window.
Do not try to "finish the merge" by opening the editor as a second window in the same
process, and do not try to "just call `RequestUserStats` in a loop" from the library
window — that is a dead end, not an optimisation problem.

The executable refuses to run from the Steam install directory
(`Program.IsRunningFromSteamDirectory`).

### Layout inside `AchievementCaretaker.App`

| Folder | Namespace | Holds |
|---|---|---|
| *(root)* | `AchievementCaretaker.App` | `Program`, `App`, `InvariantShorthand`, `GlobalSuppressions`, `app.manifest` |
| `Common/` | `…App.Common` | `AppSettings`, `IconCache` — settings and the icon directory, used by both windows |
| `Library/` | `…App.Library` | `GameInfo`, `GameCache`, `LibraryStats`, `RatingStore`, `OwnRating` |
| `Achievements/` | `…App.Achievements` | `AchievementCache`, `AchievementFilter` |
| `Achievements/Stats/` | `…App.Achievements.Stats` | the schema model — `AchievementDefinition`, `StatInfo`, … |
| `ViewModels/` | `…App.ViewModels` | `ProgressViewModel`, `GamePickerViewModel`, `ManagerViewModel`, `SettingsViewModel` |
| `Views/` | `…App.Views` | the windows, plus the hand-rolled `MessageWindow` |
| `Resources/` | — | `Icons.axaml`, `Theme.axaml`, `AchievementCaretaker.ico` |

These used to be three projects plus a `Shared/` folder linked into both executables with
`<Compile Include="..\Shared\*.cs">`. That machinery is gone: one assembly needs no links,
and `Icons.axaml` no longer has to be given a matching `avares` path in two places.

One C# rule this layout keeps tripping over: **a `using` directive imports a namespace's
types but not its nested namespaces.** `ManagerViewModel` refers to the schema model as
`Stats.AchievementInfo`, so it carries `using Stats = …App.Achievements.Stats;` — a
namespace alias, not a plain using. The same applies to anything else that wants to
qualify by folder.

Both projects target **`net10.0`** — plain, not `net10.0-windows`. The SDK version is
pinned in `global.json`; do not remove it, or builds drift onto whatever preview SDK is
installed.

## Platform support

The managed code is portable. What is not portable is **Steam's own client library**, which
the application loads into its own process — so the process architecture and the library
architecture must match, and Valve only ships x86/x64 builds.

| Target | Steam client library | Works? |
|---|---|---|
| `win-x64` | `steamclient64.dll` | **Verified** |
| `win-x86` | `steamclient.dll` | **Verified** |
| `linux-x64` | `linux64/steamclient.so` | **Verified** against live Steam (Debian 13, WSL2) |
| `osx-x64` | `steamclient.dylib` | Builds; **untested** |
| `win-arm64` | none — Valve ships no ARM client | Builds, cannot load Steam |
| `linux-arm64` | none | Builds, cannot load Steam |
| `osx-arm64` | none | Builds, cannot load Steam |

The ARM64 bundles exist so the build matrix is complete, and
`SteamPlatform.IsArchitectureSupported` makes them fail at startup with an explanation
rather than a misleading "Steam is not running". On Arm hardware the **x64** bundle is the
one to run — Windows on Arm and Rosetta 2 emulate it, and they are emulating the Steam
client itself anyway. Do not "fix" the ARM builds by loosening that check; the limitation
is Valve's, not ours.

Windows and Linux were both exercised end to end against a live, logged-in Steam client
while this code was still SAM — native build, install-path discovery,
`steamclient.so`/`.dll` load, vtable dispatch, `LibraryStats`, and the game list showing
the real library. Playtime read on Linux matched Windows to the minute for every game
checked. The macOS candidate lists are written from Steam's documented layouts and have
never been run.

### Linux runtime prerequisites

A framework-dependent build needs more than the .NET runtime: Avalonia's Skia and X11
backends dlopen system libraries that a minimal Debian does not ship. Found empirically by
launching the app on a bare Debian 13 and fixing one `DllNotFoundException` at a time:

```bash
apt-get install -y libfontconfig1 libice6 libsm6
```

`libfontconfig1` is needed before `libSkiaSharp` will load at all; `libice6`/`libsm6` are
needed by `Avalonia.X11`'s session management. With those three the app starts cleanly.
Package these as documented dependencies for any Linux release.

## Build

```bash
dotnet build AchievementCaretaker.slnx -c Release
```

The executable lands in `bin/` (64-bit). `-p:Platform=x86` builds 32-bit into **`bin/x86/`**.

The solution platform mapping is spelled out in `AchievementCaretaker.slnx`
(`<Platform Solution="*|x86" Project="x86" />`). Without it a solution-level
`-p:Platform=x86` silently builds AnyCPU — the `.slnx` reader does not infer the mapping.

`RuntimeIdentifier` defaults to `$(NETCoreSdkPortableRuntimeIdentifier)` — the *host's* RID
— so a plain build on Linux or macOS produces an apphost for that machine. Do not hardcode
`win-x64` as the default: an earlier revision did, and `dotnet build` on Debian happily
emitted a `.exe` PE32+ binary with Windows `.dll` natives.

**The two platforms must not share an output directory.** With a pinned RID the native
Skia/HarfBuzz libraries are copied flat next to the executable rather than into
`runtimes/<rid>/native/`, so an x86 build and an x64 build writing to the same folder
silently overwrite each other's natives — and the survivor dies at startup with
`The version of the native libSkiaSharp library (88.1) is incompatible`, which is really an
architecture mismatch wearing a version-number disguise. That is why `OutputPath` is
conditioned on `$(Platform)`.

To produce a bundle for a specific target, publish rather than build. There is only one
executable now, so there is only one publish command:

```bash
dotnet publish AchievementCaretaker.App/AchievementCaretaker.App.csproj \
  -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

A framework-dependent build needs the .NET 10 Desktop Runtime present to run. `bin/` must
therefore ship `.exe`, `.dll`, `.runtimeconfig.json`, `.deps.json` and the native
`libSkiaSharp` / `libHarfBuzzSharp` / `av_libglesv2` DLLs. Shipping only the `.exe`
produces something that will not start.

**The exe project pins a `RuntimeIdentifier`** (`win-x64`, or `win-x86` on the x86
platform) with `AppendRuntimeIdentifierToOutputPath=false`. Do not remove this. Without a
RID, a framework-dependent build copies the native assets for *every* RID that Avalonia and
Skia ship — Linux, musl, macOS, Android, riscv, loongarch — which came to **562 MB** in
`bin/` and made the release artifact unusable.

There are **no tests** in this repository and no test framework is referenced. That does
not mean nothing can be checked without Steam — see "Verifying without Steam" under
[Running](#running).

## Running

The Steam features (the Steam library, the achievement editor) need the Steam client
installed, running, and logged in; the library window itself opens without it. On Windows the
install path
comes from `HKLM\Software\Valve\Steam\InstallPath`; elsewhere the candidate directories in
`SteamPlatform.GetUnixInstallCandidates` are walked. Without a live Steam process
`Client.Initialize` throws `ClientInitializeException`. You cannot meaningfully exercise
this code in CI or a sandbox — changes to interop must be tested by hand.

### Verifying without Steam

Plenty of this codebase *can* be exercised on a machine with no Steam at all, and should be
before anything is called done. There are no tests in the repository, so this means a
throwaway console project that references `AchievementCaretaker.App`:

- **Windows, resources and styles** — `Avalonia.Headless` plus `Avalonia.Skia`, with
  `UseHeadlessDrawing = false`. `AppBuilder.Configure<App>().UseSkia().UseHeadless(…)
  .SetupWithoutStarting()` runs `App.Initialize()` without a lifetime, so
  `OnFrameworkInitializationCompleted` skips the Steam handshake. Each window can then be
  constructed, shown, measured, arranged and rendered with `CaptureRenderedFrame()`. This
  is the only automated thing that catches the right-hand column of the table under
  [The UI layer](#the-ui-layer), and it is cheap — run it after any XAML change.
- **The SQLite layer, `IconCache`, `RatingStore`, `ProgressViewModel`** — plain classes.
  Reference the project and drive them. `AppSettings.HomeDirectory` is a static path with
  no seam, so anything touching it runs against the real `~/.sac`: check the directory does
  not already exist and restore the machine afterwards, or do not run it at all.
- **Icon geometries** — render them and measure the ink. Do **not** use `Geometry.Bounds`
  for this: for an arc it reports the endpoint extents rather than the swept extents, so
  `RefreshIcon` measures 8.2 wide on a 16 grid and renders 15.

Starting `bin/AchievementCaretaker.exe` with no Steam running is also a real check: it
must come up as the library window, with the "No active Steam" banner and the cached games,
rather than an error or nothing, which proves `App.Initialize` loaded every style and resource
dictionary. With an app id and no Steam it shows the error `MessageWindow` instead: the
editor cannot do anything without Steam.

What none of this can reach is anything behind `SteamApi.Client`: the vtable dispatch, the
schema parse, `LibraryStats`, and every number the windows actually display.

## Architecture

### The interop pattern (`AchievementCaretaker.SteamApi`)

This is the part that matters. Everything else is ordinary application code.

1. `Steam.Load()` (`Steam.cs`) asks `SteamPlatform` for the install path and the
   candidate library paths for this OS and bitness, then loads the first one that exists
   with `NativeLibrary.TryLoad` and binds three exports via `NativeLibrary.TryGetExport`:
   `CreateInterface`, `Steam_BGetCallback`, `Steam_FreeLastCallback`. There is no
   `kernel32` P/Invoke — `NativeLibrary` maps to `LoadLibraryEx`/`dlopen` per OS and
   resolves the library's own dependencies, which is what the old `SetDllDirectory` call
   was doing.
2. `Steam.CreateInterface<T>("SteamClient018")` returns a raw `IntPtr` to a C++ object.
3. `NativeWrapper<TFunctions>.SetupFunctions` (`NativeWrapper.cs`) treats that pointer as a
   `NativeClass` (one field: `VirtualTable`), then `Marshal.PtrToStructure`s the vtable into
   a `struct` of `IntPtr` fields — one field per virtual method, **in declaration order**.
4. Calls go through `Call<TReturn, TDelegate>(Functions.SomeMethod, ...)`, which
   `Marshal.GetDelegateForFunctionPointer`s the slot (cached in `_FunctionCache`) and
   `DynamicInvoke`s it. Instance methods use
   `[UnmanagedFunctionPointer(CallingConvention.ThisCall)]` with the object pointer passed
   explicitly as the first argument.

### Bitness

On .NET Framework, `AnyCPU` executables defaulted to `Prefer32Bit`, so SAM always ran
32-bit and always loaded `steamclient.dll`. On .NET 10 that default is gone: `AnyCPU` now
means a 64-bit process loading `steamclient64.dll`. The `Environment.Is64BitProcess` branch
handles it, but it means the default build talks to a different Steam binary than SAM used
to. If something works in the `x86` configuration and not in `AnyCPU`, this is why.

`CallingConvention.ThisCall` itself is not a portability problem. `ThisCall` is only
meaningful on **Windows x86**; on x64, ARM and ARM64 there is a single calling convention
and the attribute is ignored. Since every wrapper passes the object pointer as an explicit
first argument, those declarations are correct on every target.

**Struct returns are a different story — see invariant #4.** "One calling convention on
x64" covers how *arguments* are passed; it does not cover how a C++ member function
*returns a struct by value*, and Windows and System V disagree there.

### Four invariants you must not break

**1. Vtable struct field order is an ABI contract.** The structs in `Interfaces/`
(`ISteamClient018`, `ISteamUserStats013`, `ISteamUtils005`, …) mirror the exact layout of
Valve's C++ interfaces. Every field is `IntPtr` and the *order* is the only thing carrying
meaning. Inserting, removing, or reordering a field silently calls the wrong function —
usually a crash, sometimes corruption. Never reorder these. Deprecated slots are kept as
`DEPRECATED_*` placeholders precisely for this reason.

**2. Interface version strings must match the struct they are marshalled into.** The
string passed to `CreateInterface` / `GetISteam*` selects which vtable Steam hands back.
Requesting one version and interpreting it as another is the same bug as reordering
fields. Keep `GetSteamUserStats013` ↔ `"STEAMUSERSTATS_INTERFACE_VERSION013"`,
`GetSteamApps001` ↔ `"STEAMAPPS_INTERFACE_VERSION001"`, and so on.

**3. Every vtable delegate needs `[UnmanagedFunctionPointer(CallingConvention.ThisCall)]`
and an explicit `IntPtr self` first parameter.** These are C++ member functions; the object
pointer is an argument and must be passed as `ObjectAddress`. `GetISteamApps` was missing
both for years. On x86 it went unnoticed, because dropping `this` still left the remaining
arguments at the stack offsets `thiscall` expected. On x64 there is one register-based
convention, so every argument shifted by one, Steam returned a null interface, and
`SetupFunctions` threw a `NullReferenceException` — which is how it finally surfaced, once
the .NET 10 migration made x64 the default. When adding an accessor, copy the shape of
`GetISteamUser`, and test in **both** bitnesses; x86 hides this entire class of bug.

**4. A C++ method that returns a struct *by value* needs a per-platform declaration.**
Windows and System V AMD64 disagree about how that works, and the difference is silent:

- **MSVC x86/x64** returns it through a hidden pointer passed as an extra argument, so the
  delegate is `void Native(IntPtr self, out T value)`.
- **System V AMD64** (Linux, macOS) returns a small trivially-copyable struct in RAX, so
  the delegate is `T Native(IntPtr self)`.

`ISteamUser::GetSteamID()` returns `CSteamID`, a 64-bit POD, and is the one place this
arises today. With the Windows form used on Linux it does not throw — it reads a slot
nobody wrote and returns **0**, which then silently poisons everything keyed off the
account id (`LibraryStats`' playtime and achievement lookups, and
`RequestUserStats(steamId)` in the editor). `SteamUser012.GetSteamId` therefore branches on
`OperatingSystem.IsWindows()` and caches its delegate locally, because `NativeWrapper`'s
cache is keyed on the function pointer alone and two delegate types sharing one vtable slot
would collide.

An audit of the other `out`/`ref` delegates found no further cases: the parameters in
`CreateLocalUser`, `GetStat`, and `GetImageSize` are genuine C++ pointer arguments, not
hidden struct returns. If you add a wrapper for a method whose C++ signature returns a
`struct`/`class` by value, handle both ABIs and verify on both.

### Callbacks

Steam delivers results by callback, not return value. `Client.RunCallbacks(false)` drains
`Steam_BGetCallback` in a loop and dispatches by numeric `Id` to registered `ICallback`
instances. Both view models poll it from an Avalonia `DispatcherTimer` (`OnTimer`, 200 ms),
so callbacks arrive on the UI thread. `_RunningCallbacks` guards against re-entrancy.
Stopping that timer on window close is what `Shutdown()` is for.

`ManagerViewModel` drives everything from `OnUserStatsReceived`: it loads the schema, then
populates achievements and stats. `RefreshStats` only *requests* — nothing is populated
synchronously.

### The UI layer

- `Program.Main` builds the `AppBuilder` and calls `StartWithClassicDesktopLifetime`.
- `App.OnFrameworkInitializationCompleted` *chooses* the main window: the library, the
  editor, or a `MessageWindow` carrying the error. This replaces the WinForms pattern of
  showing a `MessageBox` before `Application.Run`.
- **Only the editor does the Steam handshake up front.** It is one game's Steam session and
  is useless without one, so a failure there is the error window; its `SteamApi.Client` is
  owned by `App` and disposed on `ShutdownRequested`. **The library does not**: it has other
  sources than Steam, so it opens at once and `SteamConnector` keeps trying to create the
  Steam pipe in the background (every 3 s, off the UI thread, a fresh `Client` each try).
  Only when that works does `GamePickerViewModel.ConnectSteam` register the callback, start
  the callback timer and refresh the library. Until then the Steam source shows the cached
  games under a "No active Steam" banner with the reason (not installed, not running, nobody
  logged in), the refresh button is off, and what needs Steam (opening the editor, the App
  ID box) says so rather than failing. The connector owns the client and the view model
  disposes it in `Shutdown`.
  **It keeps watching after it connects.** A pipe to a Steam that quit does not say so, so
  `SteamWatch` compares the Steam process id taken at connection
  (`SteamPlatform.GetRunningClientProcessId`: the registry's `ActiveProcess\pid` on Windows,
  checked against the process list) with the one running now, every 3 s. A Steam that quit and
  came back has a new id and a dead old pipe. On loss the client is `Abandon`ed, not disposed
  (releasing a pipe means calling into Steam's library over a dead connection), the view model
  stops its timer and callback, turns refresh off and shows the banner again, and the connector
  starts looking again. **A refresh remembers `_SteamGeneration` and throws its results away if
  it changed**: on a dead pipe the ownership sweep sees nothing owned, and merging that would
  empty the library and then the cache. The editor watches too and locks itself
  (`IsInputEnabled`) when its Steam goes, since it can neither read nor commit without one.
  Not exercised against a real Steam restart: whether the already-loaded client library can open
  a pipe to a Steam that started after it was loaded; nor the Linux and macOS process lookup.

  **Only a finished synchronization may delete a game.** The ownership sweep is one Steam call
  per published app id, and once Steam is gone every remaining call answers "not owned", so a
  sweep cut short looks exactly like an account that owns almost nothing (it once left 48 games
  of 600). `LoadGamesAsync` therefore always *adds and updates* what it found, and returns true
  only if the sweep **and** the stats pass both ran to the end under the same Steam; only then
  are the games Steam no longer lists removed. "Under the same Steam" is asked directly with
  `SteamConnector.IsSteamStillRunning()` as each pass finishes, because the watch notices a loss
  only every few seconds, which is long enough for the whole sweep. The cache follows the same
  rule: `GameCache.Sync(rows, removeMissing: false)` and no `IconCache.Prune` unless the sync
  completed. A known name is never replaced by an empty one, and a failed download of the
  published list changes nothing on screen. Not covered: a Steam that stays up but has not
  finished loading its licences answers incompletely too, and would pass for complete.
- `ViewModels/` hold all logic and every Steam call. Views are XAML plus thin code-behind.
- `Views/MessageWindow.cs` is a hand-rolled stand-in for `MessageBox`, which Avalonia has
  no equivalent of. View models cannot show dialogs (no window to parent to), so they raise
  `ErrorRaised` / `MessageRaised` / `ConfirmRequested` and the window handles them. It
  doubles as the main window when the editor's Steam handshake fails, which is why it sets
  `ShowInTaskbar`; `ShowAsync` returns a completed task when there is no owner to parent to.
- Bindings are compiled (`AvaloniaUseCompiledBindingsByDefault`), so every `DataTemplate`
  needs an `x:DataType` and binding typos are build errors rather than silent no-ops.
  **That is the only part of the XAML the build checks.** Know which side of the line you
  are on:

  | Caught at build | Only caught when the window opens |
  |---|---|
  | `{Binding Foo}` where `Foo` is not on the `x:DataType` | `{StaticResource Foo}` where `Foo` does not exist |
  | `{CompiledBinding}` paths | `<StyleInclude Source="...">` pointing nowhere |
  | unknown control or property names | a property fed a resource of the wrong type |

  Two of those three have already bitten this codebase: a `Grid.ColumnDefinitions` fed
  from an `x:String` resource compiled cleanly and threw `InvalidCastException` at
  construction, and renaming a key in `Resources/Icons.axaml` still builds with zero
  warnings. **A green build does not mean the window opens** — see
  [Verifying without Steam](#verifying-without-steam) for how to check without a Steam
  client.
- **Do not bind `ToggleButton.IsChecked` to view-model state.** A `ToggleButton` flips its
  own `IsChecked` on click, which fights the binding and leaves the button showing a state
  that was never saved. The like/dislike buttons are plain `Button`s with
  `Classes.liked="{Binding IsLiked}"` and a style selector doing the colouring, so the view
  model stays the single owner of the value.
- **The Fluent `ProgressBar` has `MinWidth="200"`, and it beats both the column and an
  explicit `Width`.** A completion meter in the content view's 132-wide column rendered 200
  wide and ran under the Steam value on one side and the like button on the other; the one
  in a 183-wide tile stuck out past the capsule. Both meters now set `MinWidth="0"`. Any
  new `ProgressBar` narrower than 200 needs the same. The two status-bar bars still say
  `Width="130"` and actually render at 200 — harmless there, but the markup is not the truth.
- The content view's header and rows are two grids that must agree on **both** column
  widths and horizontal inset. Every column after the `*` one is laid out from the right
  edge, so an inset mismatch shifts all of them against their headers. The rows get the
  ListBox's 14 plus the Fluent `ListBoxItem`'s 12, which is why the header's padding is 26.
  **The window's `MinWidth` (1000) is derived from the same numbers**: rail 204 + inset 52 +
  fixed columns 578 = 834 before Name gets anything. It used to be 720, so the window could
  be shrunk until the Name column vanished and the dislike button was clipped. Widen any
  column and re-derive it.
- **A `DockPanel` child with no `DockPanel.Dock` is docked `Left` and sized to its content;
  only the last child fills.** The library's tile list was the middle child of the main
  area, beside the content view, so it was only as wide as one row of tiles: its scrollbar
  stood next to the last tile instead of at the window edge, and the wheel did nothing over
  the empty space to its right. Both views now sit in one `Panel` as the last child. Put a
  list's horizontal inset in `Padding`, not `Margin`, too: padding is inside the scroll
  area, so the scrollbar stays at the edge.
- Images are `Avalonia.Media.Imaging.Bitmap`. Unlike `System.Drawing.Bitmap` it copies the
  decoded pixels, so the source stream can be disposed immediately — which is why the old
  "bitmap outlives its MemoryStream" bug does not exist in the ported code.

### The Library layout

Both windows follow the Library design. The organising idea is **completion**: how far
through a game's achievements you are is the thing the library window is built to show, not
a column you can sort by if you think to.

**The library window** is a left rail plus a main area. The rail carries two kinds of thing
and they must not be made to look alike:

- **Collections** (`GameCollection`) are mutually exclusive — All games, Recently played,
  Has achievements, Perfect games, Liked. They are buttons with a `Classes.on` binding.
- **Also show** are the `ShowGames` / `ShowDemos` / `ShowMods` / `ShowJunk` toggles, which
  are inclusive and combine. They are `CheckBox`es, which is the whole reason they look
  different.

Both narrow the same pass in `RefreshGames`, collection first, then type.

Rail counts are computed over the **whole library**, not the filtered view, so a count
never changes just because another collection is open. `RefreshCollectionCounts` has to be
called after anything that moves the library or its stats — the cached load, the ownership
merge, the stats pass, and a rating change. Completion only becomes known in the stats
pass, so that one also re-runs `RefreshGames` when a collection or sort depends on it.

The tile grid carries the meter. **The meter is absent, not empty, when Steam has never
cached a schema** (`HasCompletion`): an empty track would read as "none earned", and
unknown and zero are different answers. Both views still bind the same `FilteredGames`;
only `IsVisible` differs.

**The editor** is a game header, filter chips, a list, and a detail pane. The detail pane
exists because the description had nowhere to go in the old three-column grid.

Each list row carries the name with the description under it on **one trimmed line**
(collapsed when empty, as it is for hidden achievements), and a fixed 96-wide unlock-date
column so the dates line up. `AchievementInfo.UnlockDateText` gives the day only, in the
library's `d MMM yyyy`, from `UnlockTime`, which is already local time. It is blank for a
locked achievement and `—` for one Steam reports unlocked with no timestamp — keyed on
`OriginalValue`, not `IsAchieved`, so a ticked-but-uncommitted box does not claim an unlock
whose date was lost.

Content columns are: capsule, name, release date, last played, Steam rating, achievements
(count plus meter), and the user's own like/dislike. Headers are buttons that set the sort.

### The palette

`Resources/Theme.axaml` is a `ResourceDictionary` with `ThemeDictionaries` for Light and
Dark, merged into `Application.Resources`. **Every `Sam*` brush must be referenced with
`{DynamicResource}`.** A `{StaticResource}` resolves once and then lies, and there are now
two ways for the variant to change under it: `ThemeVariant.Default` follows the operating
system while a window is open, and the Settings dialog previews a chosen variant live.

The variant itself comes from `AppSettings.Theme` (`Common/AppTheme.cs`), applied in
`App.OnFrameworkInitializationCompleted` before the window is built. `AppTheme.System` maps
to `ThemeVariant.Default` — it is not a third palette, it is what makes Avalonia track the
system setting. Each process reads the setting for itself, so a change made in Settings
reaches an editor window that is already open only when that window is relaunched.

Gold is the accent because the subject is achievements, and it is deliberately **not the
same value in both variants**: `#DDA63A` reads well on near-black and manages about 2:1 on
white, so the light variant darkens it to `#8A6612`. Do not "unify" them.

`RatingLikeBrush` and `RatingDislikeBrush` sit outside the theme dictionaries on purpose —
they are semantic tokens and mean the same thing in either variant.

### Icons

Every icon is a `StreamGeometry` in `Resources/Icons.axaml`, rendered through a `PathIcon`.
There are no icon bitmaps left; the Fugue PNGs the SAM toolbars carried were removed
because a `PathIcon` inherits the theme foreground and stays legible in dark mode, which
they did not, and because two icon mechanisms cannot be sized or coloured from one place.
**Do not put an `<Image>` in a button.**

Two things about that file are load-bearing:

- **One grid.** Every geometry is drawn on a 16x16 grid with its content spanning roughly
  1..15. `PathIcon` stretches the geometry's *own bounds* uniformly into `Width` x
  `Height`, so a glyph drawn on a different grid is silently scaled to a different weight
  beside its neighbours. `Geometry.Bounds` will not tell you when this has happened —
  for an arc it reports the endpoint extents, not the swept extents, so `RefreshIcon`
  measures 8.2 wide and renders 15. Measure the rendered pixels instead.
- **One style.** `Resources/Icons.axaml` also carries the bare `PathIcon` selector that
  sizes everything to 14x14, plus `PathIcon.small` at 10 for a glyph that qualifies another
  rather than standing alone. Add a class there rather than a `Width`/`Height` on the
  usage, or the toolbars drift apart one button at a time.

**The application icon is a different thing** and does not live there: it is
`Resources/AchievementCaretaker.ico`, a seven-size Windows icon whose vector master
is `docs/assets/app-icon.svg`. Its 16px entry is drawn separately rather than downscaled;
see `docs/BUILD.md`. Do not try to fold it into `Icons.axaml` — `ApplicationIcon` needs a
real `.ico` file on disk.

It is included as `<StyleInclude Source="/Resources/Icons.axaml" />`. That path resolves at
**runtime**, not build time: renaming a key or mistyping the source still compiles cleanly
and only fails when the window opens.

### The status bar

Both windows derive their view model from `ViewModels/ProgressViewModel.cs` and show the
same indicator: an icon, a line of text and a `ProgressBar`.

Phases with a known total count out of it; those without — the game-list download, and the
editor waiting on the `RequestUserStats` callback — show the running bar instead. Loops
that run off the UI thread report through an `IProgress<int>` **created on the UI thread**,
which is what marshals the callback back; `ProgressStep` throttles them to about 200 updates
so a sweep over tens of thousands of published app ids does not post a message per item.

`BeginProgress` and `EndProgress` count jobs rather than flipping a flag, and this matters:
the library window's startup runs the cached-icon decode, the library refresh and the logo
queue at once, all reporting into the one indicator. With a flag, the first to finish would
take the bar down while the other two were still working. Every `BeginProgress` needs a
matching `EndProgress` on every path, failures included.

Where the numbers come from matters, because the obvious approach does not work — see
[One executable, two windows](#one-executable-two-windows) for why the library window
cannot ask Steam for another game's achievements. `Library/LibraryStats.cs` reads Steam's
own on-disk caches instead:

- **Total achievements** — `appcache/stats/UserGameStatsSchema_<appid>.bin`, the same file
  and parser `ManagerViewModel` already uses.
- **Earned achievements** — `appcache/stats/UserGameStats_<accountid>_<appid>.bin`. Under
  `cache`, each numbered block holds a `data` Int32 **bitfield** plus an
  `AchievementTimes` subkey. Earned is the popcount of `data` masked to the bits the
  schema defines for that block. (Counting `AchievementTimes` entries gives the same
  answer, but the bitfield is the authoritative field.)
- **Playtime and last played** — `userdata/<accountid>/config/localconfig.vdf`, at
  `UserLocalConfigStore/Software/Valve/Steam/apps/<appid>/`, as `Playtime` (in **minutes**)
  and `LastPlayed` (unix seconds).
- **Steam rating and release date** — `SteamApps001.GetAppData`, keys `review_percentage`
  (percent positive), `review_score` (Steam's 1-9 band, used for the tooltip wording) and
  `steam_release_date` (unix seconds). Measured coverage across one library: review data
  ~98%, release date ~91%. These are Steam calls, so they run on the background pass with
  the file reads, not on the UI thread.
- **The user's own like/dislike is *not* from Steam.** Steam keeps review recommendations
  server-side and exposes them only through the Web API, which needs a key this application
  does not have. `RatingStore` persists its own value to `~/.sac/ratings.json`. Nothing is
  ever sent to Steam. Do not relabel this as "Steam rating" in the UI — it would be a lie
  to the user. Caveat: the whole file is rewritten on each change, so two library windows
  running at once will clobber each other's ratings. Acceptable for a single-instance
  desktop tool; worth knowing if that ever changes. Ratings used to live under
  `%LOCALAPPDATA%\SteamAchievementManager\`. `RatingStore.Migrate` moves that file across on
  first run and is the only thing that looks there; if the move fails it reads them where
  they are and writes to the new path next time, so a locked or read-only profile loses
  nothing. Keep it until it is safe to assume nobody is upgrading from a pre-home-directory
  build.

Two things to keep in mind when touching this:

- The file names use the **32-bit account id** (`steamId & 0xFFFFFFFF`), not the 64-bit
  SteamID.
- Coverage is partial. Steam only writes these caches for games it has actually fetched or
  run, so `TryGet` returns `null` for the rest and the UI shows `—`. **Unknown and zero are
  different answers** — do not collapse them.

All of this was validated against the live API (`RequestUserStats` +
`GetAchievementAndUnlockTime`) for several games and matched exactly, including
Civilization V at 64/286. If you change the parsing, re-validate the same way rather than
eyeballing it; a plausible-looking wrong number here is worse than no number.

### The cache (`~/.sac`)

Neither window starts empty. One SQLite database and one icon directory serve both windows;
both default under `~/.sac` and are relocatable from the Settings dialog.

**The directory moved from `~/.sam` to `~/.sac` in 1.0, and `AppSettings.EnsureMigrated`
is what makes that survivable.** The whole directory is moved across the first time, so an
existing Steam Achievement Manager install keeps its caches and — the part that cannot be
rebuilt — the user's own like/dislike ratings. `RepointMovedPaths` then rewrites any cache
location in `settings.json` that pointed *inside* the old directory, because those are
stored as absolute paths; a location the user pointed elsewhere is left alone.

Two things about it are easy to get wrong:

- **It is called from both `AppSettings.Load` and `RatingStore.Load`, not from one place.**
  `RatingStore.Load` is a *field initialiser* in `GamePickerViewModel`, so it runs before
  the constructor body that calls `AppSettings.Load`. Relying on that order would work
  today and break on the next reshuffle. Re-running it costs two directory probes.
- **It must stay a no-op when `~/.sac` already exists**, even if `~/.sam` also does. A user
  can have both, and merging them would be guessing about which copy is current.

| Store | Owner | Holds |
|---|---|---|
| `games` table | `Library/GameCache.cs` | the owned game list and its stats |
| `achievements` table | `Achievements/AchievementCache.cs` | one game's achievement list and its unlock state |
| `<appid>.img` | `Common/IconCache.cs` | capsule art, one file per app id |
| `<appid>_<icon>` | `Common/IconCache.cs` | achievement icons, one file per app id and icon name |

Two processes hold the same database file open at once — the library window and every
editor window it launched. That works because the schema is created with
`journal_mode=WAL` — concurrent readers alongside one writer — and `AchievementCache` sets
`Default Timeout` so the moment they overlap is a wait rather than a `SQLITE_BUSY`. Do not
"simplify" either by dropping WAL. Merging the two executables did **not** remove this:
they are still separate processes.

**The achievement icon naming is load-bearing.** `IconCache.Prune` walks the whole
directory and deletes what belongs to a game that is no longer owned, recovering the app id
from the file name: the whole stem for a capsule, the part before the first underscore for
an achievement icon. Name an achievement icon anything else and the next library refresh
silently eats it. Files whose name yields no app id are left alone entirely — the directory
is user-chosen and may not be ours.

Startup order matters and is deliberate:

1. `LoadFromCache()` runs **synchronously** in the constructor. It is pure database reads,
   so the window is painted with games before any network or Steam work starts.
2. `HydrateCachedIconsAsync()` decodes the on-disk capsules on a background thread.
3. `RefreshLibraryAsync()` re-downloads the published list, re-checks ownership, refreshes
   stats, and then `SaveCacheAsync()` writes it back.

Do not turn step 1 into an `async` call — the whole point is that it completes before the
first frame.

`LoadGamesAsync` **merges** into `_Games` rather than clearing and rebuilding. The cached
entries already on screen carry decoded bitmaps and the user's own rating; replacing them
wholesale blanks the window and re-downloads every capsule. `GameCache.Sync` is the
counterpart on the database side: one transaction that upserts what is owned and deletes
what is not, so a crash mid-sync cannot leave a half-written library.

Two details worth keeping:

- **Dispose the cache before moving it.** SQLite's WAL leaves `-wal` and `-shm` sidecars
  open; `GameCache.MoveTo` closes the connection, calls `SqliteConnection.ClearAllPools()`,
  moves all three files, and reopens. Skipping the pool clear leaves the file locked on
  Windows.
- **`SQLitePCLRaw.bundle_e_sqlite3` is pinned to 2.1.13.** `Microsoft.Data.Sqlite` 10.0.1
  otherwise resolves 2.1.11, which carries GHSA-2m69-gcr7-jv3q. Do not drop the pin to tidy
  the file.

Settings live at a fixed `~/.sac/settings.json` — they cannot live under the configurable
database directory, because that is the path they would have to be read to find. The file
is only written once something changes; its absence means defaults. `~/.sac/ratings.json`
sits beside it for the same reason: it is per-user data, and it must not move when the
cache directory does.

So `~/.sac` holds, by default, `settings.json`, `ratings.json`, `games.db` (plus its WAL
sidecars) and `icons/`. Only the last two are relocatable; the first two are found by
fixed path or nothing could be found at all.

`settings.json` also carries `Theme`, serialized **by name** so the file stays readable and
reordering `AppTheme` cannot silently change what an existing file means. An absent value
is `System`, which is what the application did before the theme was configurable.

### The Settings dialog

`SettingsViewModel` collects the three values and `GamePickerViewModel.ApplySettingsAsync`
commits them. Two things there are deliberate:

- **The theme is previewed live and committed separately from the paths.** A theme is a
  thing you judge by looking at it, so `SettingsWindow` applies each selection to
  `Application.Current.RequestedThemeVariant` immediately and puts the original back if the
  dialog is cancelled. `ApplySettingsAsync` therefore only has to *persist* it — and it
  persists it even when the cache move failed, because the two are independent and a failed
  move must not leave a theme that is already on screen unsaved.
- **`SelectedTheme` is written out by hand, not generated.** Its setter raises the preview
  event, and the constructor assigns the backing field directly: loading the stored theme is
  not a change the user made and must not fire a preview.

The theme control is a `ComboBox` over `ThemeOption` objects rather than `RadioButton`s,
because a `RadioButton` is a `ToggleButton` — see the rule above about not binding
`IsChecked` to view-model state. `ThemeOption` exists only because a compiled binding cannot
call `AppThemes.GetDisplayName` for the enum member.

### The achievement cache (the editor)

The editor used to show an empty window until Steam answered `RequestUserStats`, then
download every achievement icon from the CDN one at a time. Both now have a warm path.

Constructor order is deliberate and mirrors the library window:

1. `RefreshStats()` asks Steam. It only *asks* — the answer arrives later, on the callback
   timer.
2. `LoadFromCache()` then runs **synchronously**, painting the achievement list from the
   `achievements` table. It has to come after `RefreshStats()`, which clears the
   collections.
3. `OnUserStatsReceived` rebuilds the list from live data and calls `SaveCache()`.

`IsInputEnabled` stays false across that window, so nothing cached can be committed to
Steam. The cached `IsAchieved` is display only; `OriginalValue` is overwritten from Steam
before Commit is ever reachable.

Three things that are easy to get wrong here:

- **`SaveCache` builds its rows from `_AchievementDefinitions`, not from `Achievements`.**
  The latter is whatever the search box and the locked/unlocked toggles last left on
  screen. Persisting it would write a filtered subset over the whole list.
- **Rows are keyed by account id *and* language.** Achievement state belongs to whoever was
  logged in, and the cached name and description are the *localized* strings — so
  `_Language` is read once in the constructor and `LoadUserGameStatsSchema` uses that same
  field rather than calling `GetCurrentGameLanguage()` again. A language switch must miss
  the cache, not show the previous language's text.
- **Writes are chained through `_SaveTask`, not fired off with `Task.Run`.** A
  `SqliteConnection` cannot serve two commands at once, and pressing Refresh twice produces
  two callbacks.

Only the achievement list is cached. Stat definitions still come from the schema file on
every start; it is a local file and the parse is cheap.

### Pending changes (the editor)

`_PendingStates` holds the user's uncommitted toggles by achievement id, and it is the
model — `Achievements` is only what the filters last left on screen. This is not
bookkeeping for the Uncommitted bar; it fixes two real bugs the bar would otherwise have
made obvious:

- Changing a filter rebuilt the list from Steam and **silently discarded every pending
  toggle**.
- `StoreAchievements` read the changed rows off `Achievements`, so a change made before
  switching filters **was never committed**.

Three rules keep it honest:

- `_SteamStates` caches what Steam last said, read once per callback by
  `RefreshSteamStates`. The list can then be rebuilt for a filter or search change without
  asking Steam again, and `EffectiveState` is "the override if there is one, else Steam's".
- A toggle back to Steam's value **removes** the entry rather than storing a no-op, so the
  count is the number of real changes.
- `RefreshSteamStates` clears pending. Anything toggled while the *cached* list was on
  screen was toggled against unconfirmed state, so it is dropped rather than replayed onto
  the real values.

Bulk operations set `_IsBulkUpdating` so the summary is recomputed once rather than once
per achievement — on Civilization V that is the difference between 10 and 2,860 property
notifications.

The filter is one `AchievementFilter` enum, replacing a pair of independent
"show only locked" / "show only unlocked" booleans that could be set to two different
combinations meaning the same thing. It filters on the state **on screen**, pending edits
included, so an achievement does not vanish from Locked the instant it is ticked.

### The stats schema

`ManagerViewModel.LoadUserGameStatsSchema` parses
`<SteamInstall>/appcache/stats/UserGameStatsSchema_<appid>.bin`, a Valve binary KeyValues
blob, with the hand-written parser in `AchievementCaretaker.SteamApi/KeyValue.cs`. It
handles **two schema shapes**: a newer one where `stat.type` is a string enum name, and an
older one where `stat.type_int` (or `type`) is an integer. Keep both paths — upstream
commits in SAM (`4f2b037`, `de8b710`) exist specifically to fix regressions here.

### Threading

Downloads are `async`/`HttpClient` on the UI thread's synchronization context, so
continuations come back on the UI thread and no marshalling is needed — the
`BackgroundWorker` + `Invoke` dance the WinForms build required is gone.

The one deliberate exception is the ownership sweep in
`GamePickerViewModel.LoadGamesAsync`, which is wrapped in `Task.Run`. It makes two Steam
calls per app id across the whole published game list, so it cannot run on the UI thread.
SAM's WinForms build ran it on a `BackgroundWorker` for exactly the same reason. Note this
means **Steam client calls do happen off the UI thread there** — that is pre-existing
behaviour, preserved deliberately, not an invitation to add more.

Logo and icon downloads use a single-consumer queue (`ConcurrentQueue` + `SemaphoreSlim`),
one download at a time, matching the old single-`BackgroundWorker` behaviour.

## Conventions

Match the surrounding style; it is consistent and deliberate even where it is unfashionable.

- **Every source file opens with a header, and which one depends on what the file derives
  from.** Do not copy a header from a neighbouring file without checking.
  - *Derived from SAM* (everything in `AchievementCaretaker.SteamApi`, and the Steam half of
    the application that came out of the conversion): the Caretaker copyright, the "based on
    Steam Achievement Manager" attribution with the upstream URL, the "altered source
    version" marking, and the full zlib notice. A licence condition, not a style rule.
  - *Based on GOG Achievements Manager* (`Gog/`): the Caretaker copyright, the "based on GOG
    Achievements Manager by Timendum" attribution with both upstream URLs, and a note that it
    reimplements the calls it makes: listing, and unlocking and re-locking.
  - *Everything else* (`Epic/`, the shared store code, the store windows and view models,
    `tools/`, CI and packaging): the Caretaker copyright, `See LICENSE in the repository
    root.`, and for `Epic/` a line saying it is written from the endpoints Legendary and
    Heroic document. No SAM claim: it is not derived from SAM.
  - `.axaml` files carry the short XML comment form; scripts and workflows the `#` form.
- Explicit `this.` on instance members.
- Explicit comparisons: `if (x == false)`, `if (s == true)` — not `if (!x)`.
- Private fields are `_PascalCase`.
- Target-typed `new()` is used freely (`List<T> foo = new();`).
- `#region` blocks wrap each native method group in the wrappers.
- `_($"...")` (`InvariantShorthand.cs`) is the invariant-culture interpolation shorthand.
- Suppressions live in per-project `GlobalSuppressions.cs`, not inline.
- All code, comments and documentation are in English.

`LangVersion` is `latest` in both projects.

One trap: `using static InvariantShorthand` puts a method named `_` in scope, so `_` is
**not** available as a discard in files that import it. Name the variable instead.

View models use CommunityToolkit.Mvvm. `[ObservableProperty]` on a `_PascalCase` field
generates the matching `PascalCase` property, so the source generator and the existing
field-naming convention agree. Properties whose setter needs to trigger a re-filter are
written out by hand rather than generated.

## .NET Core semantics that bit this codebase

The `net48` → `net10.0` migration changed four behaviours silently — no compiler error,
just different runtime results. They are fixed; the notes are here so they are not
reintroduced.

- **`Application.StartupPath` now ends with a directory separator** (verified on .NET 10);
  on .NET Framework it did not. SAM's `Program.cs` compared it directly against the
  registry's `InstallPath` to refuse to run from the Steam directory, so that guard
  silently became dead. Replaced with `IsRunningFromSteamDirectory()`, which normalises
  both sides through `Path.GetFullPath` + `Path.TrimEndingDirectorySeparator` and compares
  `OrdinalIgnoreCase`. Do not reintroduce a raw `==` on paths.
- **`Process.Start` defaults to `UseShellExecute=false`** on .NET Core, so a bare
  `"SAM.Picker.exe"` resolved against the *current working directory*, not the app
  directory. `Program.LaunchForApp` uses `Environment.ProcessPath`, falling back to
  `AppContext.BaseDirectory`.
- **`IntPtr.ToInt32()` overflows on 64-bit pointers.** `NativeWrapper.ToString()` used it;
  harmless while everything was 32-bit, live now that `AnyCPU` means x64. Uses `ToInt64()`.
- **High-DPI settings moved out of the manifest.** `dpiAware` in `app.manifest` warns
  (`WFO0003`); the setting lives in
  `<ApplicationHighDpiMode>SystemAware</ApplicationHighDpiMode>`. `SystemAware` preserves
  the previous behaviour — do not "upgrade" it to `PerMonitorV2` without checking the
  fixed-size layouts.

The interop layer itself was **not** rewritten.
`[UnmanagedFunctionPointer(CallingConvention.ThisCall)]` plus `Delegate.DynamicInvoke` over
raw vtable slots still compiles and is still the mechanism — and it **works on .NET 10**,
verified against a live, logged-in Steam client on all three supported targets:

| Step | win-x64 | win-x86 | linux-x64 |
|---|---|---|---|
| Install-path discovery | OK | OK | OK (`~/.local/share/Steam`) |
| `Steam.Load()` + 3 exports | `steamclient64.dll` | `steamclient.dll` | `linux64/steamclient.so` |
| `CreateInterface` + vtable marshal | OK | OK | OK |
| `CreateSteamPipe()` / `ConnectToGlobalUser()` | OK | OK | OK |
| `GetAppData(480/105600, "name")` | `Spacewar` / `Terraria` | same | same |
| `IsSubscribedApp(480)` / language | True / `english` | same | same |
| `IsLoggedIn()` | True | True | True |
| `GetSteamId()` | 765611980067…409 | same | same (after invariant #4 fix) |
| `LibraryStats` playtime | 6614 / 1356 / 5847 min | identical | identical |
| The editor on Terraria | 137 achievements, 10 stats | identical | — |

Getting there required fixing the `GetISteamApps` signature (invariant #3) and the
`GetSteamID` struct-return ABI (invariant #4).

What remains unverified is the **write** path — `SetAchievement`, `SetStatValue`,
`StoreStats` and `ResetAllStats`. Those mutate a real Steam account, so they were left
alone deliberately; exercise them by hand on a throwaway title before trusting a release.

If the interop ever does misbehave, the modern replacement is C# function pointers
(`delegate* unmanaged[Thiscall]<...>`), which Microsoft documents as "more efficient,
easier to use correctly, and supported in all environments," and which also drops the
per-call boxing that `DynamicInvoke` costs.

Do not pursue trimming or NativeAOT: this much reflection-based marshalling is not a
candidate, and `Marshal.GetDelegateForFunctionPointer` is annotated `RequiresDynamicCode`.

## Known issues

A clean build is **0 warnings, 0 errors** on both `AnyCPU` and `x86`. Keep it that way.

- **`Wrappers/SteamClient018.cs`** — `GetSteamUtils004` requests `"SteamUtils004"` but
  still marshals the result as `ISteamUtils005`. Upstream SAM requests `"SteamUtils005"`.
  This bends invariant #2 and predates every migration here. It does not crash — the
  interface comes back non-null and `GetAppId()` returns — so it has been left alone, but
  it is still worth confirming as intentional.
- **The logo queue is eager.** SAM's WinForms build only downloaded capsule art for items
  whose `ListViewItem.Bounds` intersected the visible list, using WinForms' virtual mode.
  The Avalonia `ListBox` uses a `WrapPanel`, which does not virtualize, so every filtered
  game's logo is queued instead. That is bounded by games *owned*, not by the full
  published list, so it is a few hundred at worst — but it is more network traffic than
  before. Moving to `ItemsRepeater` + `UniformGridLayout` would restore virtualization and
  visible-only loading, at the cost of hand-rolled selection handling.
- **Write path is unverified** — see the interop table above.
- **Orphaned `achievements` rows are never pruned.** `GameCache.Sync` prunes the `games`
  table and `IconCache.Prune` drops the matching icons, including achievement icons, but
  nothing deletes achievement *rows* for a game that is no longer owned. They are a few
  hundred bytes each and a re-purchase makes them current again, so this is noted rather
  than fixed.
- **Moving the cache while an editor window is open.** `GameCache.MoveTo` and
  `IconCache.MoveTo` assume they are the only holder. An editor process keeps its own
  connection to the same database, so a move performed from the Settings dialog while one
  is open can fail on Windows.
- **The library toolbar wraps rather than clips.** It is a `WrapPanel`, not a
  `StackPanel`: when the view-mode control was added, a single fixed row ran off the right
  edge on a narrow window and the last button became unreachable. If you add more toolbar
  items, check the window at its `MinWidth`.
- **Licensing needs a decision.** The repository carries both `LICENSE` (MIT, for the new
  work) and `LICENSE.txt` (the original zlib notice, which the derived code is bound by).
  See `docs/ATTRIBUTION.md`.

## Packaging and CI

A `vX.Y.Z` tag releases two Windows zips and one `.deb`, all cross-built on Linux. There
are pipelines for **both** forges and they drive the same scripts:
`.github/workflows/{ci,release}.yml` attaches the artifacts to a GitHub release;
`.gitlab-ci.yml` uploads them to the generic package registry and links them from a GitLab
release. Delete whichever forge is not used. `packaging/` holds the shared half:

| File | Does |
|---|---|
| `packaging/version.sh` | prints `VERSION=…`: the tag version, or the project version with a `~dev` suffix off a tag. Reads GitLab's and GitHub's tag variables both |
| `packaging/check-version.sh` | fails unless the tag, both `.csproj` files, both window titles and the release-notes heading agree |
| `packaging/check-pe-arch.sh` | asserts a published Windows bundle is all one architecture |
| `packaging/windows/build-installer.sh` | drives NSIS to build the per-user Windows installer |
| `packaging/windows/installer.nsi` | the installer itself |
| `packaging/linux/build-deb.sh` | builds the `.deb` from a self-contained publish |
| `packaging/linux/control.in`, `*.desktop`, `copyright` | the package metadata |

Five things there are load-bearing:

- **Building 32-bit needs `-p:Platform=x86` as well as `-r win-x86`.** The runtime
  identifier alone resolves 32-bit native libraries but still emits a *64-bit* apphost, and
  that bundle dies inside SkiaSharp's type initializer with nothing printed anywhere. This
  was found by running the artifact the pipeline produced, not by reading it.
  `check-pe-arch.sh` runs on every Windows bundle so it cannot come back.

- **CI does not override the version from the tag; it refuses to release when they
  disagree.** Two window titles are literals that cannot be driven from the assembly
  version, so overriding would ship a binary whose window said something else.
- **Every artifact is self-contained.** A framework-dependent deb would need
  `Depends: dotnet-runtime-10.0`, which lives only in Microsoft's apt repository, so
  `apt install ./…deb` would fail on a stock Debian. The Windows builds were
  framework-dependent and 12 MB; they are self-contained and ~34 MB now so that one
  sentence covers every download. A portable build that first asks for a runtime is not
  portable, and the installer cannot fetch one.
- **The Windows installer is per-user and its uninstaller must never touch `~/.sac`.**
  It installs into `%LOCALAPPDATA%\Programs\`, so no administrator and no Program Files
  (x86) split; x64 and x86 share one location and one uninstall entry, and an install wipes
  the directory first so no stale assemblies survive. Removing the application is not a
  request to delete the user's ratings.
- **The scripts are invoked as `bash script.sh`**, never executed directly, so nothing
  depends on the executable bit surviving in git. **This applies inside the scripts too.**
  `version.sh` called `check-version.sh` directly, and the first real GitHub run died with
  `Permission denied` and exit 126: the repository is written from Windows, so a Linux
  checkout gets mode 644. It never reproduced locally, because Git Bash treats everything as
  executable. The `.sh` files are also marked `100755` in the index now, but the `bash`
  prefix is the part that has to hold.
- **Anything added to one pipeline belongs in `packaging/`, not in the YAML.** The two
  forges are meant to stay thin wrappers over the same scripts; logic that lives in only one
  of them is logic the other silently lacks.
- **`version.sh` is not piped into `tee`.** It was, and that masked its exit status behind
  `tee`'s, so a version mismatch would have released anyway.

Details, including what the deb contains and how to build it without tagging, are in
`docs/BUILD.md`.

## Documentation

| File | For |
|---|---|
| `README.md` | users — what it is, how to run it, where its files live |
| `docs/ARCHITECTURE.md` | a human reading the design; the condensed form of this file |
| `docs/BUILD.md` | building, publishing and packaging a release |
| `docs/RELEASE-NOTES.md` | per-version changes, including the SAM attribution |
| `docs/ATTRIBUTION.md` | the SAM lineage and the licence position |

Each of them says what the project is based on: Steam Achievement Manager for the Steam
support, GOG Achievements Manager for GOG. Keep it that way when editing them.

## Skills index

Prefer retrieval-led reasoning over recall for .NET work here: consult the skill by name
before implementing, then make the smallest change that fits the surrounding style.

**Routing**

- Interop, vtables, marshalling, calling conventions → `dotnet-skills:ilspy-decompile`
  (to inspect what Steam or a .NET assembly actually does), plus the Microsoft Learn
  native-interop docs. This is the core of the codebase; verify against docs, never guess.
- C# style and language level → `dotnet-skills:csharp-coding-standards`
- Threading, async download queues, UI-thread marshalling, callback pumping →
  `dotnet-skills:csharp-concurrency-patterns`
- Avalonia, XAML, MVVM, compiled bindings, `DataGrid`, styling → no packaged skill covers
  this; use the Avalonia docs at <https://docs.avaloniaui.net>. Do not reach for the
  WinForms or WPF skills, and do not assume WPF XAML translates one-to-one — Avalonia's
  styling system is selector-based, not `Trigger`-based.
- TFM changes, `Directory.Build.props`, `global.json`, `.slnx`, version management →
  `dotnet-skills:project-structure`
- Adding any package, or introducing Central Package Management →
  `dotnet-skills:package-management` (use `dotnet add`/`remove`, never hand-edit the XML)
- Public surface of `AchievementCaretaker.SteamApi` if it is ever versioned →
  `dotnet-skills:csharp-api-design`
- Enabling nullable reference types → `dotnet-skills:csharp-nullable-reference-types`
- Struct layout and allocation in the hot marshalling path →
  `dotnet-skills:csharp-type-design-performance`
- Replacing the KeyValues parser or adding any serialization →
  `dotnet-skills:serialization`

**Quality gates**

- `dotnet-skills:slopwatch` — after substantial new or refactored code, to catch
  suppressed warnings, disabled checks, and empty catch blocks. Note this repo already has
  a legitimate `GlobalSuppressions.cs` convention; judge additions against it.
- `dotnet-skills:crap-analysis` — only meaningful once tests exist. There are none today.

**Specialist agents**

- `dotnet-skills:dotnet-concurrency-specialist` — race conditions in the download workers
  or callback pump.
- `dotnet-skills:dotnet-performance-analyst` — if the `DynamicInvoke` path is ever profiled.
- `voltagent-lang:dotnet-core-expert` — .NET 10 runtime, SDK, publish and deployment questions.

**Not applicable here** — this project has no web, database server, DI container,
configuration system, cloud, or container surface. Skip the Aspire, EF Core, ASP.NET, Akka,
Blazor, TestContainers, OpenTelemetry, and email skills. `playwright-blazor` in particular
is not a way to test this UI — it is a desktop app, not a web one.

## Finding code: graphify and CodeGraph first

**Search through the two graphs before reading or parsing any file.** Grep, `Read`, and
ad-hoc scripts over the source come after them, not instead of them — and only for what the
graphs could not answer.

1. **graphify** — the knowledge graph in `graphify-out/graph.json`. Use it to orient: what
   an area is, how two things connect, what depends on something.

   ```bash
   graphify query "<question>"      # traversal of the graph for a question
   graphify explain "<symbol>"      # a node and its neighbours
   graphify path "<A>" "<B>"        # how two things connect
   graphify affected "<symbol>"     # what a change to it reaches
   ```

   If `graphify-out/` does not exist or is stale, build it with `graphify update .` (code
   only, no LLM needed).

2. **CodeGraph** — the symbol index in `.codegraph/`. Use it to get the actual source.

   ```bash
   codegraph explore "<symbol names or question>"
   ```

   The MCP tool `codegraph_explore` does the same in one call and returns verbatim
   line-numbered source plus call paths and blast radius, safe to edit from. It is the
   cheapest way to answer "what calls this" — which matters here, because a change to one
   vtable struct can reach every wrapper. Re-run `codegraph init` after large refactors to
   refresh the index.

3. **Only then** read or grep files directly: for what neither graph covers, or to confirm
   a detail the graphs summarised.
