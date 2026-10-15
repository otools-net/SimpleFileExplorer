# Changelog

What each version of SFE added or fixed. What an upgrade changes for an existing install (database, settings, first
scan) is in [docs/upgrading.md](docs/upgrading.md).

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
