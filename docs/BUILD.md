# Building and releasing

> Steam support in Achievement Caretaker is based on
> [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by Rick
> (gibbed), and is an altered source version of it; GOG support is based on
> [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum.
> See [ATTRIBUTION.md](ATTRIBUTION.md).

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The exact version is
  pinned in `global.json` with `rollForward: latestFeature`; do not remove that file, or
  builds drift onto whatever preview SDK happens to be installed.
- Nothing else. NuGet packages (Avalonia, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite) are
  restored automatically. Visual Studio is optional — 17.14 or newer can open
  `AchievementCaretaker.slnx` directly, since the `.slnx` solution format needs no
  conversion.

## A normal build

```bash
dotnet build AchievementCaretaker.slnx -c Release
```

Output goes to `bin/`:

```
bin/
  AchievementCaretaker.exe          the application
  AchievementCaretaker.dll
  AchievementCaretaker.SteamApi.dll the Steam interop layer
  AchievementCaretaker.runtimeconfig.json
  AchievementCaretaker.deps.json
  Avalonia.*.dll, libSkiaSharp.dll, libHarfBuzzSharp.dll, av_libglesv2.dll, …
```

**The whole directory is needed to run, not just the `.exe`.** A framework-dependent build
also needs the .NET 10 Desktop Runtime installed on the target machine.

A clean build is **0 warnings, 0 errors** on both platforms. Keep it that way.

## 32-bit

```bash
dotnet build AchievementCaretaker.slnx -c Release -p:Platform=x86
```

Output goes to `bin/x86/`. **The two platforms must not share an output directory.** With a
pinned runtime identifier the native Skia and HarfBuzz libraries are copied flat next to
the executable rather than into `runtimes/<rid>/native/`, so an x86 build and an x64 build
writing to the same folder overwrite each other's natives. The survivor then dies at
startup with `The version of the native libSkiaSharp library (88.1) is incompatible`, which
is an architecture mismatch wearing a version-number disguise.

**Publishing 32-bit needs both flags: `-r win-x86` *and* `-p:Platform=x86`.** The runtime
identifier alone resolves 32-bit native libraries but still emits a **64-bit apphost**, and
the resulting bundle dies in SkiaSharp's type initializer with nothing on stdout or stderr
to say why — it looks like the application simply vanishes. On a non-Windows host both are
required, because the project's own x86 default is guarded by `IsOSPlatform('Windows')`.
`packaging/check-pe-arch.sh` asserts it, and CI runs that check on every Windows bundle:

```bash
bash packaging/check-pe-arch.sh out/some-published-dir win-x86
```

The solution-to-project platform mapping is written out explicitly in
`AchievementCaretaker.slnx`:

```xml
<Platform Solution="*|x86" Project="x86" />
```

Without it, a solution-level `-p:Platform=x86` silently builds AnyCPU — the `.slnx` reader
does not infer the mapping.

## Building for another platform

There is one executable, so there is one publish command:

```bash
dotnet publish AchievementCaretaker.App/AchievementCaretaker.App.csproj \
  -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

Substitute any of `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`
or `osx-arm64`. Cross-publishing works from any host; you do not need a Linux or Mac to
produce those bundles. For `win-x86`, add `-p:Platform=x86` — see [32-bit](#32-bit).

`RuntimeIdentifier` defaults to the *host's* RID (`$(NETCoreSdkPortableRuntimeIdentifier)`),
so a plain `dotnet build` on Linux produces a Linux apphost. Do not hardcode `win-x64` as
the default — an earlier revision did, and a build on Debian happily emitted a `.exe` PE32+
binary with Windows `.dll` natives.

Only x86 and x64 can actually talk to Steam; see the platform table in the
[README](../README.md#platform-support).

## Self-contained build

A normal build is framework-dependent and needs the .NET 10 Desktop Runtime installed. To
produce a build that runs without it — at the cost of roughly 120 MB:

```bash
dotnet publish AchievementCaretaker.App/AchievementCaretaker.App.csproj \
  -c Release -r win-x64 --self-contained true -o publish/win-x64-selfcontained
```

Do **not** enable trimming or NativeAOT. The interop layer is built on
`Marshal.GetDelegateForFunctionPointer`, which is annotated `RequiresDynamicCode`, and the
XAML and MVVM layers are reflection-heavy.

## Packaging a release

Both pipelines do all of this; the list is here for anyone cutting a release by hand or
changing how one is cut.

1. Publish the target RID **self-contained** (see above). For `win-x86`, remember
   `-p:Platform=x86` as well as `-r win-x86`.
2. Check the architecture with `packaging/check-pe-arch.sh`.
3. Strip the `.pdb` files. They are about a third of the tree and nothing needs them.
4. Run `packaging/windows/build-installer.sh` for the installer, and zip the publish
   directory for the portable archive. On Linux, `packaging/linux/build-deb.sh`.
5. Ship the whole publish directory in the archive, not just the executable.
6. Update [RELEASE-NOTES.md](RELEASE-NOTES.md) and the `Version` / `AssemblyVersion` /
   `FileVersion` properties in both `.csproj` files. They are kept in step.
7. Update the version in the two window-title literals, which are not generated from the
   assembly version: `Title=` in `Views/GamePickerWindow.axaml`, and `_Title` plus the
   `this.Title = …` assignment in `ViewModels/ManagerViewModel.cs`.

### The application icon

`AchievementCaretaker.App/Resources/AchievementCaretaker.ico` is both the
executable's icon (`ApplicationIcon`) and the window icon (`avares`), and it carries seven
entries: 16, 24, 32, 48, 64, 128 and 256. The vector master is
[`assets/app-icon.svg`](assets/app-icon.svg) — Steam's gear with an achievement star, in
the dark theme's gold.

**The 16px entry is not a downscale of the master.** Eight gear teeth at that size smear
into a lumpy halo that dirties the silhouette, so that entry drops the teeth and uses a
thicker ring and a larger star instead; 24 and 32 keep the teeth but widen them. If the
icon is regenerated, do the same rather than resizing one image seven times — and check
the result at actual size, not magnified.

### Linux runtime prerequisites

Avalonia's Skia and X11 backends `dlopen` system libraries that a minimal Debian does not
ship:

```bash
apt-get install -y libfontconfig1 libice6 libsm6
```

`libfontconfig1` is needed before `libSkiaSharp` will load at all; `libice6` and `libsm6`
are needed by `Avalonia.X11`'s session management. With those three the application starts
cleanly on a bare Debian 13.

## Continuous delivery

The repository carries pipelines for **both** GitHub Actions and GitLab CI. They build the
same five artifacts from the same scripts under `packaging/`, and either can be deleted if
only one forge is used.

Everything is cross-built on Linux — no Windows runner is needed — and both pipelines
share the version gate below.

**Pushing a tag `vX.Y.Z` is the whole release process.** It produces:

| Artifact | What it is |
|---|---|
| `achievement-caretaker-X.Y.Z-win-x64-setup.exe` | Windows installer, 64-bit |
| `achievement-caretaker-X.Y.Z-win-x64-portable.zip` | unzip and run, 64-bit |
| `achievement-caretaker-X.Y.Z-win-x86-setup.exe` | Windows installer, 32-bit |
| `achievement-caretaker-X.Y.Z-win-x86-portable.zip` | unzip and run, 32-bit |
| `achievement-caretaker_X.Y.Z_amd64.deb` | Debian/Ubuntu package |

**All five are self-contained** — the .NET runtime is inside, so nothing has to be
installed first. For the portable archive that is the whole point; for the installer it is
because an installer that finishes and then reports a missing runtime is a worse first run
than a larger download. Debug symbols are stripped, which is about a third of the tree.

### GitHub Actions

| Workflow | Runs on | Does |
|---|---|---|
| `.github/workflows/ci.yml` | pushes to a branch, pull requests | builds with warnings as errors, publishes both Windows targets and checks their architecture, builds the `.deb` |
| `.github/workflows/release.yml` | a `v*.*.*` tag, or `workflow_dispatch` | builds the three artifacts and attaches them to a GitHub release |

The `.NET` SDK comes from `actions/setup-dotnet` with `global-json-file: global.json`, so
the pinned SDK version is honoured rather than restated. `workflow_dispatch` builds
everything and uploads it as workflow artifacts *without* creating a release, which is how
to exercise the packaging without tagging.

The release job is the only one granted `contents: write`, and it uses the preinstalled
`gh` CLI rather than a third-party action. Action versions are floating major tags
(`@v4`); pin them to commit SHAs if you want the stricter supply-chain posture.

### GitLab CI

`.gitlab-ci.yml` uses the `mcr.microsoft.com/dotnet/sdk:10.0` image, which must carry an SDK
at least as new as `global.json` asks for or restore fails before anything else does; the
build job prints `dotnet --version` first so that is visible in the log.

Artifacts are uploaded to the project's generic package registry and the release links point
there rather than at job artifacts, because job artifacts expire and a release link that
404s a month later is worse than no link.

There is no arm64 job, on purpose — see the platform table in the
[README](../README.md#platform-support).

### The version gate

The pipeline does **not** override the version from the tag. `packaging/check-version.sh`
instead fails the pipeline unless the tag, both `.csproj` files, both window-title literals
and the `docs/RELEASE-NOTES.md` heading all say the same thing.

That is deliberate: the titles are literals that cannot be driven from the assembly
version, so overriding the version from the tag would ship a binary whose window said
something else. Run it yourself before tagging:

```bash
bash packaging/check-version.sh 1.0.0
```

### The Windows installer

`packaging/windows/build-installer.sh` drives NSIS, which cross-compiles the installer stub,
so this needs no Windows machine and no Wine — only the Debian/Ubuntu `nsis` package. The
script is a thin wrapper; the installer itself is `packaging/windows/installer.nsi`.

```bash
bash packaging/windows/build-installer.sh \
  --publish-dir out/win-x64 --version 1.0.0 --arch x64 --output dist
```

It installs **per user** into `%LOCALAPPDATA%\Programs\AchievementCaretaker`, which is
deliberate: the payload is self-contained, nothing is registered system-wide, no
administrator is needed, and it sidesteps the Program Files / Program Files (x86) split so
the x64 and x86 builds share one location and one Add/Remove Programs entry rather than
leaving two half-installs behind. Installing either over the other wipes the directory
first, so no stale assemblies survive an upgrade or an architecture switch.

**The uninstaller removes only what was installed.** `~/.sac` — settings, the game cache
and the user's own like/dislike ratings — is left alone: uninstalling is not a request to
throw away data that a reinstall would want back.

Silent install and uninstall work the usual NSIS way, which is also how CI-built installers
were verified:

```
setup.exe /S /D=C:\path\without\quotes     (/D must come last)
Uninstall.exe /S
```

### Why everything is self-contained

A framework-dependent `.deb` would have to declare `Depends: dotnet-runtime-10.0`, and that
package exists only in Microsoft's own apt repository — so `apt install ./…deb` would fail
with an unmet dependency on a stock Debian or Ubuntu.

The Windows builds started framework-dependent, at about 12 MB zipped against the ~34 MB
they are now. They were changed to self-contained so that one sentence covers every
download: nothing else to install. A portable build that first asks you to install a
runtime is not portable, and the installer has no way to fetch one.

### Building the packages without tagging

On GitHub, run the **Release** workflow by hand (`workflow_dispatch`) **from a branch**: it
builds all three artifacts and uploads them as workflow artifacts, and the release step is
skipped because `github.ref_type` is not `tag`. Dispatching it against a *tag* does publish
a release - the condition is on the ref, not on the event. On GitLab both package jobs are `when: manual` on branches and upload
nothing unless a tag is set. Locally:

```bash
dotnet publish AchievementCaretaker.App/AchievementCaretaker.App.csproj   -c Release -r linux-x64 --self-contained true -o out/linux-x64
bash packaging/linux/build-deb.sh --publish-dir out/linux-x64 --version 1.0.0 --output dist
```

This needs a filesystem with POSIX permissions. The script refuses to build a package whose
apphost is not executable, which is what a Windows working copy would otherwise silently
produce — it installs cleanly and then does nothing at all.

### What is in the deb

| Path | Holds |
|---|---|
| `/usr/lib/achievement-caretaker/` | the self-contained publish, `.pdb` stripped |
| `/usr/bin/achievement-caretaker` | a wrapper that `exec`s the apphost |
| `/usr/share/applications/….desktop` | the menu entry |
| `/usr/share/icons/hicolor/scalable/apps/….svg` | the icon, from `docs/assets/app-icon.svg` |
| `/usr/share/doc/…/copyright` | DEP-5 copyright carrying the SAM attribution and the zlib text |
| `/usr/share/doc/…/changelog.gz` | one entry per release |

`Depends` covers the three libraries found empirically on a bare Debian 13
(`libfontconfig1`, `libice6`, `libsm6`), the .NET runtime's own set, and the X libraries the
running process was observed to load (`libx11-6`, `libx11-xcb1`, `libxrandr2`), with
alternatives lists for ICU and OpenSSL so one package spans Debian 12/13 and Ubuntu
20.04–24.04. The GL stack is deliberately **not** declared: it is mesa or a vendor driver
depending on the machine, and depending on either would be wrong. If the application ever
fails to start on a minimal install, this list is the first place to look.

Two things the build script drops or overrides for a reason:

- **`libcoreclrtraceptprovider.so` is deleted.** It is the only file in a self-contained
  publish with a dependency the package does not satisfy — `liblttng-ust.so.0`, which Debian
  does not install by default. The runtime dlopens it only when LTTng tracing is on and
  carries on without it, so dropping it is better than shipping a library that cannot link
  or depending on a tracing stack nobody here uses.
- **`dpkg-deb -Zxz`, not the zstd that Debian 13 would choose.** A dpkg older than about
  Debian 12 cannot read a `control.tar.zst` at all, and this package is downloaded rather
  than installed from an archive. xz also turned out smaller here: 33.7 MB against 36.5 MB.

`lintian` is clean apart from tags inherent to shipping a self-contained .NET application:
`embedded-library` for the copies of freetype, libjpeg, libpng and zlib inside SkiaSharp and
`System.IO.Compression.Native`, `unstripped-binary-or-object` for the NuGet-shipped natives
(stripping them saves 2 MB of 103, which is not worth rewriting upstream binaries for), and
`no-manual-page` for a GUI application.

The maintainer field defaults to the project author; override it with the `DEB_MAINTAINER`
environment variable or `--maintainer`.

## Verifying a build

There are no tests in this repository. What can be checked without a Steam client:

- **The application starts.** Run `bin/AchievementCaretaker.exe` with Steam not
  running. It must come up showing an error window rather than dying — that proves
  `App.Initialize` loaded every style and resource dictionary, which the compiler does not
  check.
- **The windows construct and render.** A throwaway console project referencing
  `AchievementCaretaker.App`, with `Avalonia.Headless` and `Avalonia.Skia` and
  `UseHeadlessDrawing = false`, can call `SetupWithoutStarting()` and then construct, show,
  measure, arrange and `CaptureRenderedFrame()` each window. This catches missing
  `StaticResource` keys and broken `StyleInclude` paths, which compile cleanly and only fail
  when the window opens.

What cannot be checked without Steam: the vtable dispatch, the schema parse, `LibraryStats`,
and every number the windows display. Changes to interop must be tested by hand against a
live, logged-in client, in **both** bitnesses — x86 hides an entire class of calling
convention bug.

The **write** path (`SetAchievement`, `SetStatValue`, `StoreStats`, `ResetAllStats`) mutates
a real Steam account and has not been re-verified since the .NET 10 migration. Exercise it
by hand on a throwaway title before trusting a release.
