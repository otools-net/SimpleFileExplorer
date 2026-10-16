# Changelog

What each version of SFE added or fixed. What an upgrade changes for an existing install (database, settings, first
scan) is in [docs/upgrading.md](docs/upgrading.md).

## 1.51.0

### Added

- **Read and Write rights:** every right is now either Read (it only looks) or Write (it changes something). The group
  and space editors list the Read rights first and mark the others **Changes things**, or **Changes files** for the
  rights that create, change or delete the library's files.
- **Read-only spaces:** editing a space, **Files read-only** switches off every right that changes files and
  **Read-only** every right that changes anything. In the configuration, `folder:write` and `kind:write` in
  `Sfe:Rights:Disabled` do the same for the whole server.

## 1.50.0

### Added

- **Animated images play on hover:** a large GIF, PNG, WebP or AVIF shown through its still thumbnail plays the image
  itself while the mouse rests on it (on a touch screen, while you hold it), like the trickplay of videos. Only files
  with several frames are played. Each user can turn it off (**Thumbnails › Play animated images on hover**,
  `Sfe:Thumbnails:AnimatedHover`).

## 1.49.0

### Added

- **Search on the administration page:** a box above the sections finds settings and pages by the words of their
  names, descriptions and choices (or their configuration key). Opening a setting found shows its page, scrolled to
  it, even when it is only in the full view.
- **Overview of the administration page:** it opens on every section you may use as a card, by group, with links to
  its pages; **Administration** and the group in the breadcrumbs lead back to it.
- Each page of the administration says what it holds under its title.

### Changed

- **My account** is now the first section of the administration page.
- **Administration reorganised:** the Preferences group is now **Viewing**; **Playback** has **Player**, **Streaming**
  and **Video streams** (moved from System); **System › Maintenance** holds backups and the server commands, which
  left **About**. Old addresses of moved pages still open them.

## 1.48.0

### Added

- **No setup code on the server's own computer:** a new install opened in a browser on the computer SFE runs on skips
  the code; from any other device, or once an account exists, it is still asked (`Sfe:Setup:LocalWithoutCode`).
- **Fast sign-in** for SFE used on one computer: the browser the portable package opens at start signs the
  administrator in without a password, with a one-time link. Offered by the setup page, switched in **Administration ›
  System › Start-up**; only while the server has a single administrator (`Sfe:FastSignIn`).
- **Administration › System › Start-up:** whether the portable package opens the browser when it starts.
- `--no-browser` on the command line keeps the browser closed, whatever the settings say.

## 1.47.0

### Added

- Settings `Sfe:Security:TrustedProxies` (reverse proxies on other computers whose forwarded visitor address and
  scheme are believed) and `Sfe:Security:RequireHttps` (secure cookie and HSTS when the app is only used over https).
- `Sfe:Security:AccountLockoutFailures` (default 50): failures from all addresses together that lock a local account.
- **Recover the account** on the sign-in page: a server administrator locked out, or who forgot the password, asks
  for a one-time code that the server shows only where it runs (its console, and `recovery-code.txt` beside the setup
  code); with it, a new password unlocks the account and signs it in, ending its other sessions. No restart needed.
- **Security warning**, a new severity in **Logs**: wrong passwords (with the name and address), blocked addresses
  and locked accounts, wrong setup codes, account recoveries, the first administrator and installation changes are
  logged with it, and can be filtered apart from other warnings (`SEC` in the log files).

### Changed

- **Sign-in:** wrong passwords block the address they come from for that account (`MaxFailedSignIns`,
  `LockoutMinutes`), no longer the account itself: a stranger can't lock the administrator out. Unknown names are
  answered the same way.
- Behind a reverse proxy on the same computer, the visitor's own address counts for the sign-in limits, and https at
  the proxy marks the sign-in cookie secure.
- **Administration › System › Installation** needs a signed-in server administrator as well as the setup code.
- The setup code SFE makes itself is shown in its console and kept in its file, never written to the server's log;
  the update renews it.
- Searches, thumbnails, previews, video conversions and picture uploads are limited per account (or address): one
  visitor flooding them can no longer slow the server down for everyone.

### Fixed

- Files of a library that a browser could run as a page (`.html`, `.svg`, `.xml`…) are downloaded instead of opened
  in the app: such a file could act with the rights of whoever opened it. Every file is also served in a sandbox.
- Every page of the app carries a content security policy and refuses to be framed by other sites.
- The keys, setup code, databases and settings of SFE are never served as files of a library, even when a space's
  folder holds them or a folder link leads to them.
- ffmpeg can't be set to a program that isn't ffmpeg, or to one in a space's folder.
- The setup and update pages no longer tell visitors where the setup code file is once it is not needed.

## 1.46.0

### Added

- **Logs** (Administration › Server › Logs): every message, warning and error the server logs is kept and can be
  searched by text, severity, days and space; an entry opens on its error's details.
- New right **See the logs** (`logs.view`), given to the Administrators group of every space: a space's
  administrators see the entries of their space, server administrators those of the whole server.
- Settings (whole server) to choose how long log entries are kept (30 days by default) and to also save them to a
  file per day in the logs folder (`data/logs`, next to the database and the thumbnails; `Sfe:Logs:Directory`).

## 1.45.0

### Changed

- Administration: server administrators pick whether a settings page edits **This space** or the **Whole server**
  with tabs instead of radio buttons.
- Switching between **This space** and **Whole server** keeps the changes not saved yet in each (a dot marks the tab)
  instead of asking to forget them; **Save** saves both, **Discard** forgets both.
- Saving settings sends only the settings changed (new `PATCH api/settings` and `PATCH api/me/preferences`): a space or
  a user keeps following the server for everything else, even when the page was opened before the server changed.

## 1.44.0

### Added

- **Cinema mode** (C) in the viewer's video player: the video spans the whole width of the window, as YouTube's
  theater mode; the browser remembers the choice.

### Fixed

- A small video in the viewer no longer cuts off the player's buttons: the player is at least as wide as its controls,
  and on a phone the buttons that don't fit go to a second row.
- A video in cinema mode, or a picture in the viewer, no longer overflows below a title bar made taller by tags (which
  showed a scrollbar).
- Some folders showed a scrollbar with nothing to scroll: the page now always keeps the scrollbar's room.

## 1.43.0

### Added

- Setting SFE up needs no file edited: without a configured setup code, SFE makes one at its first start, shows it in
  its window (or log) and keeps it in `data/setup-code.txt`; type it with or without the dashes.
- The setup page now has three steps: the code, **your library** (pick its folder on the server's disks, choose this
  computer only or the whole network, and find ffmpeg), then the administrator.
- **Administration › System › Installation** changes the library folder, the network address and ffmpeg later, after
  entering the setup code. Values set by environment variables or the command line are shown locked.
- The Windows portable package opens the app in the browser when started, and installs itself as a Windows service with
  `install-service.cmd` (`Sfe.Api.exe --install-service`).

### Changed

- A configuration file that isn't valid JSON, or a port already in use, now stops SFE with a plain message (the file's
  line, what to do) instead of an error dump.
- A library folder that doesn't exist (an unplugged drive) is said as such in the listing, with a link to pick another.
- The repository's `appsettings.json` is neutral (library root `~`, SQLite); a machine's own values go in
  `appsettings.Local.json`.

## 1.42.0

### Added

- **Quick links** at the top of the sidebar: keep the folders and files you use most at hand, as in Windows Explorer.
  Drag folders, files or the selection onto it, or use the pin buttons (folder bar, folder cards, selection bar,
  viewer); drag the links to reorder them, or use **Edit** on phones. Each account has its own links in each library
  (right `account.quickLinks`, given to the Administrators and Users groups).

## 1.41.0

### Added

- The viewer shows a picture larger than the screen at its actual size (100%): click it, press `Z` or use the new
  button; drag it with the mouse or a finger to look around, scroll it with the wheel, and click again (or `Esc`) to
  see it whole.

## 1.40.0

### Changed

- Thumbnails and trickplay are stored in two levels of folders (`ab/cd/...`) instead of one folder per folder of the
  library, so the cache folder no longer holds hundreds of thousands of folders. The update moves the existing ones.

## 1.39.0

### Added

- Indexed folders show how many files and sub-folders the index has directly inside them, over their card or at the
  end of their line.
- Videos the browser can't play (AVI, WMV, HEVC...) are converted by the server with ffmpeg while they play, from where
  they were; seeking works across the whole video. **Administration › Playback** can convert every video, or none, and
  limits how many at once (right `files.stream`).
- **Administration › System › Video streams** lists the videos being converted, with who watches them (right
  `server.streams`).
- **Administration › System › About** shows the library's folder on the server and whether ffmpeg was found, where,
  and its version.

### Changed

- The search syntax help is now a dialog, grouped by theme and laid out for phones too; click an example to add it
  to the search, or press `?` to open it.
- Clicking your name in the ⋮ menu opens **Administration › My account**.
- The administration page shows breadcrumbs above each page's title, following the section and page open; on phones
  they lead back to the list of sections.

## 1.38.0

### Added

- Listings show files and folders that are not indexed yet faded, with a hint saying a scan will add them.

## 1.37.0

### Added

- Update page: after an upgrade, SFE stays closed until someone with the setup code confirms the update of its
  databases and settings. Nothing is changed before that, and the page lists every change since the installed version.
- Backups: a zip file of the databases and configuration files, from Administration › System › About or from the update
  page before updating (right `server.backup`).

### Fixed

- SQLite index: parallel work (a scan's folders, simultaneous requests) could share one database connection, which
  made a scan skip a folder now and then with "database is locked" or hang.

## 1.36.0

### Added

- Search by kind of file: `type:image`, `type:video`, `type:audio`, `type:other`, by the extension lists of
  Administration › Scanning › File types; several are either-or, and `-type:` excludes.
- Search results sort by **Duration** and **Resolution** (width × height). Files whose value is known come first, the
  others follow by path.
- The search box's syntax help lists `type:` and `d:`.

## 1.35.0

### Added

- Picture editor: the viewer's **Edit** button (or E) crops a picture, freely or to an aspect ratio, and saves the
  result as a new file, never changing the original.
- Rights `files.edit` and `settings.editor`, and a Picture editor settings page (output folder, quality).
- New rights now reach the built-in groups of existing spaces once, on the first start after an upgrade.

## 1.34.1

### Fixed

- A space's own administrator could add accounts from other spaces (or from none) to the space's groups. Only server
  administrators can do that now.

## 1.34.0

### Added

- Accounts, groups and rights, with a setup page that creates the first administrator, and a sign-in page.
- Guest access for visitors without an account.
- Several libraries (spaces) on one server, each with its own root, index and caches.
- Settings by level: server, library and personal preferences (My account), with locks.
- Links given to an external media player now expire.

## 1.33.0

### Added

- Durations of videos and audio files, read by scans: shown on tiles, rows and in the viewer, and searchable with `d:`
  (`d:>10m`, `d:1:30`).
