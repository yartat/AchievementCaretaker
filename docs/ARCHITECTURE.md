# Architecture

> Steam support in Achievement Caretaker is based on
> [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by Rick
> (gibbed), and is an altered source version of it; GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.
> The Steam interop layer described below is substantially SAM's work. See
> [ATTRIBUTION.md](ATTRIBUTION.md).

This document describes how the application is put together and why. It is the condensed,
human-readable companion to [`CLAUDE.md`](../CLAUDE.md), which carries the same material at
greater length plus the rules a contributor or coding agent must not break.

## The projects

| Project | Output | Role |
|---|---|---|
| `AchievementCaretaker.SteamApi` | class library | All native interop with Steam. No package references of any kind, and no UI dependency. |
| `AchievementCaretaker.Providers` | class library | The libraries and achievements of other stores (Epic Games, GOG) over HTTPS, behind `IStoreProvider`. No native code, no packages, no UI dependency. Steam is not a provider; see [Epic and GOG](#epic-and-gog). |
| `AchievementCaretaker.App` | `AchievementCaretaker.exe` | The whole application: the library window, the achievement editor, and the store windows. |

`tools/ProviderProbe` is a command-line probe of the providers. It is in the solution so it builds
with everything and is never published.

All of them target plain `net10.0` — not `net10.0-windows`. The interop is written against
`NativeLibrary` rather than `kernel32`, and the Windows registry lookup is guarded by
`OperatingSystem.IsWindows()`.

## One executable, two windows

Steam's `ISteamUserStats` interface is **scoped to the single application ID the process was
initialised with**. A process that called `Initialize(0)` to enumerate the library cannot
then ask Steam for one game's achievements. That constraint, not packaging taste, is why
Steam Achievement Manager shipped two executables.

Caretaker ships one executable and keeps the constraint:

| Command line | Window | `Client.Initialize` |
|---|---|---|
| *(no arguments)* | `GamePickerWindow` — the library | app ID `0`, once Steam is there |
| `<appid>` | `ManagerWindow` — the achievement editor | that app ID |

`Program.TryParseAppId` reads the command line, `App.CreateMainWindow` picks the window
(doing the Steam handshake itself only for the editor), and `Program.LaunchForApp` starts a second copy of the same
executable (`Environment.ProcessPath`) when the user opens a Steam game. A game of another store
opens in a window of the library's own process instead: a store's HTTPS service has no per-process
scope, so the constraint above does not apply to it.

A consequence worth remembering: two processes hold the same SQLite database open. The
schema uses `journal_mode=WAL` and the achievement cache sets a command timeout so overlap
is a wait rather than a `SQLITE_BUSY`.

## Project layout

```
AchievementCaretaker.App/
  Program.cs            entry point, command-line dispatch, relaunch
  App.axaml[.cs]        styles, theme, window selection, the editor's Steam handshake
  InvariantShorthand.cs the _($"…") invariant-culture helper
  Common/               AppSettings, IconCache, StoreImageCache,
                        SteamConnector, SteamWatch
  Library/              GameInfo, GameCache, LibraryStats, RatingStore, OwnRating,
                        StoreGameInfo
  Achievements/         AchievementCache, AchievementFilter, StoreAchievementItem
    Stats/              the schema model
  ViewModels/           ProgressViewModel; the library, editor and settings view models;
                        StoreLibraryViewModel, StoreAchievementsViewModel
  Views/                the library, editor and settings windows; StoreLibraryView,
                        StoreAchievementsWindow, StoreSignInWindow; the hand-rolled
                        MessageWindow
  Resources/            Icons.axaml, Theme.axaml, the application icon

AchievementCaretaker.Providers/
  IStoreProvider.cs, IWritableStoreProvider.cs, StoreModels.cs
  Epic/, Gog/           one provider each, with every unofficial endpoint of its store
  StoreProviders.cs     the list of stores; adding one means listing it here
  SessionFile.cs        how a refresh token is kept on disk

AchievementCaretaker.SteamApi/
  Steam.cs              library load and export binding
  Client.cs             pipe, user, interface accessors, callback pump
  NativeWrapper.cs      vtable marshalling
  Interfaces/           the ISteam* vtable layouts
  Wrappers/             the managed faces of those interfaces
  Types/, Callbacks/    callback payloads
  KeyValue.cs, TextKeyValue.cs   Valve KeyValues parsers
```

## The interop layer

No Steamworks SDK is involved. The entire native surface is reimplemented:

1. `Steam.Load()` asks `SteamPlatform` for the Steam install path and the candidate client
   library paths for this OS and bitness, loads the first one that exists with
   `NativeLibrary.TryLoad`, and binds three exports: `CreateInterface`,
   `Steam_BGetCallback`, `Steam_FreeLastCallback`.
2. `Steam.CreateInterface<T>("SteamClient018")` returns a raw pointer to a C++ object.
3. `NativeWrapper<TFunctions>.SetupFunctions` treats that pointer as a one-field
   `NativeClass` and marshals its virtual table into a `struct` of `IntPtr` fields — one
   field per virtual method, in declaration order.
4. Calls go through `Call<TReturn, TDelegate>`, which turns the slot into a delegate with
   `Marshal.GetDelegateForFunctionPointer` (cached) and invokes it. Instance methods are
   declared `[UnmanagedFunctionPointer(CallingConvention.ThisCall)]` with the object pointer
   passed explicitly as the first argument.

### Four things that must not change

1. **Vtable field order is an ABI contract.** Every field is an `IntPtr`; the order is the
   only thing carrying meaning. Reordering silently calls the wrong function. Deprecated
   slots stay as `DEPRECATED_*` placeholders for this reason.
2. **Interface version strings must match the struct they are marshalled into.** The string
   passed to `CreateInterface` selects which vtable Steam hands back.
3. **Every vtable delegate needs `ThisCall` and an explicit `IntPtr self` parameter.**
   These are C++ member functions. On x86 a missing `this` went unnoticed for years because
   the remaining arguments still landed at the expected stack offsets; on x64 every argument
   shifts by one and Steam returns a null interface.
4. **A C++ method returning a struct by value needs a per-platform declaration.** MSVC
   returns it through a hidden pointer argument; System V AMD64 returns a small POD in RAX.
   `ISteamUser::GetSteamID()` is the one case today, and getting it wrong on Linux returns
   0 rather than throwing — which silently poisons everything keyed off the account ID.

### Callbacks

Steam answers by callback, not return value. `Client.RunCallbacks` drains
`Steam_BGetCallback` and dispatches by numeric ID to registered `ICallback` instances. Both
view models pump it from a 200 ms Avalonia `DispatcherTimer`, so callbacks arrive on the UI
thread. `RefreshStats` only *requests*; `OnUserStatsReceived` is where the editor actually
populates itself.

## The UI layer

`Program.Main` builds the Avalonia `AppBuilder`;
`App.OnFrameworkInitializationCompleted` chooses the main
window — the library, the editor, or a `MessageWindow` carrying the error. Avalonia has no
`MessageBox`, so `Views/MessageWindow.cs` is a hand-rolled stand-in that doubles as the main
window when the editor's handshake fails.

The library does not do the handshake at start. It has other sources than Steam, so it opens at
once, and `SteamConnector` keeps trying to create the Steam pipe in the background. Only when
that works does the Steam library refresh; until then the Steam source shows the games saved
last time under a "No active Steam" banner, with the reason.

It goes on watching after it connects. A pipe to a Steam that quit does not say so, but the
Steam process does, so the connector compares the process id taken at connection with the one
running now. When Steam goes, the window stops talking to it, keeps the games on screen, shows
the banner again, and the search starts over; a refresh that was running when it happened
discards its results rather than treat "nothing owned" on a dead pipe as the truth. The editor
locks itself when its Steam goes, since it can neither read nor commit without one.

**Only a finished synchronization may delete a game.** The ownership sweep asks Steam about
every published app ID, and once Steam is gone every remaining call answers "not owned", so a
sweep cut short looks exactly like an account that owns almost nothing. A refresh therefore
always adds and updates what it found, and removes games Steam no longer lists only if the sweep
and the statistics pass both ran to the end under the same Steam. Whether it is still the same
Steam is asked directly as each pass ends, because the watch only notices every few seconds. The
cache and the icon pruning follow the same rule.

View models hold all logic and every Steam call; views are XAML plus thin code-behind.
Because view models have no window to parent a dialog to, they raise `ErrorRaised`,
`MessageRaised` and `ConfirmRequested` events and the window shows the dialog.

Bindings are compiled, so binding typos are build errors. **Resource lookups are not.**
`{StaticResource Foo}` with no `Foo`, a `StyleInclude` pointing nowhere, or a property fed a
resource of the wrong type all compile cleanly and throw when the window opens — which is
why the headless render check described in [BUILD.md](BUILD.md#verifying-a-build) is worth
running after any XAML change.

Theming lives in `Resources/Theme.axaml` as a `ResourceDictionary` with `ThemeDictionaries`
for Light and Dark. Which one is used comes from `AppSettings.Theme` — *Follow system*,
*Light* or *Dark*, chosen in Settings and applied in
`App.OnFrameworkInitializationCompleted`. *Follow system* is `ThemeVariant.Default`, which
means Avalonia keeps tracking the OS setting rather than pinning a palette.

The variant can therefore change while a window is open, both from the OS and from the
Settings dialog, which previews a selection immediately and reverts it on Cancel. Every
themed brush must be referenced with `{DynamicResource}`, never `{StaticResource}`.

Each of the two processes reads the setting for itself, so a theme changed in Settings
reaches an achievement editor that is already open only when it is reopened.

Every icon is a `StreamGeometry` in `Resources/Icons.axaml` rendered through a `PathIcon`,
drawn on a shared 16×16 grid and sized by a single style. There are no icon bitmaps.

## Where the library numbers come from

The library window cannot ask Steam for another game's achievements — see
[One executable, two windows](#one-executable-two-windows). `Library/LibraryStats.cs`
therefore reads Steam's own on-disk caches:

| Number | Source |
|---|---|
| Total achievements | `appcache/stats/UserGameStatsSchema_<appid>.bin` |
| Earned achievements | `appcache/stats/UserGameStats_<accountid>_<appid>.bin` — a popcount of the `data` bitfield masked to the bits the schema defines |
| Playtime, last played | `userdata/<accountid>/config/localconfig.vdf` |
| Steam rating, release date | `SteamApps001.GetAppData` (`review_percentage`, `review_score`, `steam_release_date`) |
| Your like/dislike | `~/.sac/ratings.json` — **not** from Steam, and never sent to it |

Two things to keep in mind:

- The file names use the **32-bit account ID** (`steamId & 0xFFFFFFFF`), not the 64-bit
  SteamID.
- Coverage is partial: Steam only writes these caches for games it has actually fetched or
  run. **Unknown and zero are different answers** — a game with no cached schema shows no
  completion meter at all, rather than an empty one.

These numbers were validated against the live API (`RequestUserStats` +
`GetAchievementAndUnlockTime`) and matched exactly. If the parsing changes, re-validate the
same way; a plausible-looking wrong number here is worse than no number.

## Caching

Neither window starts empty.

| Store | Holds |
|---|---|
| `games` table | each Steam account's owned game list and its statistics, keyed by account and app ID |
| `store_games` table | each Epic or GOG account's library and achievement counts, keyed by provider, account and game |
| `meta` table | which Steam account the library window showed last, so a start without Steam shows it |
| `achievements` table | each visited Steam game's achievement list and unlock state, keyed by account ID **and** language |
| `<appid>.img` | capsule art |
| `<appid>_<icon>` | achievement icons |
| `store/<provider>/` | store art, named by a hash of the image address |

Every library is saved under its account, so signing out or switching account deletes nothing
and coming back finds the games and progress where they were. When Steam returns as a different
account, the window swaps to that account's saved library, and a refresh that began under the
previous one discards its findings instead of merging them. A database written before accounts
were recorded is rebuilt: its library tables could not say whose rows they held, and they only
ever held a cache.

The database and icon directory default under `~/.sac` and are relocatable from Settings;
`settings.json` and `ratings.json` are not, because `settings.json` is the file that would
have to be read to find them.

Startup order is deliberate in both windows: the cached read runs **synchronously** in the
constructor, so the window paints with content before any network or Steam work starts; the
background refresh then merges live data in rather than clearing and rebuilding, which would
blank the window and re-download every image.

The achievement icon file naming is load-bearing: `IconCache.Prune` recovers the app ID from
the file name to delete art for games no longer owned by **any** saved account, and files whose
name yields no app ID are left alone entirely, because the directory is user-chosen and may not
be ours. The `store/` subfolder is outside its reach on purpose, and nothing prunes it.

## Pending changes in the editor

`_PendingStates` — uncommitted toggles by achievement ID — is the model. The visible
`Achievements` collection is only what the search box and filter last left on screen.
Keeping the two separate fixes two real bugs: changing a filter used to discard every
pending toggle, and a change made before switching filters was never committed.

Three rules keep it honest: Steam's last-known states are cached so the list can be rebuilt
without asking Steam again; a toggle back to Steam's value removes the entry rather than
storing a no-op, so the count is the number of real changes; and re-reading Steam clears
pending, because anything toggled against the *cached* list was toggled against unconfirmed
state.

## Threading

Downloads are `async`/`HttpClient` on the UI thread's synchronization context, so
continuations come back on the UI thread and nothing needs marshalling.

The one deliberate exception is the ownership sweep in `GamePickerViewModel.LoadGamesAsync`,
wrapped in `Task.Run`: it makes two Steam calls per app ID across the whole published game
list, so it cannot run on the UI thread. **Steam client calls therefore do happen off the UI
thread there** — preserved deliberately from the original, not an invitation to add more.

Capsule art is read, decoded and downloaded by eight workers, off the UI thread, and the disk
cache is decoded in parallel at start so a warm library shows its art almost at once. The
editor's achievement icons are loaded the same way.

## Epic and GOG

The library window can show an Epic Games or GOG library beside Steam's: the owned games, and each
game's achievements with the signed-in player's progress. Epic is read-only. GOG's achievements can also be unlocked and re-locked: the
achievements window has the Steam editor's checkboxes, Lock / Invert / Unlock buttons and
Uncommitted bar, and Commit sends the changes through `IWritableStoreProvider`, which only GOG
implements. How the two stores' endpoints work is in [EPIC-RESEARCH.md](EPIC-RESEARCH.md)
and [GOG-RESEARCH.md](GOG-RESEARCH.md); this is how the application uses them.

- **`AchievementCaretaker.Providers`** has one `IStoreProvider` per store. It is plain HTTPS
  with no native code and no packages. Steam is deliberately not behind that interface: its
  interface is native, scoped to one app id per process, and can write.
- **One source at a time**, chosen in the rail. Steam's half of the window is unchanged. A store
  gets its own game type (`StoreGameInfo`), its own view model (`StoreLibraryViewModel`) and its
  own view (`StoreLibraryView`), because everything on Steam's side is keyed by a numeric app id
  and leans on playtime, review scores and demo/mod types that a store does not have.
- **Writing, GOG only.** Changing an achievement needs a token issued to the game's own client,
  so `GogProvider.SetAchievementsAsync` reads the game's client secret from GOGDB's build record,
  and only when something is written; listing reads the client id alone. The game token is used
  for the writes and dropped, and the account's own session is not touched. The window sends the
  pending changes four at a time, stops at the first failure, and always reads the achievements
  again afterwards, because a half-applied commit has still changed some. Epic cannot re-lock an
  achievement, so it does not implement the interface and its window has no checkboxes or buttons.
- **Sign-in shows the store's own page** in a browser inside the application
  (`StoreSignInWindow`, on `Avalonia.Controls.WebView`) and finishes by itself when the page
  reaches the store's end-of-sign-in address, which the provider recognises. The user's own browser
  and a pasted code remain as the fallback when the embedded browser cannot start. The session,
  which holds a refresh token, is kept in `~/.sac/epic.json` or `~/.sac/gog.json`.
- **The account is in the rail**, under its source, with a menu to switch account or sign out;
  both ask for confirmation. Switching signs in first and forgets the old account only once the
  new sign-in has succeeded.
- **Cached like Steam's, and per account**: the library in the `store_games` table of
  `games.db`, keyed by provider, account and game, painted at once on start and refreshed
  afterwards; images in a `store/` folder of the icon cache. Steam's `games` table is keyed by
  Steam account too. Signing out or switching account deletes nothing: each account's library
  and progress stay saved, and are shown again when that account is.
- **Achievement counts are read on request, and once at sign-in.** A game's achievements open
  in a window in the same process, which also fills in that game's meter in the library;
  **Load progress** does it for every game whose count is missing, a few at a time, and can be
  cancelled. Signing in starts that run by itself, so a new account fills in without a click and
  an account that is already known costs almost nothing. Until a game has been read its meter
  is absent rather than empty.

## What is not covered here

`CLAUDE.md` carries the longer form, including the reasoning behind decisions that look
arbitrary, the .NET Core migration traps, and the current list of known issues.
