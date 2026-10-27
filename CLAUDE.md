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

## Git

**Never commit, tag, push or open a pull request on your own initiative.** Finishing a change,
a fix or a release build is not permission to commit it. Wait for the user to ask for the
commit in that message. A request to "release" or to "send the PR" covers only the step it
names, and what it names is confirmed on the spot. Prepare commit text on request, and say
what would be committed, but do not run `git commit`, `git tag` or `git push` until told to.

## Projects

| Project | Output | Role |
|---|---|---|
| `AchievementCaretaker.SteamApi` | `AchievementCaretaker.SteamApi.dll` (class library) | All native interop. Loads `steamclient.dll`, resolves vtables, marshals calls, pumps callbacks. Also owns the Valve KeyValues parsers (`KeyValue` binary, `TextKeyValue` text). |
| `AchievementCaretaker.App` | `bin/AchievementCaretaker.exe` (WinExe) | The whole application: the library window, the achievements window (for every source, Steam included) and, started with `--steam-worker <appid>`, the hidden Steam helper, in one binary. |
| `AchievementCaretaker.Providers.Abstractions` | class library | `IStoreProvider` (a store's library and achievements), `IStoreAccount` (signing in and out, and who is signed in), `IStore` (the id and name both share), `IWritableStoreProvider`, `IStatsStoreProvider` (statistics and reset; only Steam), `ISessionStore` and the models (`StoreGame`, `StoreAchievement` with its `Protected` flag, `StoreStat`, `ProviderException`, `SignInResultKind`). Interfaces and models only: no packages, no code that runs. |
| `AchievementCaretaker.Providers.Shared` | class library | What the store providers share: the session stores (`DpapiSessionStore`, `PlainSessionStore`), the `AddSessionStore` registration and `JsonHelpers`, public, with the packages they need. |
| `AchievementCaretaker.Services` | class library | The logic behind the windows, with no UI and no Avalonia: the library database (`IGameRepository`/`GameRepository`, over a scoped `IDbConnection`, read and written with Dapper; `IDatabaseMigrator`, `IDatabaseRelocator`, `AddLibraryDatabase`) and the store services (`IStoreLibraryService`, `IStoreAchievementsService`, `IStoreSessionService`). |
| `AchievementCaretaker.Providers.Steam` | class library | Steam as an `IStatsStoreProvider` (`SteamProvider`; achievements, statistics, a single commit for both, reset): every call starts a hidden helper for one app id and talks to it over gRPC on 127.0.0.1 with TLS (a certificate made per launch, pinned by thumbprint) and a per-launch token, both handed over on the helper's standard input. Holds the `.proto`, the handshake, the process launcher and `LibraryStats` (Steam's on-disk caches). The achievements window uses it for Steam games, and the library window for the Steam library (`ILibraryScanProvider`). |
| `AchievementCaretaker.Providers.Steam.Worker` | class library | The helper's side: the Kestrel gRPC host, `SteamAppBackend` (schema + achievements + set/store through an `ISteamAppGateway`) and `SteamClientGateway` over `SteamApi.Client`. `AchievementCaretaker.exe --steam-worker <appid>` runs it (`Program.Main`, before Avalonia starts). |
| `AchievementCaretaker.Providers.Epic` | class library | The Epic Games provider, read-only. Every unofficial Epic endpoint is in its `EpicProvider.cs`. |
| `AchievementCaretaker.Providers.Gog` | class library | The GOG provider; it can also unlock and lock. Every unofficial GOG endpoint is in its `GogProvider.cs`. |

`AchievementCaretaker.slnx` is the solution, in the **`.slnx`** format. There is no
`.sln`; do not add one back. Its `Documentation` folder carries `README.md`, `CLAUDE.md`,
`LICENSE.txt` and everything under `docs/`.

### Store providers

The `AchievementCaretaker.Providers.*` assemblies read the library and achievements of other
stores, each behind `IStoreProvider`: `EpicProvider` and `GogProvider`, in their own assemblies,
with the interfaces in `Abstractions` and the shared plumbing in `Shared`. Providers do not
reference one another, and internals are not shared across assemblies (no `InternalsVisibleTo`):
whatever two providers need goes into `Shared` as public code. The providers are
registered in the application's composition root (`App/Services/ServiceRegistration.cs`), since
`Abstractions` cannot reference them. They and the
throwaway `tools/ProviderProbe` (listed in the `.slnx` so it builds with everything, never
published) are research, written up in `docs/EPIC-RESEARCH.md` and `docs/GOG-RESEARCH.md`.
The rules:

- **Steam is a provider for achievements, not a source in the rail.** `SteamProvider` is an
  `IStatsStoreProvider` (achievements, statistics, one commit for both, reset), but it is
  registered as itself, **not** as an `IStoreProvider`: everything that lists the stores asks for
  `IEnumerable<IStoreProvider>`, and Steam is the library itself, so it must not appear there.
  The **application holds no `SteamApi.Client` of its own**: everything that touches Steam goes
  through a hidden **helper** (this same executable with `--steam-worker <appid>`), because
  `ISteamUserStats` is scoped to the app id a process was initialised with. The library is
  the helper's too: `SteamProvider` is an `ILibraryScanProvider` (`ScanLibraryAsync` checks the
  published list of app ids against the account in a helper started with app id 0 and returns the
  owned games with their names, art, review scores, release dates, playtime and achievement counts;
  `FindOwnedAsync` is the single-game form behind the App ID box; `GetAccountAsync` names the
  logged-in account and is how the application learns Steam is there). `GetLibraryAsync` is
  `NotSupported` on purpose: it is not a listing the account can ask for. `ISteamLibraryService`
  (Services) downloads the published list and fronts those three calls; `SteamConnector` (Services)
  keeps asking every 3 s, after the machine has said Steam is installed and running
  (`ISteamEnvironment`, so no helper is started for nothing), and watches the Steam process id
  (`SteamWatch`) once connected. A scan reports `Complete` only if Steam was still the same Steam when
  it ended; **only a complete scan may delete a game**, see The UI layer. The helper serves gRPC on
  `127.0.0.1` over TLS.
  The helper's certificate and a token are made per launch and handed to it on standard input,
  never on the command line, in the environment or on disk; the application accepts only the
  certificate whose thumbprint it was told, and the helper refuses any call without the token.
  A helper is kept for 30 s after its last call so one editing session starts Steam once, and
  `GetAchievementsAsync` always starts a fresh one (a reload must see Steam's current state).
  Closing its standard input is what makes it exit, so a crashed application leaves none behind.
- **Epic is read-only; GOG can write.** Epic: no unlocking, no re-locking, no EOS SDK, no
  reading a game's client secret. Epic cannot re-lock an achievement anyway, and the tools that
  unlock inject into the game or need that secret. GOG: changing an achievement needs a token
  issued to the *game's* client, so `GogProvider.SetAchievementsAsync` reads the client secret
  from GOGDB's build record, **only when something is written**. Listing reads the client id
  and nothing else. The secret and the game token are never logged, kept on disk or put in an
  error message, and the account's own session is not touched by the game token. Writing is a
  separate interface, `IWritableStoreProvider`, which only GOG implements; the achievements
  window shows the checkboxes and the Lock / Invert / Unlock buttons only for a provider that
  has it, and sends the pending changes with Commit, as Steam's does. Verified against a
  live GOG account (unlock and re-lock), unlike Steam's write path.
- **Every unofficial endpoint lives in its store's one `*Provider.cs`.** They are the
  stores' own and can change; keep them in one file each.
- **A provider never holds an `HttpClient`.** It takes an `IHttpClientFactory` and asks for
  its named client (`EpicProvider.HttpClientName`, `GogProvider.HttpClientName`) **per request**.
  A client kept for the life of the process keeps its connections and the DNS answer it started
  with, so a changed address or a dropped connection is not noticed. The client's timeout and
  `User-Agent` are configured with the name, in the provider's own
  `Add<Store>Provider` extension (`EpicServiceCollectionExtensions`, `GogServiceCollectionExtensions`),
  which is also what registers the provider, as an `IStoreProvider` and as an `IStoreAccount`
  (see **Adding a store**). A provider owns nothing that
  needs disposing, so `IStoreProvider` is not `IDisposable`. The two provider assemblies
  reference `Microsoft.Extensions.Http` for this; `Abstractions` and `Shared` do not.
- **A session file holds a refresh token**, a credential for the whole store account:
  `~/.sac/epic.json`, `~/.sac/gog.json`. They go through `ISessionStore`, which a provider
  takes in its constructor. `DpapiSessionStore` encrypts them with DPAPI (`ProtectedData`,
  `CurrentUser` scope) on Windows; `PlainSessionStore` writes them user-only on Unix, where
  there is no BCL equivalent of DPAPI. **Which one is a single decision, made once in
  `AddSessionStore`**; nothing else asks which system it is on. Never log a session, print one
  or put one in an error message. Before 2.1.0 the file was plain JSON on Windows too, so
  `DpapiSessionStore.Load` still reads that (told apart by its first byte, `{`, not by catching
  a decryption failure, and with a byte-order mark skipped, which the JSON reader will not do)
  and writes it back encrypted: a sign-in made with an older version is kept.
  `ThrowIfEpicError` and `ThrowIfError` deliberately do not echo response bodies, and
  GOG's token request carries the code or refresh token in its URL, so that URL is never
  part of a message either.
- **Sign in with the store's own page.** Caretaker never reads a password and never reads
  another application's stored credentials. GOG Galaxy keeps its refresh token in the
  registry and the reference tools take it from there; this does not. The page is shown in a
  browser inside the application (`StoreSignInWindow`, the official `Avalonia.Controls.WebView`
  package: WebView2, WebKitGTK or WPE, WKWebView) so nothing has to be copied back: the window
  only watches the addresses the page moves through, and the provider says which one ends the
  sign-in (`IStoreAccount.GetSignInResultKind`: GOG's redirect carries `code=` in the address,
  Epic's redirect page carries the code in its text, read with `InvokeScript`). That browser runs
  in private mode where the platform has one (`EnvironmentRequested`), so closing it keeps
  nothing and a second account can sign in without being logged straight back into the first.
  **The user's own browser plus a pasted code stays as the fallback**, and is what is offered
  when the embedded browser does not start within a few seconds (no WebView2 runtime, no
  WebKitGTK): that failure reports nothing, the page just never loads. The WPE backend has no
  private-mode switch here, so on Linux with WPE a previous login may persist. The embedded
  sign-in was verified by the project owner against live Epic and GOG accounts (on Windows).
  Not exercised: Linux and macOS, the fallback with a missing WebView2 runtime, and switching
  between two real accounts.
- **The account is always in the rail**, under its source: its name with a menu for
  **Switch account…** and **Sign out…**, or **Sign in**. Both ask first. Switching signs in
  *before* it lets go of anything, so backing out of the page leaves the old account
  untouched; only a successful sign-in replaces the session and the library on screen
  (`ClearLibrary`, as opposed to `ForgetAccount`, which also drops the session). **Neither
  deletes saved data**, see the next item.
- **Unknown is null, not zero.** `StoreAchievement.XP`, `Progress` and `RarityPercent` are
  nullable because a store may not have them: GOG has no XP and no progress.
- **A store has two roles, and they are two interfaces.** `IStoreProvider` is what the store
  has (`GetLibraryAsync`, `GetAchievementsAsync`); `IStoreAccount` is who is signed in to it
  (`SignInAsync`, `SignOut`, `EnsureSignedInAsync`, `IsSignedIn`, `AccountId`, `AccountName` and
  what the sign-in page needs). Both extend `IStore` (`Id`, `DisplayName`). Code asks for the
  role it uses and no more: the sign-in window and `IStoreSessionService` take an `IStoreAccount`,
  the achievements service and window an `IStoreProvider`. What needs both says so
  (`StoreLibraryViewModel` takes the two; `IStoreLibraryService.LoadProgressAsync` takes the
  provider to read from and the account to save under). Epic and GOG are one object in both roles,
  and the container hands out that one instance under both interfaces; a provider's calls throw
  `login_required` when the session is gone, they do not sign in. Steam is an `IStoreProvider`
  only: it has no sign-in of its own.
- **Adding a store** means implementing `IStoreProvider` and `IStoreAccount` and registering it in
  `ServiceRegistration.AddCaretaker` (with the `Add<Store>Provider` extension that goes with it)
  as both, the same instance. Everything that lists the stores asks the container for
  `IEnumerable<IStoreProvider>` and `IEnumerable<IStoreAccount>` and pairs them by `Id`
  (`GamePickerViewModel.CreateStoreLibraries` throws if a store has only one of the two), so
  nothing else has to change to see it.

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
- **`store_games` lives in the same database and the same `IGameRepository` as `games`.** There is
  no static `Open`: the connection is a **scoped `IDbConnection`** (a `SqliteConnection`, with the
  `IDbCommand` made from it), registered by `AddLibraryDatabase`, and the tables are made **once, at
  start**, by `IDatabaseMigrator` through that connection and command (`App` calls
  `MigrateLibraryDatabase()` right after building the container). `DatabaseState` is the one
  singleton: where the file is, and why it could not be prepared. A database that could not be is a
  repository whose `IsAvailable` is false: reads are empty, writes do nothing, `Error` says why.
  The Steam library saves from a pool thread while the stores read and write from the UI thread,
  and they share their scope's connection, so every public repository member (and the relocator)
  locks on the connection. Because the repository is scoped, so is `IStoreLibraryService`, and the
  view model that uses them.
- **Store images live in `<icon cache>/store/<provider>/`, named by a hash of the URL**
  (`StoreImageCache`). The subfolder is what keeps `IconCache.Prune`, which parses app ids out
  of file names and only walks the top level, away from them. `IconCache.MoveTo` moves the
  subfolder along with the files; keep it doing that. Nothing prunes these images.
- **A game's achievements open in a window of this process** (`StoreAchievementsWindow`), for
  Steam too: the helper, not a second copy of the application with a window, is what Steam's
  one-process-per-app-id rule costs now. A provider that is an `IStatsStoreProvider` also gets a
  Statistics tab (editable values checked by `StatValueParser`, a reset with three confirmations)
  and one Commit that sends achievements and statistics together. Protected achievements and
  statistics are shown, tinted, and cannot be changed.
- **Progress is read on request, and once when an account signs in.** A library listing
  carries no achievement counts, and reading them costs a request or more per game (GOG's two
  more to GOGDB, which is run by volunteers). A game's counts are known once its window has
  been opened or **Load progress** has covered it; until then the meter is absent, not empty.
  Unknown is `null`, never 0. The one automatic run is a **sign-in** (the browser sign-in, the
  switch and the pasted code all end in `CompleteSignInAsync`), which starts **Load progress**
  by itself after the library has loaded. It reads only the games whose count is not saved,
  so signing back in to a known account is nearly free, and a new account on a large library
  is hundreds of requests (about 1,200 on Epic, 600 on GOG at last count): it shows in the
  status bar, can be cancelled, and keeps what it has. A start with a session already saved
  never starts one. Do not add more triggers without the same thought for that cost.
- **Every library is saved under its account**, so signing out or switching deletes nothing
  and coming back to an account finds its games and progress as they were. `store_games` is
  keyed `(provider, account, id)`, from `IStoreAccount.AccountId` (GOG's user id, Epic's
  account id; null when signed out, and then there is no library to show). Steam's `games`
  is keyed `(account, id)` by the 32-bit Steam account id, and `meta` remembers the account
  last shown, so a start without Steam shows that account's library. When Steam comes back
  as a different account, `UseSteamAccount` swaps what is on screen for that account's saved
  library, and a refresh that began under another account throws its findings away rather
  than merge them into this one. Progress belongs to the account it was read for and is
  never shown to another. `IconCache.Prune` keeps art for the union of every saved account's
  games (`IGameRepository.LoadAllGameIds`), not just the current one's. A dead session is still
  forgotten, or the sign-in panel would never come back; `EpicProvider` and `GogProvider`
  both report a refused refresh as `login_required` for that. A database from before accounts
  were recorded has no way to say whose rows its library tables hold, so `DatabaseMigrator`
  drops `games` and `store_games` when they lack the `account` column and rebuilds them:
  they were only ever a cache, and nothing else is migrated.
- **An element's own bindings read from its own `DataContext`.** The store view is hosted
  with `DataContext="{Binding SelectedStoreLibrary}"`, so its visibility is on a wrapper
  `Panel`, not on the view itself, or `IsVisible="{Binding IsStoreSelected}"` is looked up on
  the store.

### One executable: the library, and a helper

SAM shipped `SAM.Picker.exe` and `SAM.Game.exe` as separate programs. Caretaker ships one
`AchievementCaretaker.exe`, and the rule that made SAM need two still stands:

> **`ISteamUserStats` is scoped to the one app id the process was initialised with.**

A process that called `Initialize(0)` to enumerate the library cannot then ask Steam for one
game's achievements. So the executable runs in one of two modes:

| Command line | What runs | Steam initialised with |
|---|---|---|
| *(none)* | `GamePickerWindow`, the library, and every achievements window | app id `0`, once Steam is there |
| `--steam-worker <appid>` | no window: `SteamWorkerEntry`, called from `Program.Main` before Avalonia starts | that app id |

The helper is started by `SteamWorkerProcessLauncher` from `Environment.ProcessPath`. An app id
on its own on the command line (which used to open the editor) is accepted and ignored.
Do not call `RequestUserStats` in a loop from the library process: that is a dead end, not an
optimisation problem.

The executable refuses to run from the Steam install directory
(`Program.IsRunningFromSteamDirectory`).

### Layout inside `AchievementCaretaker.App`

| Folder | Namespace | Holds |
|---|---|---|
| *(root)* | `AchievementCaretaker.App` | `Program`, `App`, `InvariantShorthand`, `GlobalSuppressions`, `app.manifest` |
| `Common/` | `…App.Common` | `AppSettings`, `IconCache` — settings and the icon directory, used by both windows |
| `Library/` | `…App.Library` | `GameInfo`, `RatingStore`, `OwnRating` |
| `Achievements/` | `…App.Achievements` | `AchievementFilter`, `StoreAchievementItem`, `StoreStatItem` |
| `ViewModels/` | `…App.ViewModels` | `ProgressViewModel`, `GamePickerViewModel`, `StoreLibraryViewModel`, `StoreAchievementsViewModel`, `SettingsViewModel` |
| `Views/` | `…App.Views` | the windows, plus the hand-rolled `MessageWindow` |
| `Resources/` | — | `Icons.axaml`, `Theme.axaml`, `AchievementCaretaker.ico` |
| `Services/` | `…App.Services` | the composition root (`ServiceRegistration`), and the services that view models call |

These used to be three projects plus a `Shared/` folder linked into both executables with
`<Compile Include="..\Shared\*.cs">`. That machinery is gone: one assembly needs no links,
and `Icons.axaml` no longer has to be given a matching `avares` path in two places.

One C# rule this layout keeps tripping over: **a `using` directive imports a namespace's
types but not its nested namespaces.** Anything that wants to qualify by folder needs a
namespace alias (`using Foo = …App.Foo;`), not a plain using.

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
The Steam helper (`--steam-worker`) has been run against a live, logged-in Steam on Debian 13 (WSL2): reading and writing achievements and statistics, after the two Linux-only fixes described under invariant #5 and `SteamAppIdFile`.

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

Tests live under `tests/` and use **xUnit v3**, which runs on the Microsoft Testing Platform:
`global.json` opts in (`"test": { "runner": "Microsoft.Testing.Platform" }`), which the
.NET 10 SDK requires for `dotnet test`.

```bash
dotnet test --solution AchievementCaretaker.slnx -c Release
```

- **Tests use the public surface only. There is no `InternalsVisibleTo`.** If something that
  needs a test is internal, that is a reason to move it into a service with a public interface,
  not to open the assembly. This is the point of the service layer.
- `AchievementCaretaker.Services.Tests` covers the repository over a real SQLite file in a temporary
  folder, made the way the application makes it (`TestDatabase`: the real registration, the real
  migration, one scope), the migrator, the relocator, and the store services against a fake provider.
- `AchievementCaretaker.Providers.Steam.Tests` covers the helper over a real loopback TLS
  connection with a fake backend (token and certificate checks), the stdin protocol, the schema
  reader (both schema shapes) and the backend against a fake gateway. Not covered: `SteamClientGateway`
  and the process launcher, which need a live Steam and the built executable.
- `AchievementCaretaker.Providers.Tests` covers the session stores (including the plain file
  written by versions before 2.1.0, and a byte-order mark), `JsonHelpers`, what the provider
  registrations promise (the named clients, the sign-in end addresses, which store can write),
  and **the Epic and GOG providers against a played-out store** (`FakeStore`, an
  `IHttpClientFactory` answering from a table of routes; `MemorySessionStore`): sign-in, token
  refresh and rotation, the library with its paging and batching, achievements, and GOG's writes,
  including that no secret or code ends up in an error message.
- A test that cannot run on this operating system **skips**, it does not pass: the DPAPI tests
  skip off Windows and the Unix-file-mode tests skip on Windows, so CI on Linux is what runs the
  latter. Platform-annotated APIs in a test need `[SupportedOSPlatform]` or
  `[UnsupportedOSPlatform]` on the helper or test; the build treats warnings as errors.
- A new service or provider comes with its tests, in the same change.
- Pass `TestContext.Current.CancellationToken` to calls that take one (xUnit1051).

Not covered yet: the application's own code, which is mostly internal and waits for the service
layer, and anything behind `SteamApi.Client`, which needs a live Steam.

See also "Verifying without Steam" under [Running](#running) for what a unit test cannot reach.

## Running

The Steam features (the Steam library, the Steam achievements window) need the Steam client
installed, running, and logged in; the library window itself opens without it. On Windows the
install path
comes from `HKLM\Software\Valve\Steam\InstallPath`; elsewhere the candidate directories in
`SteamPlatform.GetUnixInstallCandidates` are walked. Without a live Steam process
`Client.Initialize` throws `ClientInitializeException`. You cannot meaningfully exercise
this code in CI or a sandbox — changes to interop must be tested by hand.

### Verifying without Steam

Plenty of this codebase *can* be exercised on a machine with no Steam at all, and should be
before anything is called done. The unit tests cover only public code; the application's own
internals and its windows are still checked with a throwaway console project that references
`AchievementCaretaker.App`. **A throwaway project's results prove nothing to anyone but the
session that ran it: if a check is worth running twice, it belongs in `tests/`.**

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
dictionary. Opening a Steam game with no Steam running shows an error instead of a window: the
helper cannot do anything without Steam.

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
   `Marshal.GetDelegateForFunctionPointer`s the slot (cached in `_functionCache`) and
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

### Five invariants you must not break

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
`RequestUserStats(steamId)` in the helper). `SteamUser012.GetSteamId` therefore branches on
`OperatingSystem.IsWindows()` and caches its delegate locally, because `NativeWrapper`'s
cache is keyed on the function pointer alone and two delegate types sharing one vtable slot
would collide.

An audit of the other `out`/`ref` delegates found no further cases: the parameters in
`CreateLocalUser`, `GetStat`, and `GetImageSize` are genuine C++ pointer arguments, not
hidden struct returns. If you add a wrapper for a method whose C++ signature returns a
`struct`/`class` by value, handle both ABIs and verify on both.

**5. Overloaded virtual functions are laid out in the compiler's order, and MSVC's is
reversed.** `ISteamUserStats` declares `GetStat(int*)` before `GetStat(float*)`, and `SetStat`
the same way. MSVC puts the overloads of one function in the vtable in *reverse* declaration
order; GCC and Clang put them in declaration order. The structs in `Interfaces/` are in the
Windows client's order (float, then integer), so on Linux and macOS the slot named `GetStatFloat`
holds the integer overload and the other way round. Nothing throws: reading an integer statistic
through the float slot just answers false, and the helper listed **no statistics at all** on
Linux until this was found (6 and 10 expected for Spacewar and Terraria, 0 returned).
`SteamPlatform.ReversesOverloadedSlots` is the one place that asks which system it is, and
`SteamUserStats013.Overload` picks the slot. Any new wrapper over an overloaded method (the
`GetUserStat`, `GetGlobalStat` and `GetAchievementProgressLimits` pairs are in the struct, unused)
must go through it, and be checked on both systems.

### Callbacks

Steam delivers results by callback, not return value. `Client.RunCallbacks(false)` drains
`Steam_BGetCallback` in a loop and dispatches by numeric `Id` to registered `ICallback`
instances. Only the helper has a client now, so only the helper pumps them (see below); the
application has no callback timer.

The helper tells Steam which app it is with a `steam_appid.txt` in a folder of its own that becomes its
working directory (`SteamAppIdFile`), **not** with the `SteamAppId` environment variable alone:
Windows honours the variable, Linux ignores it (`GetAppId` answers 0 and `Initialize` fails with
"appID mismatch"), and the file works on both, so it is written unconditionally.

The helper (`SteamClientGateway`) pumps callbacks itself: it requests the user's stats once, then
calls `RunCallbacks` every 50 ms until `UserStatsReceived` arrives (30 s limit). `RequestUserStats`
only *requests*; nothing can be read before the answer. All native calls take one lock, because
the gRPC server calls from pool threads.

### The UI layer

- `Program.Main` builds the `AppBuilder` and calls `StartWithClassicDesktopLifetime`.
- `App.OnFrameworkInitializationCompleted` *chooses* the main window: the library, or a
  `MessageWindow` carrying the error. This replaces the WinForms pattern of
  showing a `MessageBox` before `Application.Run`.
- **The library does not do the Steam handshake up front**: it has other sources than Steam,
  so it opens at once and `SteamConnector` (Services) keeps asking in the background, every 3 s.
  It asks the machine first (`ISteamEnvironment`: unsupported processor, not installed, no Steam
  process), and only when a Steam process exists starts a helper for `GetAccountAsync`, whose
  failure message is the reason shown. When that works, `GamePickerViewModel.ConnectSteam` shows
  that account's library and refreshes it. Until then the Steam source shows the cached games under
  a "No active Steam" banner with the reason, the refresh button is off, and what needs Steam
  (opening a game's achievements, the App ID box) says so rather than failing.
  **It keeps watching after it connects.** Whatever was learned stops being current when Steam
  quits and nothing says so, so `SteamWatch` compares the Steam process id taken at connection
  (`SteamPlatform.GetRunningClientProcessId`: the registry's `ActiveProcess\pid` on Windows,
  checked against the process list) with the one running now, every 3 s. On loss the view model
  turns refresh off and shows the banner again, and the connector starts looking again. **A refresh
  remembers `_steamGeneration` and throws its results away if it changed.**
  Not exercised against a real Steam restart; nor the Linux and macOS process lookup (macOS has no
  verified Steam at all).

  **Only a finished synchronization may delete a game.** The scan asks about every published app
  id, and once Steam is gone every remaining question answers "not owned", so a scan cut short looks
  exactly like an account that owns almost nothing (it once left 48 games of 600). The helper
  therefore says `Complete` only if the Steam process was still the same one when the last question
  was answered, and `LoadGamesAsync` always *adds* what it found (and a known name is never replaced
  by an empty one) but applies the details and removes the games Steam no longer lists only for a
  complete scan under the same `_steamGeneration`. The cache follows the same rule:
  `IGameRepository.Sync(account, rows, removeMissing: false)` and no `IconCache.Prune` unless the
  sync completed. A failed download of the published list changes nothing on screen. Not covered: a
  Steam that stays up but has not finished loading its licences answers incompletely too, and
  would pass for complete. Not carried over from the in-process days: Steam's `AppDataChanged`
  callback, which filled in a name and capsule for a game whose data Steam fetched after the
  check; such a game shows blank until the next refresh.
- `ViewModels/` hold the UI logic; every Steam call is the helper's. Views are XAML plus thin code-behind.
- `Views/MessageWindow.cs` is a hand-rolled stand-in for `MessageBox`, which Avalonia has
  no equivalent of. View models cannot show dialogs (no window to parent to), so they raise
  `ErrorRaised` / `MessageRaised` / `ConfirmRequested` and the window handles them. It
  doubles as the main window when the command line is wrong, which is why it sets
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

**The achievements window** is a game header, filter chips, a list, and a detail pane. The detail pane
exists because the description had nowhere to go in the old three-column grid.

Each list row carries the name with the description under it on **one trimmed line**
(collapsed when empty, as it is for hidden achievements), and a fixed 96-wide unlock-date
column so the dates line up. `StoreAchievementItem.UnlockDateText` gives the day only, in the
library's `d MMM yyyy`, from the unlock date, shown in local time. It is blank for a
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
system setting. The library reads the setting for itself, so a change made in Settings
reaches a helper only by being read again when it next starts (a helper has no window).

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
achievements window waiting on a store — show the running bar instead. Loops
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
  and parser (`SteamSchemaReader`) the helper uses.
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
| `games` table | `Services/GameRepository.cs` (made by `DatabaseMigrator`) | each Steam account's owned game list and its stats |
| `<appid>.img` | `Common/IconCache.cs` | capsule art, one file per app id |

Only the application process opens the database now (the helper never does), so WAL is no longer
about two processes. It still stays on: readers and the one writer overlap inside the process
(the Steam library saves from a pool thread while the stores read). Do not "simplify" it away.

**`IconCache.Prune` walks the whole directory** and deletes what belongs to a game that is no
longer owned, recovering the app id from the file name (the whole stem for a capsule, `<appid>.img`).
Files whose name yields no app id are left alone entirely — the directory is user-chosen and may
not be ours — which is also what keeps it away from the `store/` subfolder.

Startup order matters and is deliberate:

1. `LoadFromCache()` runs **synchronously** in the constructor. It is pure database reads,
   so the window is painted with games before any network or Steam work starts.
2. Capsules are not decoded at start: each tile reads its own when it comes into view (`LazyImage`).
3. `RefreshLibraryAsync()` re-downloads the published list, re-checks ownership, refreshes
   stats, and then `SaveCacheAsync()` writes it back.

Do not turn step 1 into an `async` call — the whole point is that it completes before the
first frame.

`LoadGamesAsync` **merges** into `_games` rather than clearing and rebuilding. The cached
entries already on screen carry decoded bitmaps and the user's own rating; replacing them
wholesale blanks the window and re-downloads every capsule. `GameRepository.Sync` is the
counterpart on the database side: one transaction that upserts what is owned and deletes
what is not, so a crash mid-sync cannot leave a half-written library.

Two details worth keeping:

- **Close the connection and clear the pools before moving the database.** SQLite's WAL leaves
  `-wal` and `-shm` sidecars open; `DatabaseRelocator.MoveTo` closes the scope's connection,
  calls `SqliteConnection.ClearAllPools()`, moves all three files, points `DatabaseState` and the
  connection at the new path and opens it there. Skipping the pool clear leaves the file locked on
  Windows. A move that fails puts the old path back, so the database is where it was.
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

### Pending changes (the achievements window)

`StoreAchievementsViewModel` keeps the user's uncommitted ticks on the items themselves
(`StoreAchievementItem.IsAchieved` against `OriginalValue`, `StoreStatItem.Value` against the
reported one); the list on screen is only what the filter and search left. A toggle back to the
reported value is no change, so the count is the number of real changes. Commit sends achievements
and statistics together (`IStoreAchievementsService.CommitAsync`) and **always reads everything
again afterwards**, whatever happened, because a commit that failed half-way has still changed some.
Bulk buttons skip protected rows. Only Steam has the Statistics tab.

The old editor cached its achievement list in an `achievements` table so a window painted before
Steam answered. That cache went with the editor: the helper answers in about a second.

### The stats schema

`SteamSchemaReader` (in `AchievementCaretaker.Providers.Steam.Worker`) parses
`<SteamInstall>/appcache/stats/UserGameStatsSchema_<appid>.bin`, a Valve binary KeyValues
blob, with the hand-written parser in `AchievementCaretaker.SteamApi/KeyValue.cs`, for both
achievements and statistics. It handles **two schema shapes**: a newer one where `stat.type` is a string enum name, and an
older one where `stat.type_int` (or `type`) is an integer. Keep both paths — upstream
commits in SAM (`4f2b037`, `de8b710`) exist specifically to fix regressions here. Both are
covered by tests. A statistic's or achievement's `permission` bits mark it protected (`& 2` for
statistics, `& 3` for achievements); the helper refuses to set one.

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

Capsule art belongs to the control that shows it. `Views/LazyImage` (an `Image`) is given a
`Loader` (`GameInfo.LogoLoader`, `StoreGameInfo.LogoLoader`) and asks it once the control is within
300 px of the scroll viewport (`EffectiveViewportChanged`); when it leaves, the bitmap is dropped
for the garbage collector and an unfinished load is cancelled. The view models keep **no**
`Bitmap`, only `ImageUrl` and the loader; a changed address sets the loader to null and
`RefreshGames` gives it a new one, which is what makes a tile ask again. A loader reads the disk
cache first (read and decode on the pool, never the UI thread), then downloads, writes the file
and decodes; Steam's downloads are bounded by `_logoDownloads` (8). The stores' capsules are
decoded no wider than 480 px (`StoreImageCache.LoadAsync(decodeWidth)`): Epic's art is up to
4267 px wide, and 902 of them decoded in full were 8.6 GB. Measured on the same library: 9.4 GB
before the width limit, 1.2 GB with it, 0.4 to 0.75 GB with lazy loading (it varies with the
window size and the run), flat while scrolling the Epic list. Scrolling every list to the end with the
mouse wheel (Steam, Epic and GOG in turn) peaked at 877 MB and settled at 720 MB until the store
images were read **as streams** (`StoreImageCache` no longer calls `File.ReadAllBytes`) and decoded
at most **three at a time** with the wait cancellable (`_decodeGate`), after which it peaks at 407 MB
and settles at 390 MB. The cause was the large object heap: 562 MB of it after one such session, of
which 67 MB was live, because every multi-megabyte capsule read into an array is LOH garbage that the
collector reclaims only in a gen-2 collection, and `dotnet-gcdump` on the running application
(not a guess) is what showed it. Jumping with End does not reproduce this, because it skips the
tiles in between: test by scrolling. A `Bitmap` is a few hundred bytes of
managed object over megabytes of native pixels the garbage collector cannot see, so `LazyImage`
**disposes** the bitmap when it lets go of it: without that, switching to Epic cost +430 MB, with
it +85 MB. There is no "Loading game icons" progress any more, and nothing is downloaded for a tile that is never scrolled to. The
achievements window still loads its icons with four workers through `StoreImageCache`, which
keeps them under `<icon cache>/store/<provider>/` by a hash of the URL.

## Dependency injection

`Microsoft.Extensions.DependencyInjection` is the container; `App/Services/ServiceRegistration.cs`
is the one place that says what exists and for how long. A deliberate decision: it makes
adding a provider or a service a registration, not an edit to every view model.

- **Singleton is state that belongs to the process.** The store providers hold a signed-in
  session and an `HttpClient`, so there is one of each, and **the container disposes them**.
  A view model must not.
- **Scoped is what belongs to one window.** A desktop application has no request to scope to,
  so the unit is the window: the library window gets a scope in `App.CreateMainWindow`,
  resolves `GamePickerViewModel` from it and disposes the scope when the window closes.
- **A platform check is made once, at registration.** Code that behaves differently per
  operating system is two implementations of an interface, and the `if (OperatingSystem...)`
  that picks between them lives where they are registered (`AddSessionStore`), the only place
  CA1416 needs a guard. It does not appear inside the implementations or their callers.
- **No static `HttpClient` anywhere.** Capsule art, the published game list and achievement
  icons use `IHttpClientFactory.CreateClient()` per request (the default client from
  `AddHttpClient()`), handed to `GamePickerViewModel` and `StoreImageCache`.
- The container is built with `ValidateScopes` and `ValidateOnBuild`, so a singleton that
  depends on a scoped service fails at start, not on first use.
- **Migration is in progress.** The store half is done: the library window resolves the
  caches and the store services from the container, and the logic (refresh, progress runs,
  commits, failure wording) lives in `AchievementCaretaker.Services`, with tests. View models
  still create some of their own dependencies (`AppSettings.Load`, `RatingStore.Load`), and the library's Steam half still talks to `SteamApi.Client` directly.
  Moving that logic into services, and the view models onto constructor injection, is the next
  step; do not add new `new`-ed dependencies to a view model while it is under way.

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
- **Plain data is a `record` in a `Models/` folder, in a `….Models` namespace** of its own
  assembly (`Services.Models`, `Providers.Models`, `Providers.Epic.Models`, …), one type per file.
  Positional when it is built once and read (`StoreGame`, `GameStats`), `init` properties when it
  is filled in field by field (`CachedGame`). A record prints every property in `ToString`, so a
  record that holds a credential (`EpicSession`, `GogSession`) overrides `PrintMembers` to print
  nothing; a test keeps it so.
- **File-scoped namespaces** (`namespace X;`), one per file; the `using` directives stay above it.
- **No `this.`.** Members are reached by their plain names; `this.` is written only where it is needed to tell a member from a parameter or local of the same name (and an extension method called on the instance).
- Explicit comparisons: `if (x == false)`, `if (s == true)` — not `if (!x)`.
- Private fields are `_camelCase`, including `private static readonly` ones (`_startTimeout`); constants stay `PascalCase`.
- Target-typed `new()` is used freely (`List<T> foo = new();`).
- `#region` blocks wrap each native method group in the wrappers.
- `_($"...")` (`InvariantShorthand.cs`) is the invariant-culture interpolation shorthand.
- Suppressions live in per-project `GlobalSuppressions.cs`, not inline.
- All code, comments and documentation are in English.

`LangVersion` is `latest` in both projects.

### Nullability and documentation

Nullable reference types are on everywhere (tests included; `tools/ProviderProbe` opts out) and
every non-test project writes an XML documentation file, so a public member without a `///`
comment is a warning and therefore a build error in CI.

- **Annotate what is true; do not suppress.** `?` means the value can really be absent: a store
  with no image, a Steam key with no value, a native export that did not load. `!` and
  `null!` are for late initialisation only (a field set before first use, a test fake) and a
  one-line comment says why. Prefer a guard, a `?? throw` or a property pattern.
- **Unknown stays null across boundaries.** `StoreGame.Title` is nullable: the helper sends an
  empty string over gRPC and `SteamProvider` turns it back into `null`. Do not "fix" a
  warning by turning null into `""`; a test pins it.
- **A parameter is nullable if the code tolerates null.** `IProgress<LibraryScanProgress>? progress`
  is called with `progress?.Report` and callers pass null.
- **Document with `<summary>`, and `<param>` where a parameter is not obvious.** An
  implementation of an interface member says `/// <inheritdoc />`. The vtable structs in
  `Interfaces/` document each slot with the C++ method it stands for, in declaration order.
- **A fake in a test follows the interface's nullability exactly**, or the build reports
  CS8613/CS8767. Tests change only for that reason, never to make a warning go away by
  weakening an assertion.

One trap: `using static InvariantShorthand` puts a method named `_` in scope, so `_` is
**not** available as a discard in files that import it. Name the variable instead.

View models use CommunityToolkit.Mvvm. `[ObservableProperty]` on a `_camelCase` field
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
  directory. `Program.GetExecutablePath` uses `Environment.ProcessPath`, falling back to
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
| The helper on Terraria (read) | 137 achievements, 10 stats | not run | 137 achievements, 10 stats (after invariant #5) |

Getting there required fixing the `GetISteamApps` signature (invariant #3) and the
`GetSteamID` struct-return ABI (invariant #4).

The **write** path was exercised through the helper on 2026-10-22, on Windows x64 and on Debian
under WSL2, against the project owner's live account and always put back: unlocking and
re-locking `ACH_WIN_ONE_GAME` on Spacewar, and writing and restoring one integer
(`STAT_BUCKETS_OF_BOLTS`) and one float (`STAT_FREQUENT_FLYER`) statistic of Terraria. Each write
was read back as the new value and each restore as the original. Not exercised: `ResetAllStats`
(it destroys data), a protected item against real Steam, macOS, and the 32-bit client.

If the interop ever does misbehave, the modern replacement is C# function pointers
(`delegate* unmanaged[Thiscall]<...>`), which Microsoft documents as "more efficient,
easier to use correctly, and supported in all environments," and which also drops the
per-call boxing that `DynamicInvoke` costs.

Do not pursue trimming or NativeAOT: this much reflection-based marshalling is not a
candidate, and `Marshal.GetDelegateForFunctionPointer` is annotated `RequiresDynamicCode`.

## Known issues

A clean build is **0 warnings, 0 errors** on both `AnyCPU` and `x86`. Keep it that way.
That includes the two checks that produce the most noise: nullable reference types
(`Directory.Build.props`) and XML documentation (`Directory.Build.targets`, every non-test
project), both enforced by CI with `-p:TreatWarningsAsErrors=true`. See **Nullability and
documentation** under Conventions.

- **`Wrappers/SteamClient018.cs`** — `GetSteamUtils004` requests `"SteamUtils004"` but
  still marshals the result as `ISteamUtils005`. Upstream SAM requests `"SteamUtils005"`.
  This bends invariant #2 and predates every migration here. It does not crash — the
  interface comes back non-null and `GetAppId()` returns — so it has been left alone, but
  it is still worth confirming as intentional.
- **The tile views are virtualized by `Views/TileGrid`**, an `ItemsRepeater` with a
  `UniformGridLayout` (package `Avalonia.Controls.ItemsRepeater` 12.0.0; Avalonia 12.1.2's core
  has none) inside a `ScrollViewer` subclass. It needs every tile the same size, so the owner sets
  `TileWidth`/`TileHeight` (211 x 158: the 183-wide template plus the room a `ListBoxItem` used to
  give) and a tile with a hidden meter keeps its slot. Four things about it are traps:
  - **It does what a `ListBox` did, by hand.** A press selects (`SelectedItem`, two-way), the tile
    gets the plain class `selected` (a pseudo-class cannot be set from outside the control), and
    arrows, Home, End and Page move the selection. Enter and double tap stay with the owner's
    `KeyDown`/`DoubleTapped`. Scrolling to the selection is **arithmetic** (row x `TileHeight`):
    `BringIntoView` on a tile that has not been arranged does nothing.
  - **A subclass of `ScrollViewer` has no template unless it says `StyleKeyOverride =>
    typeof(ScrollViewer)`.** Without it the control looks fine, but has an extent and viewport of
    zero, no scroll bar and does not scroll.
  - **The repeater reuses elements and only changes their `DataContext`.** `TileGrid` therefore sets
    each tile's `Content` from the `DataContext` in `ElementPrepared`; setting it once at creation
    showed the wrong games in the right places after a scroll.
  - The content (row) views are `ListBox`es over the default `VirtualizingStackPanel`, as before.
- **`ResetAllStats` has never been run against Steam**, because it destroys data. Exercise it by hand on a throwaway title before trusting a release.
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
| `packaging/chocolatey/build.sh` | the Chocolatey package from a published release (portable zips, SHA-256); `--push` sends it, with `CHOCO_API_KEY` |
| `packaging/winget/build.sh` | the winget manifests (version, locale, installer) from a published release (NSIS setup.exe, SHA-256); nothing is submitted |

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
- Nullable reference types (enabled solution-wide) → `dotnet-skills:csharp-nullable-reference-types`
- Struct layout and allocation in the hot marshalling path →
  `dotnet-skills:csharp-type-design-performance`
- Replacing the KeyValues parser or adding any serialization →
  `dotnet-skills:serialization`

**Quality gates**

- `dotnet-skills:slopwatch` — after substantial new or refactored code, to catch
  suppressed warnings, disabled checks, and empty catch blocks. Note this repo already has
  a legitimate `GlobalSuppressions.cs` convention; judge additions against it.
- `dotnet-skills:crap-analysis` — meaningful now that `tests/` exists, though coverage is thin.

**Specialist agents**

- `dotnet-skills:dotnet-concurrency-specialist` — race conditions in the download workers
  or callback pump.
- `dotnet-skills:dotnet-performance-analyst` — if the `DynamicInvoke` path is ever profiled.
- `voltagent-lang:dotnet-core-expert` — .NET 10 runtime, SDK, publish and deployment questions.

**Not applicable here** — this project has no web, database server, configuration system,
cloud, or container surface. It does have a DI container (see
[Dependency injection](#dependency-injection)); that is not a reason to reach for the
ASP.NET skills. Skip the Aspire, EF Core, ASP.NET, Akka,
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
