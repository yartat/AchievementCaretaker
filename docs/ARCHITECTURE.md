# Architecture

> Steam support in Achievement Caretaker is based on
> [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by Rick
> (gibbed), and is an altered source version of it; GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum;
> Ubisoft Connect support is based on [UbiSlot](https://github.com/BalTor02/UbiSlot) by BalTor02, [Achievements](https://github.com/PSerban93/Achievements) by PSerban93, [galaxy-integration-uplay](https://github.com/melcom-creations/galaxy-integration-uplay) by melcom and [UplayKit](https://github.com/UplayDB/UplayKit) by UplayDB (all MIT).
> The Steam interop layer described below is substantially SAM's work. See
> [ATTRIBUTION.md](ATTRIBUTION.md).

This document describes how the application is put together and why. It is the condensed,
human-readable companion to [`CLAUDE.md`](../CLAUDE.md), which carries the same material at
greater length plus the rules a contributor or coding agent must not break.

## The projects

| Project | Output | Role |
|---|---|---|
| `AchievementCaretaker.SteamApi` | class library | All native interop with Steam. No package references of any kind, and no UI dependency. |
| `AchievementCaretaker.Providers.Abstractions` | class library | `IStoreProvider` (a store's library and achievements), `IStoreAccount` (signing in and out), `IWritableStoreProvider` and the models. Interfaces only; no packages. |
| `AchievementCaretaker.Providers.Shared` | class library | What the providers share: the session file (DPAPI) and JSON helpers. |
| `AchievementCaretaker.Providers.Epic` | class library | The Epic Games provider over HTTPS, read-only. |
| `AchievementCaretaker.Providers.Gog` | class library | The GOG provider over HTTPS; it can also unlock and lock. |
| `AchievementCaretaker.Providers.Ubisoft` | class library | The Ubisoft Connect provider (Windows only): reads the files the launcher keeps on this computer, with pictures and achievement lists from Ubisoft's public CDN; an optional sign-in through Ubisoft's own page reads the account from Ubisoft and allows unlocking (never locking). |
| `AchievementCaretaker.Services` | class library | The logic behind the windows, with no UI: the SQLite caches and the store services (library, achievements, sign-in). |
| `AchievementCaretaker.App` | `AchievementCaretaker.exe` | The whole application: the library window, the achievements window for every source (Steam included), and the hidden Steam helper (`--steam-worker`). |

`tools/ProviderProbe` is a command-line probe of the providers. It is in the solution so it builds
with everything and is never published.

All of them target plain `net10.0` — not `net10.0-windows`. The interop is written against
`NativeLibrary` rather than `kernel32`, and the Windows registry lookup is guarded by
`OperatingSystem.IsWindows()`.

## One executable, and a helper

Steam's `ISteamUserStats` interface is **scoped to the single application ID the process was
initialised with**. A process that called `Initialize(0)` to enumerate the library cannot
then ask Steam for one game's achievements. That constraint, not packaging taste, is why
Steam Achievement Manager shipped two executables.

Caretaker ships one executable and keeps the constraint, by starting a hidden copy of itself
for one game at a time:

| Command line | What runs | `Client.Initialize` |
|---|---|---|
| *(no arguments)* | `GamePickerWindow` — the library, and every achievements window | app ID `0`, once Steam is there |
| `--steam-worker <appid>` | no window: `SteamWorkerEntry`, before Avalonia starts | that app ID |

`SteamProvider` (an `IStatsStoreProvider`) starts the helper through `SteamWorkerProcessLauncher`,
hands it a per-launch certificate and token on its standard input, and talks to it over gRPC on
`127.0.0.1` with TLS; the helper reads the achievements and statistics through `SteamApi.Client`,
sets and stores them, and exits when its standard input closes. The helper is kept for 30 s after
its last call so a read, the statistics and a commit start Steam once. The library is the helper's
too: started with app ID 0 it checks the published list against the account and reports the owned
games with everything Steam's client and cache files know (`ILibraryScanProvider`); the application
itself holds no Steam client. A game of another store
opens in the same window without a helper at all: its HTTPS service has no per-process scope.

## Project layout

```
AchievementCaretaker.App/
  Program.cs            entry point, command-line dispatch (the library, or the Steam helper)
  App.axaml[.cs]        styles, theme, window selection
  InvariantShorthand.cs the _($"…") invariant-culture helper
  Common/               AppSettings, IconCache, StoreImageCache
  Library/              GameInfo, LibraryStats, RatingStore, OwnRating,
                        StoreGameInfo
  Achievements/         AchievementFilter, StoreAchievementItem
    Stats/              the schema model
  ViewModels/           ProgressViewModel; the library, store and settings view models;
                        StoreLibraryViewModel, StoreAchievementsViewModel
  Views/                the library, achievements and settings windows; StoreLibraryView,
                        StoreAchievementsWindow, StoreSignInWindow; the hand-rolled
                        MessageWindow
  Resources/            Icons.axaml, Theme.axaml, the application icon

AchievementCaretaker.Services/
  GameRepository.cs, IGameRepository.cs, DatabaseMigrator.cs, DatabaseRelocator.cs, DatabaseState.cs,
  LibraryDatabaseExtensions.cs   the library database
  StoreLibraryService.cs, StoreAchievementsService.cs, StoreSessionService.cs   and their interfaces
AchievementCaretaker.Providers.Abstractions/
  IStore.cs, IStoreProvider.cs, IStoreAccount.cs, IWritableStoreProvider.cs, IStatsStoreProvider.cs, ProviderException.cs, Models/
AchievementCaretaker.Providers.Shared/
  DpapiSessionStore.cs, PlainSessionStore.cs   how a refresh token is kept on disk, per OS
  JsonHelpers.cs
AchievementCaretaker.Providers.Epic/   EpicProvider.cs with every unofficial Epic endpoint
AchievementCaretaker.Providers.Gog/    GogProvider.cs with every unofficial GOG endpoint

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
thread. The helper pumps its own: it requests the user's stats once and polls until the answer
arrives.

## The UI layer

`Program.Main` builds the Avalonia `AppBuilder`;
`App.OnFrameworkInitializationCompleted` chooses the main
window — the library, or a `MessageWindow` carrying the error. Avalonia has no
`MessageBox`, so `Views/MessageWindow.cs` is a hand-rolled stand-in that doubles as the main
window when the command line is wrong.

The library does not do the handshake at start. It has other sources than Steam, so it opens at
once, and `SteamConnector` (in the services layer) keeps asking in the background: when the machine
says Steam is running it starts a helper to name the logged-in account. Only when that works does
the Steam library refresh; until then the Steam source shows the games saved
last time under a "No active Steam" banner, with the reason.

It goes on watching after it connects. A pipe to a Steam that quit does not say so, but the
Steam process does, so the connector compares the process id taken at connection with the one
running now. When Steam goes, the window keeps the games on screen, shows the banner again, and the
search starts over; a refresh that was running when it happened discards its results rather than
treat "nothing owned" as the truth.

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

The library reads the setting once, at start.

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

## Pending changes in the achievements window

The uncommitted ticks live on the items themselves (`StoreAchievementItem.IsAchieved` against
`OriginalValue`; `StoreStatItem.Value` against the reported value); the visible collection is only
what the search box and filter last left on screen, so a filter change never loses a pending change.
A toggle back to the reported value is no change, so the count is the number of real changes.
Commit sends achievements and statistics together and always reads everything again afterwards,
because a commit that failed half-way has still changed some. Bulk buttons skip protected rows
(Steam's permission bits); only Steam has statistics, so only its window has the Statistics tab
and the reset (asked three times).

## Threading

Downloads are `async`/`HttpClient` on the UI thread's synchronization context, so
continuations come back on the UI thread and nothing needs marshalling.

The one deliberate exception is the ownership sweep in `GamePickerViewModel.LoadGamesAsync`,
wrapped in `Task.Run`: it makes two Steam calls per app ID across the whole published game
list, so it cannot run on the UI thread. **Steam client calls therefore do happen off the UI
thread there** — preserved deliberately from the original, not an invitation to add more.

Capsule art is not held by the view models: each tile loads its own when it comes into view and
lets it go when it leaves (`LazyImage`), reading the disk cache first and downloading only what is
missing. The stores' art is decoded no wider than 480 px. The tile views build controls only for the
tiles on or near the screen (`TileGrid`, an `ItemsRepeater` with a uniform grid layout), which took about
125 MB of control objects off a library of 1,400 games. The achievements window loads its icons
with four workers.

## Epic and GOG

The library window can show an Epic Games or GOG library beside Steam's: the owned games, and each
game's achievements with the signed-in player's progress. Epic is read-only. GOG's achievements can also be unlocked and re-locked: the
achievements window has the Steam editor's checkboxes, Lock / Invert / Unlock buttons and
Uncommitted bar, and Commit sends the changes through `IWritableStoreProvider`, which GOG and Ubisoft Connect (signed in, unlock only)
implements. How the two stores' endpoints work is in [EPIC-RESEARCH.md](EPIC-RESEARCH.md)
and [GOG-RESEARCH.md](GOG-RESEARCH.md); this is how the application uses them.

- **`AchievementCaretaker.Providers.*`** has one assembly per store, each implementing
  `IStoreProvider` from `Abstractions` and using `Shared` for the session file. They are plain
  HTTPS with no native code, and each asks an `IHttpClientFactory` for a client per request rather
  than keeping one (a kept client holds on to stale connections and DNS answers). Providers do not
  reference one another. They are registered in the
  application's composition root (`App/Services/ServiceRegistration.cs`) as singletons, and
  the library window asks the container for them. Steam is not behind that interface yet: its interface is
  native, scoped to one app id per process, and can write.
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

## Ubisoft Connect

Ubisoft Connect is a source like the others, but **found on this computer**: `UbisoftProvider` reads the
launcher's ownership cache (which products the account owns), its configuration catalog (what they are
called and which pictures and achievement list belong to them), its achievement archives (names,
descriptions, icons) and its spool (what was earned on this computer, and when). It is an
`IStoreProvider` and an `ILocalStoreAccount`, an `IStoreAccount` that is found rather than signed in
to: the launcher's user is the account, there is no sign-in panel, Switch account or Sign out, and when
the launcher is not there the window says so and offers **Look again**. It registers only on Windows,
in one place (`AddUbisoftProvider`), so elsewhere there is no such source.

The only network use is Ubisoft's public CDN (`static3.cdn.ubi.com`), which needs no sign-in: the
background picture of a game (the library's `ImageUrl`) and, for a game whose archive the launcher has
not saved, the archive itself, named by the catalog and kept in `~/.sac/ubisoft/archives`. The provider
takes an `IHttpClientFactory` and asks for its named client per request, like Epic and GOG. The library
listing downloads nothing; **Load progress** and opening a game do.

What the files hold is partial and is shown as such: a game is an owned product whose catalog block can
be started (`start_game`), named through the block's localization table, with the shipped name list as a
fallback; a game the catalog gives no list for has an empty one, a game the catalog does not know has an
error that says so, and "not earned" means "not earned here". Icons are taken out of the archives into
`~/.sac/ubisoft/icons` and shown from there (`file:` addresses in `StoreImageCache`). The formats and
what was found on a real machine are in [UBISOFT-RESEARCH.md](UBISOFT-RESEARCH.md).

### Signing in to Ubisoft (optional)

`UbisoftProvider` is an `ILocalStoreAccount` that can also be signed in to (`IsSignedInOnline`). The
sign-in is Ubisoft's own page (`connect.ubisoft.com/login`) in `StoreSignInWindow`, which runs the
account's `SignInScript` on that site's pages: the page leaves the session in its local storage, and the
script moves the page to `connect.ubisoft.com/change_domain/<session>`, the address
`GetSignInResultKind` recognises as `Address`. The window never reads the page's fields. The provider
checks the session with Ubisoft's session service (`UbisoftProvider.Online.cs`), keeps it through
`ISessionStore` (`~/.sac/ubisoft.json`, DPAPI) and renews it with its ticket, else with the remember-me
ticket. Both partial files of the provider hold Ubisoft's endpoints.

A signed-in account is read over Ubisoft's socket service (`DemuxClient`, `UbisoftOnline`): TLS to
`dmx.upc.ubisoft.com`, four-byte big-endian length-prefixed protocol buffers, written by hand by field number
(`ProtoMessage`) so there is no package. A connection announces the protocol version the server says is
current, signs in with the ticket, and opens `ownership_service` (the owned products, each with its catalog
block, which `GameConfigurations.Parse` reads exactly as it reads the launcher's) and `ach_frontend` (what the
account has earned, by product id). The names, pictures and achievement lists still come from the catalog
block and the public CDN. Calls take turns on one connection, which is opened again once if it has dropped and
is closed 20 seconds after its last use: Ubisoft lets an account be signed in to the service once at a time, so a
connection left open would turn the launcher away and be turned away itself. The ownership service closes
itself on any request before its list has been asked for, so that is always the first. The provider is
`IDisposable`, which the container honours.

A signed-in account can also **unlock**: `UbisoftProvider` is an `IWritableStoreProvider` with `CanWrite` true
only when signed in (the launcher's files are only read) and `CanRelock` false. `ach_frontend` takes a game's whole
list of earned achievements, so `SetAchievementsAsync` reads what is earned, sends it back with its times and the new
ones stamped now, and reads it again to see that the service kept them (it answers a write with an empty message
either way). An unlock cannot be undone, so the window shows no Lock or Invert, shows what is earned as fixed
(`StoreAchievement.Protected`), and asks once before sending (`CanRelock` on the achievements service, a
confirmation in `StoreAchievementsViewModel`).

## Services and tests

`AchievementCaretaker.Services` holds what used to sit inside the store view models and has no
Avalonia in it, so it is tested without a window. The view models keep what is on screen (games,
pending ticks, filters, images) and call the services for the rest:

- **`IGameRepository`** is the saved libraries, over the scoped `IDbConnection` of a window
  (Dapper builds the commands and reads the rows). The tables are made once at start by
  `IDatabaseMigrator`, and `IDatabaseRelocator` moves the file for the Settings dialog. A database
  that could not be prepared is a repository that reads empty and writes nothing, with `Error`
  saying why.
- **`IStoreLibraryService`**: the saved library of the signed-in account, re-reading the owned
  list, and the progress run (four workers, saved in batches, so a cancelled run keeps what it
  read).
- **`IStoreAchievementsService`**: reading a game's achievements and sending changes, with a
  failure put as a sentence for the user.
- **`IStoreSessionService`**: sign-in, sign-out and the wording of what the user is asked first.

Services are singletons registered in `App/Services/ServiceRegistration.cs`; the library window
resolves its view model from a scope. No static `HttpClient` is left: art, the published game
list and icons ask `IHttpClientFactory`. Tests are in `tests/` (xUnit v3), use the public
surface only and run with `dotnet test --solution AchievementCaretaker.slnx`. The view models
and windows themselves are not under test yet.

## What is not covered here

`CLAUDE.md` carries the longer form, including the reasoning behind decisions that look
arbitrary, the .NET Core migration traps, and the current list of known issues.
