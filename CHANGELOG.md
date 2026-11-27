# Changelog

What each version of SFE added or fixed. What an upgrade changes for an existing install (database, settings, first
scan) is in [docs/upgrading.md](docs/upgrading.md).

## Unreleased

### Added

- **Merge folders**: when a folder is moved, copied, extracted or transferred where a folder of the same name is, the
  operations panel offers **Merge** besides Replace, Skip and Keep both: what the folder holds goes into the one
  there, each name they share asked in turn (files replaced, skipped or kept both; folders merged again). A moved
  folder is removed once emptied (its own folder rules and default tags with it); what was left out stays in it. Not
  offered into the folder holding the one coming, through links, or where you may not write. API: the conflict word
  `merge` (`conflicts` of a request answers folder clashes only), `canMerge` on a job's `conflict`, `merged` on an
  item's result; Sfe.Client: `SfeConflictChoice.Merge`, `SfeJobConflict.CanMerge`, `SfeJobItem.Merged`.
- **Previous versions** of a file: **Previous versions…** in a file's menu (and **Show…** in its Properties) lists
  the versions kept in the trash when it was saved over (text and picture editors), newest first: **Open** one in a
  new tab, **Restore a copy** next to the file, or **Restore** it over the file (the content it had becomes a version
  in turn). Whoever may open the file sees them; they follow the file when it is renamed or moved. API:
  `GET api/files/versions?path=`, `GET api/files/versions/{id}/content`, `POST api/files/versions/{id}/copy`,
  `POST api/files/versions/{id}/restore`.
- **Undo with Ctrl+Z**: Ctrl+Z (⌘Z on a Mac) undoes your latest action in the tab, and **Undo** in the ⋮ menu shows
  what it would undo: renames, deletes to the trash, moves, removed duplicates, and now also copies into the library and
  new folders and files (they go to the trash, unless they changed since). The tab keeps your last 20 actions; nothing
  is kept across reloads, tabs or devices. A rename made in a connection is undone only while that connection is open.
- **Notifications**: the messages shown in the corner of the page are kept by a bell at the top right (library and
  administration pages), newest first, with their buttons: **Open** goes to the folder items went to (also after a
  reload), **Undo** while the page is open. The bell counts what you haven't seen: warnings, errors and what came while
  the page was in the background. Each browser keeps its own for each account and space, the last 200 for 30 days.
  Messages now have a level (done, warning, error) shown by their colour.
- **Background tasks in the notifications**: a scan, trickplay check, similar-video search, trash purge or connection
  index you started by hand tells you when it ends (done, cancelled or failed), also when it ended while the library
  page was closed or in the background; those who may see the background tasks are also told of every failed run.
  **Open** shows the run with its log in Administration › Background tasks › History (which now opens on a run from
  its address, `run=`). API: the change feed's `task` event, and `since` on `GET api/files/changes` for the runs
  that ended meanwhile (the server keeps the last 50 in memory).

- **Uploads checked piece by piece**: each piece of an upload carries its SHA-256 (`Content-Digest: sha-256=:…:`,
  RFC 9530), and the server compares it before keeping the piece (library and connections): a piece damaged on the way
  is sent again instead of landing in the file. The page and Sfe.Client send it; a piece without it is taken as
  before. API: problem type `chunk-digest` (400).
- **Read only shown**: a folder you can open but not change shows a **Read only** chip next to its counts, whose hint
  says why (the server's or the space's switch, a visitor or guest account, your rights, a folder rule, a read-only
  link, a read-only disk, or the server not allowed to write there); a lock marks the files and folders inside a
  writable folder that you can't change (a folder rule, or a file the disk marks read-only). The disk is asked as it is
  now, once per folder (kept 30 s), without writing anything. API: `readOnly` on `GET api/browse` (a reason word),
  on its items (a file the disk marks read-only) and on `access` and its `children`; Sfe.Client: the same in
  `Raw` models.
- **Access in Properties**: the properties of a file or folder of the library have an **Access** section: what you
  may do with it (open, download, save over, add files to a folder, rename, move, move to the trash), each refused one
  with its reason (the server's or the space's switch, visitor, your rights, a folder rule, a read-only link, the disk
  read-only or refusing the server, a file the disk marks read-only), the folder the rule deciding there is set on,
  and what the disk allows. In a folder the disk refuses (or behind a read-only link), the menu entries writing there
  (Cut, Rename…, Move to…, Delete…, the archive's, New folder…, New file…, uploads, Paste) are greyed out with that
  reason. API: `access` on `GET api/files/properties` (`actions`, `ruleFolder`, `disk`), the reason word
  `fileReadOnly`; Sfe.Client: the same in `Raw` models.

### Changed

- **Uploads into a connection go on** after the page is reloaded or the server restarts, as the library's do: the
  operations panel lists them again and the same file picked or dropped sends the rest. The server keeps their
  records in `connection-uploads` beside the setup code (the provider's upload session protected by the server's
  keys); those idle for 24 hours, or of a connection removed, are removed every 10 minutes and at start.
- Unfinished uploads not touched for **Keep unfinished uploads for** are now removed every 10 minutes and at start,
  not only when someone starts or lists uploads, so their pieces don't stay on the disk.
- A "for all" answer to a name clash now holds apart for clashes of two folders that could be merged and for the
  others: answering **Replace** for every file no longer replaces such a folder without asking.
- **Undo** after a move leaves out the folders merged (moving them back would take what the folder there held).
- **The page loads less before showing**: the dialogs of the listing (tags, rename, properties, compress…) and the
  video player page are downloaded separately, right after the page shows or when first opened (first download
  1.08 MB → 944 kB, 253 → 231 kB compressed).

### Fixed

- A copy (or a transfer from the library) of a file the server's account may not read said it "may not change
  files there", sending you to check the target folder; it now says the server's account may not read the file,
  naming it when it is inside a copied folder.
- **Requests made with an access token** (Sfe.Client, scripts) answer several milliseconds sooner: the server's
  settings were worked out again twice for each of them. Many small files uploaded through Sfe.Client go about three
  times as fast (13 to 42 files a second on a test computer).
- Linux and macOS: the folders created above a folder of the server's secrets (when the data folder is new) were
  readable by the other accounts of the computer; they are now for the server's account only, like that folder.
- The free space kept by copies, moves, extractions, uploads and transfers is checked on the volume the files really
  go to: a folder that is a junction, a mounted folder or a link to another disk is measured on that disk, not on the
  drive letter of its path. Libraries on a network share are checked too (they weren't), and on Linux and in Docker
  the library's own file system is checked, not the system's root. An upload is also checked on its target folder's
  volume, not only where its parts are kept.

## Earlier versions

- 1.x (1.33.0 to 1.86.0): [changelog/1.x.md](changelog/1.x.md)
