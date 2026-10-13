# Changelog

What each version of SFE added or fixed. What an upgrade changes for an existing install (database, settings, first
scan) is in [docs/upgrading.md](docs/upgrading.md).

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
