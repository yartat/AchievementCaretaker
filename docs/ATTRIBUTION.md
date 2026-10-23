# Attribution

Achievement Caretaker is not based on one thing. Each part has its own lineage, and each
file says which it belongs to.

| Part | Based on | Licence of the original |
|---|---|---|
| **Steam support**: `AchievementCaretaker.SteamApi`, and the Steam half of the application (the library window, the achievement editor, their caches) | [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by Rick (gibbed) | zlib |
| **GOG provider**: `AchievementCaretaker.Providers.Gog` | [GOG Achievements Manager](https://github.com/timendum/gog_achievements) by Timendum | none published |
| **Epic Games provider**: `AchievementCaretaker.Providers.Epic` | the endpoints documented by [Legendary](https://github.com/derrod/legendary) and Heroic | Legendary: GPL-3.0 |
| Everything else: the shared store code, the store windows and view models, `tools/`, CI and packaging | this project | — |

## Steam support is based on Steam Achievement Manager

The Steam support derives, directly and substantially, from **Steam Achievement Manager
(SAM)** by **Rick — [gibbed](https://github.com/gibbed)**:

- Upstream project: <https://github.com/gibbed/SteamAchievementManager>
- Original copyright: © 2008–2024 Rick (rick 'at' gibbed 'dot' us)
- Original licence: [zlib](https://opensource.org/licenses/Zlib)

SAM was released as closed source in 2008, had its last major release in 2011 and a hotfix
in 2013, and was later opened so that others could build on it. This project is one of
those derivatives.

That part is an **altered source version** of SAM, and says so:

- in the header of every file derived from it, together with the full zlib notice;
- in [`README.md`](../README.md);
- in [`docs/RELEASE-NOTES.md`](RELEASE-NOTES.md);
- in [`CLAUDE.md`](../CLAUDE.md), so that coding agents keep it that way;
- in the `Copyright` and `Description` properties of the shipped assemblies and in the
  installer and package metadata, so it survives into the binaries.

Removing any of those would break the licence, not just the courtesy.

### What the Steam support inherited

Essentially all of the native interop is SAM's work, carried over with renames only:

- the vtable marshalling layer (`NativeWrapper`, `NativeClass`, `NativeStrings`);
- the `ISteam*` interface layouts and their wrappers;
- the callback pump;
- the Valve KeyValues parsers, binary and text;
- the achievement and statistics model.

### What was changed

- Merged `SAM.Picker.exe` and `SAM.Game.exe` into one executable that dispatches on its
  command line, and renamed `SAM.API` to `AchievementCaretaker.SteamApi`.
- Replaced the Windows Forms UI with Avalonia.
- Migrated from .NET Framework 4.8 to .NET 10, including 64-bit and non-Windows targets.
- Added the SQLite library and achievement caches and the icon cache.
- Rebuilt the game list around achievement completion, with collections, sorting and two
  view modes.
- Fixed the `GetISteamApps` interop signature and the `GetSteamID` struct-return ABI.

The per-version detail is in [`RELEASE-NOTES.md`](RELEASE-NOTES.md).

## The GOG provider is based on GOG Achievements Manager

The GOG provider is based on **GOG Achievements Manager** by **Timendum**, which exists as a
command-line tool and a desktop app:

- <https://github.com/timendum/gog_achievements>
- <https://github.com/timendum/gog-achievements-gui>

Those tools list, unlock and re-lock a GOG account's achievements through the services GOG
Galaxy itself uses. What this project took from them is the knowledge of which services to
call, in what order, and what they return: the owned-games list, GOGDB for a game's client
id, and the achievement service. The C# is written for this project; no source file of
theirs was copied.

Two things were deliberately **not** taken:

- **Their sign-in.** Both tools read GOG Galaxy's refresh token out of the Windows registry,
  which is another application's stored credential. Caretaker has the user sign in on GOG's
  own page instead.
- **Their registry token, and their storage of secrets.** The account token comes from the
  user's own sign-in. Unlocking and re-locking are taken from them (see
  [GOG-RESEARCH.md](GOG-RESEARCH.md#writing)): the game's client secret is read from GOGDB's
  build record only when something is written, and is never logged or saved.

**Neither repository publishes a licence.** By default that leaves all rights with the
author. This section credits their work and says where this one comes from; it is not a
licence grant, and it does not make anything of theirs available under another licence.
Anything of theirs beyond the facts above should be used only with their permission.

## The Epic Games provider

The Epic provider is written from the endpoints that the open-source launchers
[Legendary](https://github.com/derrod/legendary) and [Heroic](https://heroicgameslauncher.com/)
document, and from community notes such as
[MixV2/EpicResearch](https://github.com/MixV2/EpicResearch): the launcher's public OAuth
client, the library and catalog services, and the store's GraphQL endpoint.

Legendary is licensed **GPL-3.0**. None of its source is included, but the two GraphQL
queries follow its published query text with fewer fields. That is said here rather than
left unsaid; whether it is enough to matter is for the project owner to decide.

## Licence position — needs a decision

The repository currently carries **two** licence files, and they do not agree:

| File | Says |
|---|---|
| `LICENSE` | MIT, © 2026 Yaroslav V Tatarenko |
| `LICENSE.txt` | zlib, © 2008–2024 Rick (gibbed) — the upstream notice |

The code derived from SAM is bound by zlib regardless: clause 3 says the notice may not be
removed, and clause 2 requires altered versions to be plainly marked. Every file derived
from SAM therefore carries the zlib notice, and `LICENSE.txt` must stay. Files that are not
derived from SAM say `See LICENSE in the repository root.` and carry no zlib notice.

What is unresolved is whether the *new* work in this repository is offered under MIT
alongside that, or whether the whole project should simply be zlib like its parent. Both
are defensible — zlib and MIT are both permissive, and zlib does not forbid distributing a
derivative under additional terms as long as the notice survives — but shipping two licence
files without saying which governs is the one option that helps nobody.

**Recommended:** keep `LICENSE` and add a sentence to it saying it covers only the work that
is not derived from SAM, with the SAM-derived code remaining under zlib. Until that is
decided, treat `LICENSE.txt` as governing the SAM-derived files and `LICENSE` the rest.

## Trademarks

Steam is a trademark of Valve Corporation, GOG and GOG Galaxy are trademarks of GOG sp. z
o.o., and Epic Games is a trademark of Epic Games, Inc. This project is not affiliated with,
endorsed by, or sponsored by any of them, nor by the authors of Steam Achievement Manager,
GOG Achievements Manager or Legendary.

## Third-party components

| Component | Licence |
|---|---|
| [Avalonia](https://avaloniaui.net/) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | Apache-2.0 |

Earlier SAM releases used the [Fugue Icons](https://p.yusukekamiyamane.com/) set by Yusuke
Kamiyamane (CC BY 3.0). Caretaker ships no icon bitmaps; every icon is a vector geometry
drawn for this project.
