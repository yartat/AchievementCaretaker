# Security and defect audit — remediation plan

Audit of the repository at commit `2304e26` (2.2.0 released), done on 2026-11-26. It covers
vulnerabilities, defects, implicit (hidden) behaviour and wrong access rights. Each finding is written
so that it can be fixed on its own, in order, by someone who has not read the rest of this file.

## How to use this file (read first)

You are fixing the findings below. Follow these rules for every one of them:

1. **Work one finding at a time**, in the order of the table. Do not combine fixes in one change.
2. **Read `CLAUDE.md` before the first change.** It is binding. The rules that matter most here:
   - Code, comments and documents are in **English**.
   - Explicit comparisons: `if (x == false)`, not `if (!x)`. No `this.`. Private fields `_camelCase`.
   - Every source file keeps its header. Do not remove an attribution.
   - Tests use the **public surface only**; there is no `InternalsVisibleTo`. Do not add one.
   - A clean build has **0 warnings, 0 errors** on AnyCPU and x86.
   - **Never commit, tag or push.** Leave the changes in the working tree.
3. **After each finding**, run:
   ```bash
   dotnet build AchievementCaretaker.slnx -c Release -p:TreatWarningsAsErrors=true
   dotnet build AchievementCaretaker.slnx -c Release -p:Platform=x86 -p:TreatWarningsAsErrors=true
   dotnet test --solution AchievementCaretaker.slnx -c Release
   ```
   All three must pass before you start the next finding. If `bin/` cannot be written because the
   application is running, close it; do not kill a process you did not start.
4. Every finding lists **Acceptance**: the test(s) to add. Add them in the same change.
5. Do not "improve" code next to the fix. Touch only what the finding names.
6. Do not change behaviour that `CLAUDE.md` documents as deliberate unless the finding says so. If a
   fix contradicts `CLAUDE.md`, update the matching paragraph of `CLAUDE.md` in the same change.
7. Line numbers are as of commit `2304e26`; search for the quoted code if they have moved.

Severity: **High** = data loss or a security boundary that does not hold; **Medium** = exploitable
with a precondition, or a defect users will hit; **Low** = hardening, robustness; **Info** = note,
optional.

## Summary

| ID | Severity | Area | Title |
|---|---|---|---|
| A01 | High | Steam helper | Token is not checked on the streaming RPC `ScanLibrary` |
| A02 | High | Icon cache | `IconCache.Prune` deletes the user's own files whose names start with digits |
| A03 | High | Windows installer | Uninstall wipes the whole chosen directory; install wipe guard is weak |
| A04 | Medium | Image cache | `file:` image URLs are accepted from any source, including UNC paths |
| A05 | Medium | Ubisoft unlock | Lost update: earned list is read and rewritten in two separate steps |
| A06 | Medium | Session files | Unix session file is briefly world-readable; writes are not atomic |
| A07 | Medium | Steam helper | Commit leaves partial state, does not validate stat ranges, can throw untyped |
| A08 | Medium | Ratings | A failed load followed by a save destroys all ratings; writes not atomic |
| A09 | Medium | Database move | Overwrites the destination, half-done move loses the WAL, case rule wrong on Linux |
| A10 | Medium | Steam helper | Helper's stderr/stdout pipes are never drained: the helper can hang |
| A11 | Medium | CI / supply chain | Actions pinned by tag, `pip install` unpinned in a job holding a secret, shell interpolation of the tag |
| A12 | Low | Parsers | Unbounded recursion in `KeyValue` / `TextKeyValue` crashes the process |
| A13 | Low | Steam helper | Handshake record prints the private key; certificate not disposed; SHA-1 pin |
| A14 | Low | Sign-in | Epic end-of-sign-in host match has no dot boundary; scheme not checked |
| A15 | Low | Downloads | No size limit on downloads and zip entries |
| A16 | Low | Published list | XML read with default reader; a bad value escapes as an unhandled exception |
| A17 | Low | Steam polling | A helper process is started every 3 s forever when Steam has no account |
| A18 | Low | Ubisoft provider | Connection dropped outside its lock; idle timer sources leak |
| A19 | Low | Steam helper | Temp folder created with default (0755) permissions on Unix |
| A20 | Low | Windows installer | `$INSTDIR` passed unescaped to `cmd /c` |
| A21 | Low | Data folder | `~/.sac` and its files get default permissions on Unix |
| A22 | Info | Sign-in window | The embedded browser shows no address and goes anywhere |
| I01–I06 | Info/Low | Implicit behaviour | Things the application does without being asked (see last section) |

---

## A01 — High — Steam helper token is not checked on the streaming RPC

**Where:** `AchievementCaretaker.Providers.Steam.Worker/SteamWorkerService.cs:241-266`
(`TokenInterceptor`), RPC `ScanLibrary` at line 129, proto
`AchievementCaretaker.Providers.Steam/Protos/*.proto` line 19
(`rpc ScanLibrary (...) returns (stream ScanLibraryEvent)`).

**Problem:** `TokenInterceptor` overrides only `UnaryServerHandler`. gRPC calls
`ServerStreamingServerHandler` for a server-streaming method, and the base `Interceptor` passes it
straight through. So `ScanLibrary` runs **without the token check**. The helper listens on
`127.0.0.1` and the TLS certificate only authenticates the server, so **any local process, of any
local user**, can find the port and call `ScanLibrary` to read the user's owned Steam library and make
the helper issue thousands of Steam client calls. `CLAUDE.md` states "the helper refuses any call
without the token", which is false for this method.

**Fix:**
1. In `TokenInterceptor`, also override `ServerStreamingServerHandler`, `ClientStreamingServerHandler`
   and `DuplexStreamingServerHandler`. Each calls `Check(context)` first, then the continuation.
   Covering all four means a future streaming RPC cannot reopen the hole.
2. Keep `Check` as it is (constant-time compare).

**Acceptance:** in `tests/AchievementCaretaker.Providers.Steam.Tests/SteamWorkerHostTests.cs` add:
- a test that calls `ScanLibrary` through a `SteamWorkerChannel` built with a **wrong token** and
  asserts `RpcException` with `StatusCode.Unauthenticated` when reading the stream;
- the same with the right token succeeds (the fake backend already returns a scan).

---

## A02 — High — `IconCache.Prune` deletes the user's own files

**Where:** `AchievementCaretaker.App/Common/IconCache.cs:159` (`TryGetAppId`), `:193` (`Prune`),
`:212` (`MoveTo`).

**Problem:** the icon directory is **user-chosen** (Settings dialog). `TryGetAppId` takes any file
name, strips the extension, cuts at the first `_`, and parses the rest as a number. So `2024_taxes.pdf`,
`1.png`, `123.txt` all "have an app id". `Prune` deletes every such file whose number is not an owned
game. If the user points the cache at `Documents`, `Desktop` or `Downloads`, files are **deleted
permanently** (no recycle bin). `MoveTo` uses the same enumeration, so it also moves those user files
to the new directory and overwrites same-named files there (`File.Move(..., true)`).

**Fix:**
1. Make `TryGetAppId` accept only the names this class writes:
   - `<digits>.img` (`FileFor(uint)`), and
   - `<digits>_<iconName>` where the whole name was produced by `FileFor(uint, string)`.
   The simplest safe rule: accept only names that match the regular expression
   `^[0-9]{1,10}(\.img|_[A-Za-z0-9._-]+)$` **and** whose extension is one of `.img`, `.jpg`,
   `.jpeg`, `.png`, `.gif`, `.ico` (check what `FileFor(uint, string)` actually stores; if achievement
   icon names come from Steam with other extensions, list them). Use a `[GeneratedRegex]`.
2. In `MoveTo`, do not overwrite an existing file in the destination: use `File.Move(file, target,
   false)` and skip (leave in place) on `IOException`.
3. Update the `CLAUDE.md` paragraph "**`IconCache.Prune` walks the whole directory**" to state the
   exact name rule.

Do **not** move the cache into a subfolder as part of this fix; it changes the on-disk layout.

**Acceptance:** `IconCache` is internal to the App, which has no test project. Move the name rule into
a small public static helper in `AchievementCaretaker.Services` (for example
`IconCacheNames.TryGetAppId(string fileName, out uint appId)`) and call it from `IconCache`. Test it in
`AchievementCaretaker.Services.Tests`: `570.img` → true/570; `570_abc.jpg` → true; `2024_taxes.pdf`,
`1.png.bak`, `123.txt`, `report.img`, `../570.img` → false.

---

## A03 — High — Windows uninstaller wipes the whole chosen directory

**Where:** `packaging/windows/installer.nsi:50` (`MUI_PAGE_DIRECTORY`), `:86` and `:136`
(`RMDir /r "$INSTDIR"`).

**Problem:** the user can choose any install directory. The **uninstaller** runs `RMDir /r
"$INSTDIR"` unconditionally. If the user installed into an existing folder (for example `Documents` or
`D:\Games`), uninstalling deletes **everything in it**. The install-time wipe (line 86) is guarded only
by `AchievementCaretaker.exe` existing there: a portable zip extracted straight into `Downloads`
followed by an install into `Downloads` wipes `Downloads`.

**Fix (all three):**
1. Make the directory page always end in the application's own folder. Add a `.onVerifyInstDir`
   function (or a directory-page leave callback) that appends `\${APP_ID}` when the chosen path does
   not already end with it. Example:
   ```nsis
   Function .onVerifyInstDir
       ${GetFileName} "$INSTDIR" $R0
       ${If} $R0 != "${APP_ID}"
           StrCpy $INSTDIR "$INSTDIR\${APP_ID}"
       ${EndIf}
   FunctionEnd
   ```
   (`${GetFileName}` needs `!include "FileFunc.nsh"`, already used for `${GetSize}`.)
2. At install, write a marker file `$INSTDIR\.achievement-caretaker-install` after `File /r`. Guard
   the install-time `RMDir /r` on that **marker** existing, not on the exe.
3. In the uninstaller, replace `RMDir /r "$INSTDIR"` with: if the marker exists, `RMDir /r`; otherwise
   delete only `Uninstall.exe` and the files the installer put there (the publish directory listing can
   be generated at build time into an `!include`d file of `Delete` lines) and finish with a non-recursive
   `RMDir "$INSTDIR"`.
4. Update the `CLAUDE.md` bullet "**The Windows installer is per-user...**" accordingly.

**Acceptance:** this cannot be unit-tested. Build the installer with
`bash packaging/windows/build-installer.sh` (see `docs/BUILD.md`) and check by hand: install into
`%TEMP%\x` that contains a file `keep.txt`, then uninstall — `keep.txt` must remain and the app must be
in `%TEMP%\x\AchievementCaretaker` (or whatever `APP_ID` is). Write the steps you ran into
`docs/BUILD.md`, section about the installer.

---

## A04 — Medium — `file:` image URLs accepted from any source, including UNC

**Where:** `AchievementCaretaker.App/Common/StoreImageCache.cs:119` (and `OpenLocal`).

**Problem:** `LoadAsync` opens **any** `file:` URL as a local file. Image and icon URLs come from store
servers (Epic, GOG JSON), from the Steam helper, and from rows cached in SQLite. A URL like
`file://attacker.example/share/x.png` becomes `\\attacker.example\share\x.png`: Windows connects over
SMB and **sends the user's NTLM credentials hash** to that host; `file:///C:/Users/...` makes the app
read arbitrary local files. Only Ubisoft is meant to use `file:`, and only for icons it extracted
itself.

**Fix:**
1. Accept `file:` only when **all** hold: `provider == "ubisoft"` (use
   `AchievementCaretaker.Providers.Ubisoft.UbisoftProvider.ProviderId` if the App references it, else
   the literal), `address.IsUnc == false`, and the full path is **inside** the Ubisoft icon directory
   (`Path.Combine(AppSettings.HomeDirectory, "ubisoft", "icons")`): compare
   `Path.GetFullPath(address.LocalPath)` against that directory with a trailing separator,
   `OrdinalIgnoreCase` on Windows, `Ordinal` elsewhere.
2. Otherwise treat the URL as invalid and return `null`.
3. For the network branch, accept only `https:` (reject `http:` and every other scheme) before calling
   `GetByteArrayAsync`.

**Acceptance:** move the decision "is this URL allowed and how" into a public static helper in
`AchievementCaretaker.Services` (for example `ImageUrlPolicy.Classify(string provider, string url,
string ubisoftIconDirectory)` returning `Local(path)` / `Remote(uri)` / `Rejected`) and test:
UNC `file://host/share/a.png` → Rejected; `file:///C:/Windows/win.ini` → Rejected; a file inside the
icon dir with provider `ubisoft` → Local; same with provider `gog` → Rejected; `http://...` →
Rejected; `https://...` → Remote; `ftp://...` → Rejected.

---

## A05 — Medium — Ubisoft unlock can drop achievements (lost update)

**Where:** `AchievementCaretaker.Providers.Ubisoft/UbisoftProvider.Online.cs:506` onward
(`SetAchievementsAsync`): `current` is read in one `WithOnlineAsync` call, the whole list is written in
another.

**Problem:** the achievements service **replaces** a game's whole earned list. Between the read and
the write the `_connecting` lock is released, so another call — or the game itself, running on this or
another computer and recording an achievement — can change the list. The write then sends the old list
plus the new ids and **erases** what was earned in between. The final check only verifies the *added*
ids, not that earlier ones survived.

**Fix:**
1. Do the read, the merge and the write inside **one** `WithOnlineAsync` callback, so they run on one
   connection under one lock without another call in between.
2. After the write, re-read and verify that **every id from `current`** and every added id is present.
   If any earlier id is missing, throw `ProviderException("not_recorded", "...")` with a message that
   says earlier achievements may have to be checked in Ubisoft Connect. (Do not try to "repair".)
3. Note in the XML doc of `SetAchievementsAsync` that a game running at the same time can still race
   the service and that the confirmation dialog should say "close the game first". Add that sentence to
   the confirmation text in `AchievementCaretaker.App/ViewModels/StoreAchievementsViewModel.cs`
   (`StoreAsync`, the `ConfirmRequested("Unlock", ...)` message).

**Acceptance:** in `tests/AchievementCaretaker.Providers.Tests/UbisoftProviderTests.cs` add a test where
`FakeDemuxServer` changes `Earned["SYNC"]` between the read and the write (add a hook to
`FakeDemuxServer`, for example `Action? BeforeWrite`, that adds an id) and assert that the earlier id
added by the hook is still present afterwards, **or** that `not_recorded` is thrown. With fix step 1
the hook runs inside the same connection's call sequence, so make the hook fire on the first write
request.

---

## A06 — Medium — Session file permissions and non-atomic writes

**Where:** `AchievementCaretaker.Providers.Shared/PlainSessionStore.cs:50-53`,
`AchievementCaretaker.Providers.Shared/DpapiSessionStore.cs:80`.

**Problem:**
1. **Unix:** `File.WriteAllBytes` creates the file with the process umask (normally `0644`,
   world-readable) and only then `File.SetUnixFileMode` sets `0600`. For that window the refresh token
   (a credential for the whole store account) is readable by every local user. The parent directory
   `~/.sac` is created with default permissions (`0755`).
2. **Both stores:** the file is overwritten in place. A crash or power loss mid-write leaves a truncated
   file; `Load` then returns `null` (see I02) and the user is signed out. GOG and Ubisoft rotate the
   refresh token on every renewal, so the old token is already dead: the account must sign in again.

**Fix:**
1. Write to `path + ".tmp"` and then `File.Move(tmp, path, overwrite: true)` (atomic rename on the same
   volume) in both stores.
2. In `PlainSessionStore`, create the temp file with its final mode from the start:
   ```csharp
   var options = new FileStreamOptions
   {
       Mode = FileMode.Create,
       Access = FileAccess.Write,
       UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
   };
   using (var stream = new FileStream(tmp, options)) { stream.Write(bytes); stream.Flush(true); }
   ```
   `UnixCreateMode` only applies when the file is created, so delete a stale `tmp` first.
3. In `PlainSessionStore`, create the directory with `Directory.CreateDirectory(directory,
   UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)` when it does not exist.
4. Keep the existing `SetUnixFileMode` call after the move as a belt-and-braces step.

**Acceptance:** in `tests/AchievementCaretaker.Providers.Tests` (the Unix tests already skip on
Windows): assert the saved file mode is `0600` and the new directory mode is `0700`; assert that
after `Save` no `.tmp` file remains; for both stores, assert that a `Save` over an existing file leaves
a readable session.

---

## A07 — Medium — Steam commit: partial state, no range checks, untyped errors

**Where:** `AchievementCaretaker.Providers.Steam.Worker/SteamAppBackend.cs:131-198`.

**Problem:**
1. Changes are applied one by one with `SetAchievement`/`SetIntStat`/`SetFloatStat`. If one fails the
   method throws "nothing has been stored", but the **earlier changes stay set in the Steam client's
   memory** and are stored by the next successful `StoreStats` (next commit, or the game).
2. Statistics are not checked against the schema's `Min`, `Max`, `MaxChange`, `IncrementOnly` in the
   helper. The UI checks them (`StatValueParser`), but the helper is the trust boundary.
3. `checked((int)Math.Round(change.Value))` throws `OverflowException`, and `NaN`/`Infinity` pass to
   `SetFloatStat`. `Guarded` only converts `ProviderException`, so these escape as `StatusCode.Unknown`.
4. When the schema is missing (`schema == null`, line 146) the protected-achievement check is skipped
   entirely, and achievement ids are never checked against the schema.

**Fix:**
1. **Validate everything first, then apply.** Build the full list of changes, check every one (known
   id, not protected, finite, in range, integer fits `int`, `IncrementOnly` not decreasing,
   `MaxChange` respected against the current value), and only if all pass call the setters.
2. Step 1 removes almost every way a setter can fail half-way. For a setter that still returns false,
   there is **no** clean "discard local changes" call: `ISteamUserStats013` has no
   `RequestCurrentStats` slot (Valve removed it in this interface version; see
   `AchievementCaretaker.SteamApi/Interfaces/ISteamUserStats013.cs`). Do **not** add vtable fields
   (invariant #1 of `CLAUDE.md`). Instead, after a failed setter, put back the values already changed
   in this commit to what `TryGetAchievement`/`TryGetIntStat`/`TryGetFloatStat` returned before the
   commit started (read them during validation), then throw. Do not call `StoreStats` on that path.
3. When the schema is missing and there are achievement changes, throw
   `ProviderException("no_schema", ...)` instead of skipping the check.
4. Convert `OverflowException` and non-finite values to `ProviderException("invalid_value", ...)`.

**Acceptance:** in `tests/AchievementCaretaker.Providers.Steam.Tests/SteamBackendTests.cs` (it has a
`FakeGateway`): a commit with one valid and one out-of-range stat sets **nothing** (assert the fake saw
no setter call); `NaN` → `invalid_value`; value above `int.MaxValue` → `invalid_value`; an achievement
change with no schema → `no_schema`; an unknown achievement id → refused.

---

## A08 — Medium — Ratings can be wiped

**Where:** `AchievementCaretaker.App/Library/RatingStore.cs:96` (load swallows every exception),
`:173` (`File.WriteAllText` in place). Same pattern in `AchievementCaretaker.App/Common/AppSettings.cs`.

**Problem:** if `ratings.json` cannot be read (locked by antivirus, half-written, corrupt), `Load`
returns an empty set. The next like/dislike **saves only that one rating** over the file: all others are
gone. `CLAUDE.md` calls these ratings "the part that cannot be rebuilt". A crash during the in-place
write produces exactly that corrupt file.

**Fix:**
1. Atomic write: temp file + `File.Move(..., overwrite: true)`.
2. On load failure **other than "file does not exist"**: copy the file to
   `ratings.json.corrupt-<yyyyMMddHHmmss>` before anything else, and set a flag so that `Save` does not
   overwrite until a load has succeeded (return `false` from `Save` and let the caller show the existing
   error path). Choose the simpler of the two if both are not possible; the backup copy is mandatory.
3. Apply step 1 to `AppSettings` saving too.

**Acceptance:** `RatingStore` is internal to the App. Put the atomic-write helper in
`AchievementCaretaker.Services` as a public static `AtomicFile.WriteAllBytes(string path, byte[] data)`
(reuse it in A06 and A09) and test it: writes the content, leaves no temp file, overwrites an existing
file. Test the backup behaviour through a public seam only if one exists; otherwise describe the manual
check in the PR text.

---

## A09 — Medium — Database move can lose data

**Where:** `AchievementCaretaker.Services/DatabaseRelocator.cs:43-80`.

**Problem:**
1. `File.Move(from, destination + suffix, true)` **overwrites** whatever is at the destination path the
   user typed (another application's `games.db`, for example).
2. The three files (`db`, `-wal`, `-shm`) are moved one by one. If the second move fails, the `catch`
   restores the **path** to the source but the main file has already moved: the source now has no
   database (a new empty one is created on next open) and the WAL is orphaned.
3. The same-path check uses `OrdinalIgnoreCase` on every OS. On Linux `Games.db` and `games.db` are
   different files: the move is skipped and the connection is pointed at an empty new file.

**Fix:**
1. Before moving, if `destination` (or its `-wal`/`-shm`) already exists, throw `IOException` with a
   clear message; do not overwrite.
2. Before moving, run `PRAGMA wal_checkpoint(TRUNCATE);` on the open connection so the WAL is empty,
   then close and clear pools as now. After a checkpoint only the main file has to move; move `-wal`
   and `-shm` only if they still exist and are non-empty.
3. On failure, move back any file already moved (best effort) before restoring the path.
4. Compare paths with `OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ?
   StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal`. Per `CLAUDE.md`, a platform check
   belongs at registration; if you follow that strictly, pass the comparison into `DatabaseRelocator`
   from `LibraryDatabaseExtensions`.

**Acceptance:** in `tests/AchievementCaretaker.Services.Tests` (the relocator is already tested):
moving onto an existing file throws and leaves both files unchanged; after a successful move with data
written, the data is readable at the destination and nothing remains at the source.

---

## A10 — Medium — Steam helper pipes are not drained

**Where:** `AchievementCaretaker.Providers.Steam/SteamWorkerProcessLauncher.cs:41-53`.

**Problem:** stdout and stderr are redirected. stdout is read once (the `READY` line); stderr is read
only after a failure. If the helper writes more than the pipe buffer (about 4 KB on Windows) to either
stream while running — an exception trace from Steam, a runtime warning — its write **blocks** and the
helper hangs; the application waits until its own timeouts.

**Fix:** after reading the `READY` line, start background draining:
- `process.ErrorDataReceived += ...` with `process.BeginErrorReadLine()`, keeping the **last** ~8 KB in
  a bounded buffer (used for error messages);
- for stdout, after `READY`, start `process.BeginOutputReadLine()` and discard.
Do not call `BeginErrorReadLine` before the handshake is written. Make `ReasonAsync` use the bounded
buffer instead of `ReadToEndAsync`.

**Acceptance:** hard to unit-test without the executable. Add a short note to the class XML doc and
check by hand that a helper start still works (open a Steam game's achievements with Steam running).

---

## A11 — Medium — CI supply chain

**Where:** `.github/workflows/ci.yml`, `.github/workflows/release.yml`.

**Problem:**
1. Every action is referenced by a movable tag (`actions/checkout@v4`, `actions/setup-dotnet@v4`,
   `actions/upload-artifact@v4`, `actions/download-artifact@v4`). The release jobs hold
   `contents: write` and the secrets `CHOCO_API_KEY` and `CLOUDSMITH_API_KEY`.
2. `release.yml:296` runs `pip install --quiet cloudsmith-cli` with no version and no hash, in the job
   that has `CLOUDSMITH_API_KEY` in its environment.
3. `release.yml:243` interpolates `${{ github.ref_name }}` directly into a `run:` script. Git tag names
   may contain `$(`, `)` and `;`, so a tag like `v1.2.3$(…)` matching `v*.*.*` is shell injection
   (requires push rights, but defence in depth).

**Fix:**
1. Pin every `uses:` to a full commit SHA with the tag in a comment, e.g.
   `uses: actions/checkout@<40-hex-sha> # v4.x.y`. Look the SHAs up from the action repositories' tags.
2. Pin the CLI: `pip install --quiet "cloudsmith-cli==<version>"` and prefer
   `--require-hashes` with a small `packaging/apt/requirements.txt` containing the hashes.
3. Pass the tag through `env:` (`TAG: ${{ github.ref_name }}`) and use `"$TAG"` in the script. Do the
   same for any other `${{ ... }}` inside `run:` blocks that is not a constant.
4. Add a `.github/dependabot.yml` with `package-ecosystem: github-actions` so pinned SHAs get updates.

**Acceptance:** `actionlint` (if available) passes; the workflow files still parse
(`gh workflow view` or a dry push to a branch). Do not push tags to test.

---

## A12 — Low — Unbounded recursion in the KeyValues parsers

**Where:** `AchievementCaretaker.SteamApi/KeyValue.cs:315` (`current.ReadAsBinary(input)` for type
`None`), `AchievementCaretaker.SteamApi/TextKeyValue.cs:149` (`ParseObject` for `{`).

**Problem:** both recurse once per nesting level with no limit. A file with deep nesting (two bytes per
level for the binary format, one `{` per level for the text format) causes a `StackOverflowException`,
which **cannot be caught** and kills the process. These files are Steam's caches
(`appcache/stats/*.bin`, `userdata/*/config/localconfig.vdf`), read by the **main application**
(`LibraryStats`) as well as the helper. The binary reader also ignores the nested call's `false` result.

**Fix:** add a depth parameter with a limit of 64 to both (private overload; keep the public signature).
Beyond the limit, the binary reader returns `false` and the text reader returns `Invalid`. Propagate a
nested `false` from the binary reader. Files derived from SAM keep their zlib header.

**Acceptance:** tests in `tests/AchievementCaretaker.Providers.Steam.Tests` (they already build binary
KeyValues): 10 000 nested empty objects → `ReadAsBinary` returns false without crashing; same for
`TextKeyValue.Parse` with 10 000 `"a" {` → `Invalid`; normal files still parse.

---

## A13 — Low — Handshake secrets in `ToString`, certificate lifetime, SHA-1 pin

**Where:** `AchievementCaretaker.Providers.Steam/SteamWorkerHandshake.cs:20,47,50`,
`AchievementCaretaker.Providers.Steam/SteamWorkerChannel.cs:39`,
`AchievementCaretaker.Providers.Steam.Worker/SteamWorkerHost.cs:52`.

**Problem:**
1. `SteamWorkerHandshake` is a `record` holding `PfxBase64` (the private key) and `Token`. A record's
   `ToString` prints both. `CLAUDE.md` requires credential records to override `PrintMembers`.
2. `SteamWorkerHost` loads the certificate with its private key and never disposes it. On Windows an
   imported PFX key is persisted under the user profile until the certificate is disposed; a helper that
   exits without disposing can leave key files behind on every launch.
3. The client pins the SHA-1 `Thumbprint`. SHA-1 is deprecated for identity.
4. `new X509Certificate2(certificate)` in the validation callback is never disposed.

**Fix:**
1. Add `private bool PrintMembers(System.Text.StringBuilder builder) => false;` to the record.
2. Dispose the certificate in `SteamWorkerHost.DisposeAsync` (keep it in a field).
3. Pin SHA-256: in the handshake expose `CertificateHash` =
   `Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256))`; the client compares
   `certificate.GetCertHash(HashAlgorithmName.SHA256)` with `CryptographicOperations.FixedTimeEquals`.
   Rename `SteamWorkerEndpoint.Thumbprint` accordingly and update its users and tests.
4. `using var presented = new X509Certificate2(certificate);` in the callback — or use the
   `X509Certificate.GetCertHash(HashAlgorithmName)` overload directly without creating a new object.

**Acceptance:** test that `handshake.ToString()` contains neither the token nor the PFX; the existing
TLS host tests still pass with the SHA-256 pin; a test with a different certificate is refused.

---

## A14 — Low — Sign-in end address matching

**Where:** `AchievementCaretaker.Providers.Epic/EpicProvider.cs:101-106`; also
`AchievementCaretaker.Providers.Gog/GogProvider.cs` (`GetSignInResultKind`) and
`AchievementCaretaker.Providers.Ubisoft/UbisoftProvider.Online.cs` (`GetSignInResultKind`).

**Problem:** Epic accepts any host that **ends with** `epicgames.com`, so `evil-epicgames.com` matches;
its page text would then be taken as the sign-in result (login CSRF: the user ends up signed in to an
attacker's account). None of the three checks the scheme, so an `http:` address also matches.

**Fix:** Epic: `host == "epicgames.com" || host.EndsWith(".epicgames.com")` (ordinal ignore case). All
three: require `address.Scheme == Uri.UriSchemeHttps`.

**Acceptance:** extend the existing sign-in address tests
(`tests/AchievementCaretaker.Providers.Tests/ProviderRegistrationTests.cs`, `GogProviderTests.cs`,
`UbisoftProviderTests.cs`): `https://evil-epicgames.com/id/api/redirect` → `None`;
`http://www.epicgames.com/id/api/redirect` → `None`; the real address still → `PageText`.

---

## A15 — Low — Unbounded downloads and zip entries

**Where:** `AchievementCaretaker.App/Common/StoreImageCache.cs:132`,
`AchievementCaretaker.App/ViewModels/GamePickerViewModel.cs:1074`,
`AchievementCaretaker.Providers.Ubisoft/UbisoftProvider.cs:247`,
`AchievementCaretaker.Services/SteamLibraryService.cs:48`, provider `ReadAsStringAsync` calls, and
`AchievementCaretaker.Providers.Ubisoft/AchievementArchive.cs` (`CopyIcon`, `ReadAchievements`).

**Problem:** `GetByteArrayAsync` buffers the whole response (default limit 2 GB). A huge or endless
response exhausts memory. Zip entries are copied/read in full with no size check (zip bomb).

**Fix:** set `MaxResponseContentBufferSize` on the named/default clients in the registrations
(images: 32 MB; Ubisoft archive: 64 MB; published list: 32 MB; store JSON: 32 MB). For zip entries,
refuse an entry whose `Length` exceeds 16 MB (icons) / 8 MB (localization text) and copy through a
bounded stream.

**Acceptance:** a `FakeStore` route returning more than the limit makes the call fail with a handled
error (`ProviderException` / `null` image), not an out-of-memory; a zip with an oversized entry is
rejected by `AchievementArchive` (build it with `UbisoftFiles.Archive`).

---

## A16 — Low — Published list parsing

**Where:** `AchievementCaretaker.Services/SteamLibraryService.cs:69-84` (`ParseGameList`), `:50`.

**Problem:** `new XPathDocument(stream)` uses a reader whose DTD handling is not explicitly disabled.
`current.ValueAsLong` throws `FormatException` on a bad value, which the `catch` at line 50 does not
include, so it escapes as an unexpected error.

**Fix:** create the document from `XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing =
DtdProcessing.Prohibit, XmlResolver = null })`. Parse each value with `long.TryParse` and skip invalid
entries. Add `FormatException` to the caught exceptions.

**Acceptance:** tests in `AchievementCaretaker.Services.Tests`: a list with a DTD is rejected
(`XmlException` → `list_unavailable`); a list with one non-numeric id still returns the valid ones.

---

## A17 — Low — Steam helper started every 3 seconds with no back-off

**Where:** `AchievementCaretaker.Services/SteamConnector.cs:113-131`.

**Problem:** while Steam runs but `GetAccountAsync` fails (nobody logged in, Family Share lock, broken
client), the connector starts a **new helper process** (a full .NET start plus loading
`steamclient`) every 3 seconds, indefinitely.

**Fix:** exponential back-off on consecutive `GetAccountAsync` failures: 3 s, 6 s, 12 s … capped at 60 s;
reset to 3 s when the Steam process id changes (`ISteamEnvironment`) or on success.

**Acceptance:** the connector takes an interval in its constructor; add a test with a fake library that
always fails and a short base interval, and assert the number of `GetAccountAsync` calls over a window
is logarithmic, not linear (or assert the delays passed to an injected delay function).

---

## A18 — Low — Ubisoft provider connection handling

**Where:** `AchievementCaretaker.Providers.Ubisoft/UbisoftProvider.Online.cs:168` (`SignOut`), `:184`
(`Dispose`), `:615` (`DropWhenIdle`).

**Problem:** `SignOut` and `Dispose` call `DropConnectionAsync()` without taking `_connecting`, so they
can dispose the connection while a call is using it (it then fails and retries). Both block with
`GetAwaiter().GetResult()` (works today only because every await in `DemuxClient` uses
`ConfigureAwait(false)`). Each `DropWhenIdle` creates a `CancellationTokenSource` that is never disposed.

**Fix:** take `_connecting` (with `Wait()` in the synchronous methods) around `DropConnectionAsync`;
dispose the previous `_idle` source when replacing it and when cancelling it.

**Acceptance:** existing Ubisoft tests pass; add one that calls `SignOut` while a
`GetAchievementsAsync` is in flight (the `FakeDemuxServer` can delay its answer) and asserts no
unhandled exception.

---

## A19 — Low — Steam helper temp folder permissions

**Where:** `AchievementCaretaker.Providers.Steam.Worker/SteamAppIdFile.cs:73`.

**Problem:** the folder is created in the shared temp directory with default permissions (`0755` on
Linux/macOS). The name is random, so it is not predictable, but other users can read it.

**Fix:** use `Directory.CreateTempSubdirectory("sac-steam-")` (creates the folder with `0700` on Unix
and returns its `DirectoryInfo`). Keep the zlib header (this file is derived from SAM).

**Acceptance:** extend `tests/AchievementCaretaker.Providers.Steam.Tests/SteamAppIdFileTests.cs`: on Unix
the folder mode is `0700` (skip on Windows).

---

## A20 — Low — `$INSTDIR` passed unescaped to `cmd`

**Where:** `packaging/windows/installer.nsi:150`.

**Problem:** `Exec 'cmd /c ... del /f /q "$INSTDIR\Uninstall.exe" & rmdir "$INSTDIR"'`. A path containing
`"` cannot exist on Windows, but `&`, `^` and `%VAR%` inside the quoted path are still interpreted by
`cmd` in some forms. With A03's fix the directory always ends in the app folder, but the parent part is
user-chosen.

**Fix:** replace the `cmd` line with NSIS's own deferred deletion: `Delete /REBOOTOK
"$INSTDIR\Uninstall.exe"` and `RMDir /REBOOTOK "$INSTDIR"`, or keep `cmd` but reject (during
`.onVerifyInstDir`) paths containing `&`, `^`, `%` or `!`.

**Acceptance:** manual: install into `%TEMP%\a&b`, uninstall, nothing outside the app folder is touched.

---

## A21 — Low — `~/.sac` permissions on Unix

**Where:** `AchievementCaretaker.App/Common/AppSettings.cs` (home directory creation), the database
(`games.db`), `ratings.json`, `ubisoft/`.

**Problem:** the directory and files are created with default permissions (`0755`/`0644`). The library,
account ids, ratings and downloaded data are readable by other local users.

**Fix:** where `~/.sac` is created (find `Directory.CreateDirectory` for `HomeDirectory`), on Unix create
it with `UnixFileMode.UserRead | UserWrite | UserExecute` (`0700`). Do not change the mode of a
directory that already exists and was chosen by the user (relocated caches).

**Acceptance:** manual on Linux, or a Unix-only test if `AppSettings.HomeDirectory` has a seam (it does
not today — then manual).

---

## A22 — Info — Embedded sign-in window shows no address

**Where:** `AchievementCaretaker.App/Views/StoreSignInWindow.axaml(.cs)`.

**Problem:** the window shows the page without its address, and does not restrict where the page may
navigate. A redirect to a look-alike page cannot be noticed by the user.

**Fix (optional):** show the current host (from `NavigationCompleted`) in the window, with a lock glyph
for `https`. Optionally cancel navigation to hosts outside an allow-list per provider
(`IStoreAccount` could expose `IReadOnlyCollection<string> SignInHosts`), keeping identity providers the
stores use (Google, Apple, Facebook, Steam, Xbox, PlayStation, Nintendo) in mind — if unsure, show the
host only.

---

## Implicit behaviour (what happens without the user asking)

These are not all bugs; several are documented and deliberate. They are listed so that each is a
conscious decision. Fix only what the "Action" line asks for.

### I01 — Info — Bulk reads start by themselves
- After a store sign-in, **Load progress** starts automatically: hundreds of requests (Epic ~1 200,
  GOG ~600, including two per game to GOGDB, a volunteer site). Documented in `CLAUDE.md`.
- Ubisoft: opening a game or **Load progress** downloads achievement archives from Ubisoft's CDN into
  `~/.sac/ubisoft/archives`; they are never pruned.
- **Action:** none required. Optional: a setting "Read progress after signing in" (default on).

### I02 — Low — A session is silently discarded
- `DpapiSessionStore.Load` and `PlainSessionStore.Load` return `null` for **any** exception (locked
  file, transient I/O, corrupt file). The account then looks signed out with no message, and the next
  sign-in overwrites the file.
- A legacy plain-text session is silently re-saved encrypted (documented, fine).
- **Action:** distinguish "file missing" (null) from "unreadable": on unreadable, rename the file to
  `<name>.unreadable-<timestamp>` before returning `null`, so it is not lost and the cause can be seen.
  Never put the file content in any message.

### I03 — Info — The Steam helper is started in the background
- See A17. Also: a helper stays 30 s after its last call; Ubisoft's socket stays 20 s. Both documented.
- **Action:** A17 only.

### I04 — Info — Ubisoft requests present a browser identity
- `UbisoftProvider.Online.cs` sends a Chrome `User-Agent`, `Origin: https://connect.ubisoft.com` and the
  web client's `Ubi-AppId`, and the socket client announces itself as `uplay_pc`. Recording achievements
  outside a game may be against Ubisoft's terms (the README says so).
- **Action:** none in code. Keep the README warning.

### I05 — Low — Failures are swallowed
- About 40 `catch (Exception)` blocks without logging, most in `IconCache`, `GamePickerViewModel`,
  `StoreImageCache`, `StoreLibraryService`, `SteamWorkerProcessLauncher`, `RatingStore`, `AppSettings`.
  Each is individually defensible ("a cache that cannot be written is a slow app"), but together they
  hide real problems (A08, I02).
- **Action:** narrow the ones in `RatingStore`, `AppSettings` and the session stores to the exceptions
  that are expected (`IOException`, `UnauthorizedAccessException`, `JsonException`,
  `CryptographicException`). Leave the cache ones.

### I06 — Info — One Ubisoft socket connection per account
- Ubisoft allows one connection per account. Caretaker's connection can disconnect Ubisoft Connect (or
  another Caretaker) for up to 20 s, and vice versa. Documented in `CLAUDE.md`.
- **Action:** none.

---

## Checked and found sound (do not change)

- Token comparison in the helper is constant-time (`CryptographicOperations.FixedTimeEquals`).
- The helper's certificate and token travel only on standard input, never on the command line or disk.
- SQL is parameterised (Dapper); the only interpolated SQL (`DatabaseMigrator.DropIfWithoutAccount`)
  uses compile-time table names.
- Icon file names from Steam (`IconCache.FileFor(uint, string)`) are validated against path traversal.
- Ubisoft archive file names use `Path.GetFileName(spec)`; icon files are named by numeric ids.
- Credential records `EpicSession`, `GogSession`, `UbisoftSession` override `PrintMembers`.
- Error messages from Epic/GOG do not echo response bodies or token-bearing URLs.
- The `.deb` sets explicit modes (`0755` directories and executables, `0644` files) and is built with
  `--root-owner-group`.
- Workflows default to `permissions: contents: read`; only the release job gets `contents: write`.
- No secrets, keys or certificates are tracked in git; `/dist/` and `publish/` are ignored.
