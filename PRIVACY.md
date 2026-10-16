# Privacy Policy

Achievement Caretaker is a desktop application. Its author, Yaroslav V Tatarenko, runs no
server that the application reports to, and receives no data from it. This document says what
the application stores on your computer, which outside services it contacts, and what leaves
your computer as a result.

Last updated: 2026-10-16.

## What the application does not do

- It sends no usage statistics, analytics, crash reports or advertising identifiers anywhere.
- It has no account of its own. Your Steam, Epic Games and GOG accounts are used only through
  those stores' own clients or sign-in pages, as described below.
- It does not read your passwords, and it does not read the stored credentials of any other
  application.

## What is stored on your computer

Everything is kept in the `.sac` folder in your user profile (`C:\Users\<you>\.sac` on Windows,
`~/.sac` elsewhere).

| File or folder | What it holds |
|---|---|
| `settings.json` | the chosen cache location and theme |
| `ratings.json` | your own like or dislike for each game. This is not your Steam review and is never sent to Steam |
| `games.db` | the library, achievement counts and achievement lists you have loaded, kept separately for each account |
| `icons/`, `store/` | cached game images and achievement icons |
| `epic.json`, `gog.json` | your sign-in token for that store, present only while you are signed in to it |

The sign-in tokens are a credential for your whole store account. On Windows they are encrypted
with the Windows Data Protection API for your user account. On other systems the file is readable
only by your user. Signing out deletes the token. Deleting the `.sac` folder removes everything the
application has stored.

## Outside services the application contacts

Each request is an ordinary web request, so the service sees your IP address and the request
itself.

**Steam** (only if you use the Steam source)
- Your Steam client, on your computer. The application loads Steam's own client library and asks
  it for your library, ownership, achievements and statistics. When you commit an achievement
  change, it is sent to Steam through that client, as the Steam client does.
- `cdn.steamstatic.com` and `shared.cloudflare.steamstatic.com` serve game images.
- `gib.me` serves the published list of Steam app IDs that the application checks your library
  against. The same list is used by Steam Achievement Manager.

**Epic Games** (only if you sign in to Epic)
- `epicgames.com`, for signing in, in the application's embedded browser, and for the account
  and launcher services that issue your sign-in token.
- Epic's library, catalog and store services (`launcher.store.epicgames.com`,
  `library-service.live.use1a.on.epicgames.com`, `catalog-public-service-prod06.ol.epicgames.com`,
  `account-public-service-prod03.ol.epicgames.com`). They receive your token and the product
  identifiers of the games you have.

**GOG** (only if you sign in to GOG)
- `auth.gog.com`, for signing in, in the embedded browser, and for refreshing your token.
- `embed.gog.com`, `api.gog.com` and `gameplay.gog.com`, for your library, product information,
  and your achievements. To unlock or re-lock an achievement, they receive the request you make.

**GOGDB** (`www.gogdb.org`, a volunteer community site; only for GOG)
- Used to find the internal identifier that GOG uses for a game. Reading achievements sends the
  game's product identifier. Changing an achievement also reads the game's client credentials
  from the same record, and sends the request for the new token to GOG. GOGDB is not told about
  your account.

## Sign-in

Signing in happens on the store's own sign-in page. The page is shown in a browser window inside
the application, or, if that is unavailable, in your own browser. Caretaker only watches the web
address the page moves to, so that it knows when sign-in has finished. It does not see what you
type.

The embedded browser keeps its own cookies. On Windows it runs in private mode, so nothing is kept
when the window closes. On Linux with the WPE engine there is no private mode, and a sign-in may
remain in the browser's data until you remove it.

## Your choices

- **Sign out** in the application removes that store's sign-in token from your computer and
  leaves the store's account untouched.
- **Do not use a source** and nothing is sent to that store. Steam is used only when you open the
  library or an achievement window.
- **Delete the `.sac` folder** to remove everything the application has stored. Uninstalling the
  application does not delete it.
- **Changes to an achievement** are sent to the store only when you press Commit.

## Children

The application is not directed at children, and nothing in it collects data from anyone,
including children.

## Changes to this policy

This file lives in the project repository. Changes are recorded in its history, and the date
above is updated with each change.

## Contact

Questions about this policy or about the application's data handling: open an issue at
<https://github.com/yartat/AchievementCaretaker/issues>.
