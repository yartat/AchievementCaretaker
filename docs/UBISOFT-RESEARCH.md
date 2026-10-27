# Ubisoft Connect: research notes

> **The Ubisoft Connect provider is based on
> [UbiSlot](https://github.com/BalTor02/UbiSlot) by BalTor02,
> [Achievements](https://github.com/PSerban93/Achievements) by PSerban93,
> [galaxy-integration-uplay](https://github.com/melcom-creations/galaxy-integration-uplay) by melcom and
> [UplayKit](https://github.com/UplayDB/UplayKit) by UplayDB**, all MIT. It is not
> based on Steam Achievement Manager. See [ATTRIBUTION.md](ATTRIBUTION.md).

Ubisoft Connect is the fourth source in the library window, after Steam, Epic Games and GOG. Unlike
the others it is **found on this computer**: the account, the games and what was earned are read from
the files the launcher keeps, and there is nothing to sign in to. The network use without a sign-in is
Ubisoft's public content server, for pictures and achievement lists. **Signing in with Ubisoft's own page is
optional** and reads the account from Ubisoft instead (below). It is read only. The code is
`AchievementCaretaker.Providers.Ubisoft`, an `IStoreProvider` that is also an `ILocalStoreAccount`.

**Without a sign-in the launcher must be installed and logged in once.** Without its caches there is no list
of what the account owns, and the public server knows nothing about the account. That case, and what was
earned on other computers, is what the optional sign-in is for.

## Where this comes from

Four sources, none of which documents a Ubisoft endpoint that needs credentials:

- **UbiSlot** (C#, WPF, Windows) lists the games the launcher says the user owns, shows their
  achievements, and **unlocks** them by writing the launcher's spool. It credits NikitaGareev for the
  list of game ids and PSerban93 for the way the achievement data is extracted.
- **Achievements** (JavaScript, Electron) watches running games and shows notifications. For Ubisoft
  it reads the same spool and archives, and the configuration catalog for names.
- **galaxy-integration-uplay** (Python, a GOG Galaxy plugin) shows that the catalog and Ubisoft's public
  content server hold the games' names and pictures. Its own route, an online sign-in with a ticket, is
  not used here.
- **UplayDB** (Uplay-Protobufs, UplayKit, UplayApps) publishes the schemas of the launcher's cache files, which
  confirmed the layout of the ownership cache and the configuration catalog, and the launcher's socket
  protocol (Demux), which the optional sign-in uses. UplayApps' `AchiSync` is the working example of reading
  an account's achievements: sign in with the ticket, open the ownership service, open `ach_frontend`, ask
  for a product's achievements with the user's id and the platform `PC`.

What was **not** taken:

- **Unlocking.** UbiSlot writes the spool, and to do so makes the launcher treat it as a running game:
  it loads `uplay_r164.dll`, calls `UPLAY_Startup`, which asks for the process to be restarted under the
  launcher, and writes while that session is alive. That needs a third way of starting this
  executable and a channel that does not depend on its standard input (the launcher starts the
  process, not Caretaker), and, by UbiSlot's own README, "modifying achievement data may carry account
  or service-related risks". Not part of this step.
- **Rarity.** Achievements' table of Uplay and Steam ids could give Steam's global percentages for the
  games that are also on Steam. Not used.
- **Password sign-in.** UbiServices (UplayDB) and the launcher's own tools sign in with an e-mail and a
  password. Caretaker never reads a password, so it signs in with Ubisoft's page and takes the session from it.

## What is read

All paths are the current user's.

| What | Where | Gives |
|---|---|---|
| Ownership cache | `%LOCALAPPDATA%\Ubisoft Game Launcher\cache\ownership\<user guid>` | the ids the account owns; the file name is the user's id |
| Achievement archive | `%PROGRAMDATA%\Ubisoft\Ubisoft Game Launcher\cache\achievements\<game id>_<hash>[.zip]` | names, descriptions (every language) and icons |
| Spool | `%LOCALAPPDATA%\Ubisoft Game Launcher\spool\<user guid>\<game id>.spool` | the achievements earned on this computer, with the time |
| Configuration catalog | `%LOCALAPPDATA%\Ubisoft Game Launcher\cache\configuration\configurations` | per product: name, whether it can be started, pictures, the achievement list's file name |
| Game names | `Data\UplayGames.txt`, shipped with the application | `id\|name` for 448 games; used only when the catalog does not know a product |
| Public content server | `https://static3.cdn.ubi.com/orbit/uplay_launcher_3_0/assets/<file>` | pictures and achievement archives, no sign-in; the archive is byte for byte the launcher's |

### The ownership cache

A header of 264 bytes (not understood, not needed), then protocol buffers: repeated records (field 1),
each with two varints (fields 1 and 2). Both are ids; which is the product and which something it
belongs to is not known, so both are taken. The ids are games but also add-ons and other products, so
**only the ids a name is known for are taken to be games**. That is what keeps the library to games
and what ages: a game released after the list shipped is left out until the list is updated.

### The configuration catalog

Protocol buffers, no header: repeated records (field 1), each with the product id (field 1), the id it
is launched by (field 2) and a block of YAML (field 3). On the machine this was written on that is 1,348
records, 1,135 with a block. Only a few fields are read, by indentation, because the blocks hold tabs and
other things a strict YAML parser refuses:

- `root.start_game` is present for something that can be played; add-ons, tools and the stubs that link
  a Steam purchase to the account have none.
- `root.name` is often not a name but the **key** of a line in the `localizations.default` table (`l1`,
  `NAME`), and so are `thumb_image` and `background_image`. Placeholders (`name`, `l1`, `GAMENAME`, an
  empty value) fall back to `root.installer.game_identifier`, then to the table's `GAMENAME`, and
  otherwise the product has no name.
- `root.thumb_image` (240 x 280), `root.background_image` (1200 x 700, the one used) and `root.logo_image`
  (800 x 240) are file names on the public content server.
- `root.uplay.achievements` is the archive's file name on the same server (`<hash>` or `<hash>.zip`);
  the launcher keeps it as `<game id>_<that name>`. Absent for a game with no achievements.

The ownership cache lists a product under both of its ids, so the provider joins the two by the
catalog (a game is listed once).

### The achievement archive

A zip. `<locale>_loc.txt` for eleven languages (`en-US`, `ru-RU`, ...), each line `id<TAB>name<TAB>description`
(the description can contain tabs), `<achievement id>.png`, and `achievements.dat`, whose records
repeat the ids four times over; what else it holds is not known and nothing uses it. The name carries a
hash that changes with the contents, so the file is found by its `<game id>_` prefix, and **the `.zip`
extension is not always there**: of two archives on the machine this was written on, one has it and one
does not.

### The spool

Protocol buffers: repeated records (field 1) holding the achievement's id as a varint one message
down (field 1) and the time as a varint of its own (field 2), in seconds (milliseconds are told apart
by size). The id is found by field number, a few levels deep, because that is how both tools read it.

## Signing in (optional)

Ubisoft's web client signs in on `https://connect.ubisoft.com/login?appId=...` and leaves the session in the
page's local storage: `PRODloginData` (`ticket`, `sessionId`, `userId`, `nameOnPlatform`) and `PRODrememberMe`
(`rememberMeTicket`). galaxy-integration-uplay reads those; Caretaker runs a small script in the embedded browser
that moves the page to `https://connect.ubisoft.com/change_domain/<those, as JSON>` once they exist, so the sign-in
window only has to recognise an address, as it does for GOG. The password is typed into Ubisoft's page and the
window never reads a field.

The sign-in page is opened with the same `appId` the session service is called with (`f68a4bb5-...`, the Ubisoft
Connect client's, and genome id `954e66a0-...`), as galaxy-integration-uplay does: the Club ids it keeps as a
fallback also open a login form, but a ticket made for one application is not necessarily accepted as another's.
The page was checked in a browser: it loads with these parameters, stores `PRODloginData` (the web client's
environment prefix and `loginData`), and the script's address is a valid URL.

A sign-in is **kept** whatever the first renewal answers, with an hour's expiry: the page has just made the ticket,
and the socket service is what says whether it is any good. The session service is sent `Ubi-SessionId` as well,
as the plugin's session always does.

The session is checked, and later renewed, at `https://public-ubiservices.ubi.com/v3/profiles/sessions`:
`PUT` with `Authorization: Ubi_v1 t=<ticket>`, else `POST` with `Authorization: rm_v1 t=<remember-me ticket>` and
`{"rememberMe": true}`, both with `Ubi-AppId`. The answer has a fresh `ticket`, `sessionId`, `rememberMeTicket`, and
`expiration` and `serverTime` (their difference is how long it lasts). It is kept in `~/.sac/ubisoft.json`
(DPAPI).

### The socket service

`dmx.upc.ubisoft.com:443`, TLS. Every message, in both directions, is a four-byte big-endian length and a
protocol buffers message (`Upstream` and `Downstream`, from `demux.proto`). In order:

1. `GetPatchInfoReq("DEFAULT")` answers with the current protocol `latest_version`; the client pushes it back as
   its own version. (UplayKit hard-codes 12922; the server said **13393** when this was checked, so the client uses
   what it is told.)
2. `AuthenticateReq` with the session's ticket (`token.ubi_ticket`, client id `uplay_pc`).
3. `OpenConnectionReq("ownership_service")`. Its messages travel inside data pushes, each a length and a
   message: `InitializeReq` returns every `OwnedGame` (`product_id`, `owned`, `product_type` 0 for a game, `state`,
   and `configuration`, the same YAML as the launcher's catalog), `GetProductConfigReq` returns one product's
   block. Every request carries `ubi_ticket` and `ubi_session_id`.
4. `OpenConnectionReq("ach_frontend")`: `ReadAchievementsReq(user_id, product(product_id, "PC"))` returns the
   achievements the account has earned, each an id and a time. **`product_id` is the catalog block's
   `uplay.achievements_sync_id` (`ACK` for Valhalla), not the product id**: asked by product id, or by space id, the
   service answers with an empty list and no error, which reads as "nothing earned". The platform is `PC`
   (`uplay` and `UPLAY` answer empty).

Names, pictures and achievement lists are then found as without a sign-in, from the block and the public server.

**Verified** against the real server and a real account: the connection and framing, the version answer, the
sign-in page and its script, the ticket sign-in, the ownership list and the achievement calls. The provider is
also tested against a stand-in that speaks the protocol (`FakeDemuxServer`), which proves the client follows the
definitions and not that Ubisoft's server still does; a refused ticket, a closed service or a version Ubisoft no
longer accepts show as an error message.

Two things the first run on an account found: the ownership list has **1,346 products, 458 owned, 63 of type
game**, and the launcher's own library includes games in state 0 (cannot be downloaded any more) and 4 (expired),
so no state is filtered; the ones left out are the products owned only through a lapsed subscription
(`locked_by_subscription`), which are the same games again under another product id. And the achievements need
the sync id above.

## What the files hold, and what they do not

This is the part to read before trusting a number. On the machine this was written on (one Ubisoft
account, the launcher installed and logged in):

| | |
|---|---|
| ids in the ownership cache | 261 |
| games the catalog and the name list make of them | **54** (51 by the name list alone) |
| archives the launcher had saved | **2** |
| archives on the public server for the 54 games | **28** (the games with a `uplay.achievements` entry) |
| spool files | **3** (66, 21 and 3 records) |

So:

- The **library** is the owned products that are games. A game released after the catalog on the
  machine was last updated is not in it.
- A game's **achievement list** comes from the launcher's archive if it has one, else from the
  public server, else (the catalog names none) it is empty, which here does mean "this game has none".
  A game the catalog does not know at all has no list and the error says so (`no_local_data`).
- The spool has **what was earned on this computer**. An achievement not in it is shown as not earned,
  which cannot be told from earned on another computer, and the launcher does not say. Same caveat as
  the other stores' "no record".
- The spool of a game with no list is **not** turned into one: its ids cannot be named.
- XP, rarity and progress are unknown (`null`).

The library lists each game's counts when its archive is already on the computer (the launcher's, or one
downloaded before); **Load progress** downloads the rest, 28 small files.

## Verified

Against the machine's real files and the real public server, with the provider run from a throwaway
console program (nothing written to the launcher's folders):

- the library: 54 games, each with its background picture;
- achievement lists for all 28 games that have one, with no failures, 26 games with none;
- Anno 1800: 112 achievements, 3 unlocked; Far Cry 6: 99, 21 unlocked; Assassin's Creed Valhalla: 92, 66
  unlocked (its list now comes from the server, where before only the spool's 66 records were known);
- the server's archives are byte for byte the launcher's own for the games both have.

Signed in to the account used for this (57 games, all with a picture): Valhalla 66 of 92 and Far Cry 6 21 of 99
(the same as the launcher's spool), Odyssey 84 of 93, Assassin's Creed Unity 36 of 57, Black Flag 40 of 60 and Far
Cry 3 36 of 44, which were earned on other computers and which the launcher's files do not have; 30 games with a
list, none failed.

Not verified in the application window yet: the pictures and the downloads through **Load progress**.
Not verified at all: the "not found" panel (needs a machine with no launcher), a launcher update that
changes the files, an account with a different set of games, and comparing every date with the
launcher's own window.
