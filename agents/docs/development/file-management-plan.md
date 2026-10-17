# File management: plan and progress (epic)

Code: `src/Sfe.Core/Files/` (built: `FileNames`, `WorkFolder`, `PathMoves`, `FileOperations`; planned: trash, uploads), `src/Sfe.Api/Endpoints/
FileEndpoints.cs`, client `src/app/files/` (dialogs, clipboard, operations panel) and `src/app/library/` (selection,
menus). Rules every operation must keep: [file-operations.md](file-operations.md). Folder rules:
[folder-access.md](../features/folder-access.md). Rights: [right-kinds.md](../features/right-kinds.md).

Goal: manage the library's files from the browser as in an OS file browser (Explorer, Finder, Nautilus): new folder,
rename, move, copy, delete to a trash, upload, download several, properties, with cut/copy/paste, drag and drop,
context menus and keys, on Windows and Linux servers, without breaking existing installs.

## How to use this file

- Built over several sessions: **update it in the commit that does the work** (tick `[x]`, add the version, move
  findings into the phase), so the next session starts here. One phase = one commit = one minor version.
- New requirements or gotchas found while building go in [Found while building](#found-while-building) (or straight
  into the phase they belong to); decided questions move from [Open questions](#open-questions) to
  [Decisions](#decisions).
- When a phase is done, its lasting facts go into a feature note (`agents/docs/features/<topic>.md`, one per function,
  indexed in `AGENTS.md`); this file keeps only the checklist and a one-line pointer.
- Reference project (inspiration only, never copied): `C:\workspace\3dparty\filebrowser` (Go + Vue). What is worth
  knowing from it is noted under each phase.

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## Decisions

- Rights per operation, all `Kind = Write`, `AtFolder = Write`, `GuestAllowed = false`, `DefaultGroups = []`, with
  `since` (nobody gets them by upgrade); `folder:write` and the space's **Files read-only** switch them off
  ([file-operations.md](file-operations.md#rights-and-checks)).
- Folder rules follow folders renamed or moved through SFE with `FolderRuleService.BeginMoveAsync` → disk →
  `CompleteAsync`/`AbortAsync` (built in 1.52.0).
- Every operation is a Core service called with the canonical root-relative paths and the subject's `FolderPolicy`;
  routes only check rights, parse and map results (as `DuplicateRemover`/`EditedImages`).
- New names never overwrite by accident: create with `FileMode.CreateNew` / fail when the target exists, so a target
  appearing between check and act is a conflict, not a loss.
- "Keep both" names follow the editor's pattern: `name (2).ext`, `name (3).ext`… (folders: `name (2)`).
- **Always delete permanently** (user's request, 2026-10-17): setting `Files:DeletePermanently`, **off by default**,
  per library (server default + library level, [settings-levels.md](../features/settings-levels.md)). On: every delete
  skips the trash (also what a **Replace** removes), the confirmation says "permanently, can't be undone", no Undo
  toast, and deleting needs `files.deletePermanently` (otherwise a user trusted only with "delete to the trash" could
  destroy files). Turning it on leaves what is already in the trash (restore and purge still work); turning it off
  needs nothing. Off is today's safe behaviour, so existing installs change nothing.
- **Work folder** (question 2, decided 2026-10-17): a **library setting** `Files:WorkFolder`, `Library` (default:
  `.sfe` at the root, hidden) or `Server` (`{CacheDirectory}/work`); the name `.sfe` at the root is reserved either
  way. Built in 1.55.0: [work-folder.md](../features/work-folder.md).
- **Names on Linux** (question 3): `Files:PortableNames`, per library, on by default (built in 1.55.0,
  [file-names.md](../features/file-names.md)).
- **Scan running** (question 4): **refused with 409** (`scan-running`) while a scan runs over an overlapping tree; a
  scan starting meanwhile waits for the operations on its tree (`ScanCoordinator.TryBeginOperation`, 1.55.0).
- Each phase adds only the rights and settings it uses (not all in phase 0): the group editor and the settings pages
  would otherwise offer functions that don't exist yet.

## Open questions

Decide at the start of the phase that needs them (recommended answer first).

1. **Trash** (phase 3): (a) SFE's own trash per library, a hidden work folder at the root (`.sfe/trash/`, same volume
   as most of the library, so deleting is a rename), with a table of what was deleted, by whom, from where; restore,
   empty, purge after `Files:Trash:RetentionDays`; or (b) the OS trash (Windows Recycle Bin of the service account,
   freedesktop `Trash` on Linux: per user, absent on network shares, invisible to other SFE users); or (c) no trash,
   permanent delete with a confirmation. Recommended: (a); a folder on another volume (mount point, junction target)
   falls back to "can't go to the trash: delete permanently?". Whatever is chosen, `Files:DeletePermanently` (see
   [Decisions](#decisions)) switches the trash off for a library.
2. ~~Work folder~~, 3. ~~Names on Linux~~, 4. ~~Scan running~~: decided, see [Decisions](#decisions).
5. **Big operations** (phase 4): every move/copy/delete as a background job (`TaskRuns`, progress, cancel) or only
   above a size. Recommended: one code path, a job per request; the client waits inline for small ones (a second)
   and shows the operations panel otherwise.
6. **Replace** (phase 4): a separate right or "replace needs `files.delete` at the target". Recommended: needs
   `files.delete` (replacing destroys data), the replaced file goes to the trash (with `Files:DeletePermanently` on:
   it is deleted, and replacing needs `files.deletePermanently`).
7. **Links inside the library** (phases 3–4): deleting or moving a symbolic link/junction acts on the link only;
   copying a folder copies links as links (Windows needs a privilege for symlinks) or skips them with a report.
   Recommended: never follow a link while copying or deleting; report skipped links.

## Cross-platform requirements (Windows and Linux)

Check each against both systems in the phase that touches it; tests run on Windows here, so Linux behaviour is
covered by unit tests of the pure logic plus the portable AppImage smoke test.

- **Names:** refuse empty, `.`, `..`, `/`, NUL, control characters everywhere; on Windows (and with portable names)
  also `< > : " \ | ? *`, trailing dot or space, reserved names with or without extension (`CON`, `PRN`, `AUX`, `NUL`,
  `COM0-9`, `LPT0-9`, also `COM¹²³`), alternate data streams (`name:stream`); a byte length limit per segment (255 on
  most Linux file systems, 255 UTF-16 units on NTFS).
- **Case:** comparisons through `LibraryPaths.PathComparer`. On Windows `photo.jpg` and `Photo.jpg` are the same file:
  a case-only rename is allowed and may need a two-step rename through a temporary name (`Directory.Move` refuses
  same-path on some versions); on Linux they are two files and both can exist (a clash check must not merge them).
- **Moves across volumes:** `File.Move` copies across volumes, `Directory.Move` throws (EXDEV on Linux,
  `IOException` on Windows): folders need SFE's own copy-then-delete with progress, cancel and cleanup of a partial
  copy; mount points and junction targets inside the library count as other volumes.
- **Locked files (Windows):** a file open in a player, a stream, ffmpeg (thumbnail, trickplay) or another program
  can't be renamed or deleted: cancel SFE's own work on that file first (thumbnail job, trickplay, stream), retry
  briefly, then answer which file is in use. Linux allows it (the open handle keeps the old inode).
- **Permissions:** the service account must be able to write: Windows docs grant `NT SERVICE\SFE` only `RX` on the
  media folder today, the systemd example has `ProtectSystem=strict` with `ReadWritePaths` commented out. "Access
  denied" answers say the server's account may not write there. New files and folders get the system's defaults
  (umask, inherited ACLs); copies don't try to keep owners.
- **Long paths:** .NET handles long paths, but the index caps `FullName` at 260 (`SfeDbContext.MaxPathLength`):
  refuse an operation whose indexed rows would get longer, or keep them as missing files; say which.
- **Timestamps:** the listing cache uses the folder's last write time (FAT 2 s, some network shares don't update it):
  invalidate `LibraryBrowser`'s snapshot of every folder an operation touched instead of relying on it.
- **Unicode:** keep names as sent (no normalisation); NFC/NFD twins are two files on Linux, one on macOS.
- **Free space:** check `DriveInfo` of the target before a copy or upload and keep a margin (setting).
- **Hidden items:** dot files on Linux, the hidden attribute on Windows: new names starting with `.` are allowed but
  disappear unless "show hidden items" is on; say so.

## Phases

### Phase 0 — Foundations (shipped with phase 1 in 1.55.0)

- [x] (1.55.0) `Core/Files/FileNames`: `Check(name, portable)`, `KeepBoth`, per-library `Files:PortableNames`;
      `FileNamesTests` ([file-names.md](../features/file-names.md)).
- [x] (1.55.0) Work folder: setting `Files:WorkFolder` (`Library` = `.sfe` at the root, hidden; `Server` =
      `{CacheDirectory}/work`), `WorkFolder.Get` on demand; `.sfe` at the root refused by `LibraryPaths.TryResolve`
      (8.3 name too), left out of listings, siblings, covers and scans even with `ShowHiddenItems`
      ([work-folder.md](../features/work-folder.md)). Nothing writes in it yet (phases 3, 5).
- [x] (1.55.0) `Core/Files/PathMoves`: index rows (one `ExecuteUpdate` per table and tree, both providers, ids kept),
      quick links, `Scan:ExcludedFolders`, `Trickplay:Folders`, `Editor:OutputFolder`, schedules' `Path`, thumbnail and
      trickplay caches (folders in the background), tag counts, listings ([path-moves.md](../features/path-moves.md)).
      Moves to another parent are handled for index rows (`FolderIdAsync`) but not used before phase 4.
- [x] (1.55.0) Destination checks in `FileEndpoints`: `FolderChecks.Check` (canonical + real path), the parent too for
      a rename, `AllowsThroughout` for a folder, real path not in `ServerFiles` nor a work folder (`Protected`), "exists"
      for any clash. Phase 4 must check the **destination parent** the same way (`Protected` + `Check`).
- [x] (1.55.0) `LibraryBrowser.Invalidate(folder)` (listings and covers), used by `PathMoves` and new folders.
- [x] (1.55.0) Logging: `LogInformation` with the account for every change; `LogSecurityWarning` for an attempt on an
      existing place the caller doesn't see, or on the server's files through a link (`FileEndpoints.Refused`).
- [~] Rights: `files.createFolders`, `files.rename` (+ `settings.files`) in 1.55.0 (`since: 5`); the others
      (`files.upload`, `files.move`, `files.copy`, `files.delete`, `files.deletePermanently`) come with their phase
      (decision above). `RightCatalogTests`, `AccessTests` (switching off rights changing files) and the upgrade test
      in `EditorTests` list them.
- [~] Settings: `Files:PortableNames`, `Files:WorkFolder` (tab **File management**, right `settings.files`) in 1.55.0;
      `DeletePermanently`, `Trash:RetentionDays`, `Uploads:*`, `MinimumFreeSpace` with their phases.
- [x] Decided open questions 2, 3, 4 (see [Decisions](#decisions)).

### Phase 1 — New folder and rename (built in 1.55.0)

Lasting facts: [new-folder-and-rename.md](../features/new-folder-and-rename.md).

- [x] (1.55.0) Core `FileOperations.CreateFolderAsync(parent, name, actor)` and `RenameAsync(path, name, actor)`: the
      routes check the rights and folder rules (not Core, as `EditedImages`/`DuplicateRemover`); names, clash,
      case-only rename (through a temporary name), `BeginMoveAsync` → `Directory.Move` → `PathMoves` → `CompleteAsync`
      (abort on a disk error), retries on a sharing violation, index path-length check (SQL Server).
- [x] Rules' comparison: a rename keeps the parent, so no confirmation (moves only).
- [x] (1.55.0) Routes `POST api/files/folders`, `POST api/files/rename`; 409 problem types `file-exists`,
      `scan-running`, `folder-rules`; disk refusals are 500 with a message (never a server path).
- [x] (1.55.0) Client: item menu (right click when it has file actions, Shift = the browser's; ⋮ button; long press =
      the browser's `contextmenu`), **New folder** button in the location head and **Shift+N**, **Rename** (F2) dialog
      selecting up to the extension, the listing updated in place and the item highlighted (the selection holds
      files only until phase 2).
- [x] (1.55.0) Tests: `FileNamesTests`, `FileOperationsTests` (index rows, tags, settings, schedules, caches, case-only,
      scan gate, locked files on Windows, work folder), `FileRouteTests` (rights, read-only/hidden folders, names,
      paths outside, conflicts, rules and quick links moved, work folder, a junction to the keys folder),
      `files.spec.ts` + store and location specs.
- [x] (1.55.0) Docs: `docs/user-guide/managing-files.md`, `docs/administration/file-management.md`, rights, upgrading,
      installation (write access), configuration; screenshots `item-menu`, `rename-dialog`, `new-folder-dialog`,
      `admin-files`.
- Not done: cancelling SFE's own work on a file in use (thumbnail job, trickplay, stream) before renaming it; only
  retries. Revisit with moves and deletes (phase 3–4), where it matters more.
- Reference: filebrowser renames through a `PATCH` with `action=rename`; we keep one route per operation with a JSON
  body ([api-routes.md](api-routes.md)).

### Phase 2 — Selecting folders and keys (built in 1.56.0)

Lasting facts: [selection-and-keys.md](../features/selection-and-keys.md). Clipboard and drag and drop moved to
phase 4 (decided 2026-10-17: without moves and copies, Cut/Copy/Paste and drops would have nothing to do).

- [x] (1.56.0) The selection holds folders too (`selectedItems`; `selectedFiles` for tagging); ranges span folders and
      files; Ctrl+A takes both; a click on a folder while selecting toggles it; the bar counts both and offers Rename
      for one item; quick links take both.
- [x] (1.56.0) Keys: arrows (geometric up/down in grids), Home/End, Space/Ctrl+Space, Shift+arrows, once an item has
      the focus (else the page scrolls as before); Enter opens (the link's own); Backspace/Alt+↑, F2, Shift+N were
      there. Delete/Shift+Delete come with phase 3, Ctrl+X/C/V with phase 4. Listed in the search help.
- [x] (1.56.0) Tests: store, selection bar, folder item, `listing-keys.spec.ts`, page keys in `files.spec.ts`.

### Phase 3 — Delete, trash and restore (built in 1.57.0)

Lasting facts: [trash.md](../features/trash.md).

- [x] Decided questions 1 (SFE's own trash) and 7 (links acted on as links), plus: a trashed folder's rules and its
      files' tags are kept in the trash entry for a restore (2026-10-17).
- [x] (1.57.0) `Files:DeletePermanently` (off by default) forces permanent deletes, then needing
      `files.deletePermanently`; `api/config` tells the client (`files`); tested on the route.
- [x] (1.57.0) Core `FileOperations.DeleteAsync` + `Trash` (entry first, disk, tags/rows, rules); links deleted as
      links; other drive: copy then delete, refused for folders holding links (`trashUnavailable`).
- [x] (1.57.0) Tables `TrashEntries`, `TrashTags` (SQL Server migration `AddTrash`, SQLite created by the
      initializer; upgrade test `AnIndexFromBeforeTheTrashGetsItsTables`).
- [x] (1.57.0) Trash page: Administration › File management › Trash (restore, delete, empty, load more).
- [x] (1.57.0) Purge task `TrashPurge` (schedulable, run history, also automatic when the trash is used).
- [x] (1.57.0) Client: Delete key / menu / selection bar, confirmation with count and size, Shift+Delete, Undo toast.
- [x] (1.57.0) Tests: `TrashTests`, `TrashRouteTests`, `delete.spec.ts`.
- Not done: Replace (phase 4) must send what it replaces to the trash through `Trash.MoveInAsync`.

### Phase 4 — Move and copy

- [ ] Decide open questions 5 and 6.
- [ ] (from phase 2) Clipboard store (cut/copy, source space and paths, survives navigation, cleared on paste after
      cut), Ctrl+X/C/V, "Paste" in the folder's menu and the location bar; cut items look faded.
- [ ] (from phase 2) Drag and drop inside the app: items onto a folder, a breadcrumb, a quick link (moves; Ctrl =
      copy, as in Explorer); the drop target highlights; the existing drag to the quick-links sidebar keeps working
      (`QuickLinksService.dragPaths` already carries every selected item).
- [ ] Core `MoveAsync`/`CopyAsync(policy, paths, targetFolder, conflicts)` as a background job carrying the subject
      (`AccessSubject`), re-evaluating the policy per item when it runs; moves: Write at source and target,
      `AllowsThroughout` for folders, `CompareMoveAsync` → confirmation naming who gains access, `keepAccess`; copies:
      Read at source (hidden sub-folders are left out, not copied), Write at target; refuse into itself, the same
      place, the work folder; same volume = rename, else copy-then-delete with cleanup of a partial copy.
- [ ] Conflicts: per item **Replace** (needs `files.delete`, replaced item to the trash), **Skip**, **Keep both**, "do
      the same for the rest"; folders merge (ask per file inside) or keep both; show sizes and dates of both sides.
- [ ] Index and caches: moves through `PathMoves`; copies index the new files (`IndexFilesAsync` in batches) and copy
      the thumbnail/trickplay caches instead of regenerating; copies don't copy tags (decide: Explorer-like = no tags,
      or ask).
- [ ] Free space check before a copy; progress (items, bytes), cancel, a final report (done, skipped, failed with why).
- [ ] Client: **Move to…** / **Copy to…** with a library folder picker (only folders where one may write, new folder
      inside it), paste of the clipboard, drop targets of phase 2, the operations panel (running jobs, progress, cancel,
      results, links to the target).
- [ ] Tests: same and other volume (a second temp folder or a junction/mount on another drive where available),
      cancel mid-copy leaves no partial file, rules moved with a folder, gains confirmation, a scan running at the same
      time, conflicts of each kind, case-only clash on Windows vs two files on Linux (pure logic), quota/free space.
- Reference: filebrowser moves with `Rename` and falls back to copy + `RemoveAll`; its conflict prompt shows origin
  and destination per file with checkboxes. Ours: a job with progress and a report.

### Phase 5 — Upload

- [ ] Resumable chunked uploads (own small protocol, not a full tus server): `POST` start (target, name, size,
      conflict choice) → id; `PUT` chunks with offsets; `HEAD`/`GET` status to resume; finish = rename from
      `.sfe/uploads/<id>.part` (same volume) with `CreateNew`; abandoned parts removed after a while. Chunks keep each
      request under proxy limits (nginx 1 MB default, Cloudflare 100 MB) and Kestrel's 30 MB.
- [ ] Limits: max file size, free-space margin, per-caller concurrent uploads (`RequestLimits`), total per request;
      names validated per file; folder uploads (`webkitdirectory`, dropped folders) create sub-folders, each relative
      path checked segment by segment (no `..`, no zip-slip-like escape), Write checked at each created folder.
- [ ] Index each finished file (`IndexFilesAsync`), invalidate the listing, thumbnails come lazily.
- [ ] Client: **Upload files** / **Upload folder** buttons, drop from the desktop onto the listing or a folder,
      an uploads panel (per file progress, speed, pause/resume, retry, cancel), conflicts asked once with "for all";
      warn before leaving the page while uploading.
- [ ] Tests: chunk order, resume after a dropped connection, wrong offset, size mismatch, cancel cleans up, conflict
      choices, folder upload escaping attempts, limits, rights and folder rules, a hidden clash answers "exists".
- Reference: filebrowser uses tus (`POST` create, `PATCH` chunks, `HEAD` offset), drains rejected chunk bodies so the
  browser sees the status instead of a reset, and removes abandoned uploads on cache eviction. Worth keeping: drain
  refused chunk bodies, `fsync` before finishing.

### Phase 6 — Download several files or a folder

- [ ] `GET api/files/archive?paths=…` (or a POST returning a one-time link, as backups do): a zip streamed while it
      is written (`ZipArchive` on the response, no temp file, store-only for media), right `files.download`, Read at
      each item, hidden sub-folders left out, links not followed outside the root, `ServerFiles` excluded, entry names
      checked (no `..`, no absolute paths); per-caller limit; name `<folder>.zip` with a safe `Content-Disposition`.
- [ ] Client: **Download** on a selection of several items or a folder.
- [ ] Tests: content of the zip, hidden folder absent, a link to the data folder absent, cancel by the client stops
      the work.
- Reference: filebrowser offers zip and several tar formats; zip alone is enough here.

### Phase 7 — Properties

- [ ] Item properties: full name, kind, size, dates (created, modified), path (with `files.copyPaths`), folder:
      count of files and folders and total size (from the index when the folder is indexed, else a background count
      with a cap and cancel), media data already in the index, tags.
- [ ] On demand: checksum (SHA-256) of one file, limited per caller.
- [ ] Client: **Properties** (Alt+Enter) in the menu, a side sheet.

### Phase 8 — Comfort

- [ ] Undo of the last rename/move (toast), restore covers deletes.
- [ ] Batch rename of a selection (pattern with counter, find/replace, preview before applying).
- [ ] New empty file / new text file (decide whether a text editor follows).
- [ ] Other tabs and users see changes: refresh the listing when it comes back into view, or a change feed
      (server-sent events from the operations, not a file-system watcher over millions of files).
- [ ] Phone layout: action sheet for the menu, long press to select, drag replaced by Move to….

### Later / optional (not planned yet)

- [ ] Text file editor (filebrowser has one), with its own right.
- [ ] Compress to zip / extract archives into a folder (zip-slip checks, size bomb limits).
- [ ] Shared links that accept uploads (a drop box), expiring.
- [ ] Schedules that change files (needs `TaskSchedule.CreatedById`, [file-operations.md](file-operations.md#background-jobs)).
- [ ] File operations from the duplicates page (move instead of delete) and the missing-files page.

## Security checklist (every phase)

- Every new route: its right, `.FolderScoped(...)`, the route test of `AccessTests`, 404 for unseen paths, 403 for
  seen but not allowed, no server path in errors (audit #10).
- Paths: `..`, encoded separators, `\`, `C:`, `::$DATA`, `CON`, trailing dots/spaces, NUL, long names, case variants
  and 8.3 names of a hidden folder, links and junctions from a writable folder into a hidden one, out of the root, or
  onto the data folder (writing through them), the work folder.
- Names with `<script>`, quotes, RTL override characters shown in dialogs, toasts and logs.
- CSRF: every write is a POST/PUT/DELETE with `X-SFE-Request` (already enforced); no GET changes anything.
- DoS: per-caller limits on uploads, archives, checksums, folder sizes; a delete or copy of the root refused.
- Folder rules: an operation never reveals a hidden file (clash = "exists"), never moves or deletes a folder with a
  stricter rule below, never lets someone give themselves access by moving a folder (gains confirmation is for those
  allowed to move; the move itself needs Write at both ends).

## Found while building

Add here, dated, what the phases didn't foresee (then move it into the phase it changes).

- 2026-10-17: the selection was files only (`ListingStore.selection`, `selectedFiles()`): extended in 1.56.0.
- 2026-10-17: the Windows installation docs grant the service account read-only access (`RX`) to the media folder,
  and the systemd example keeps `ReadWritePaths` commented: file management needs write access (docs in phase 1,
  and an answer that says so when the disk refuses).
- 2026-10-17: thumbnail and trickplay caches (with the video fingerprint) are keyed by full path: moves must move
  them (as `RelinkService.MoveCache`) or similar videos lose their fingerprints until the next pass.
- 2026-10-17: Chrome and Edge keep **Ctrl+Shift+N** (private window) from the page: New folder is **Shift+N** (Ctrl+Shift+N
  still works where the browser lets it through).
- 2026-10-17: a right click on every item would take the browser's menu ("open in a new tab") from everyone: the item
  menu opens only on items with a file action, and Shift+right click always gives the browser's menu.
- 2026-10-17: `FolderRuleService` and `QuickLinkService` live in the application's container: `TenantHost` now hands
  them to each library (like `SettingLayers`), so Core's file services can use them.
- 2026-10-17: offset paging of a folder changed between pages: `ListingStore.accept` drops repeated items; a rename to a
  name sorting later can still skip one item until a reload. Phase 8's change feed or a reload after big operations
  covers the rest.
- 2026-10-17: the initial bundle is over its 600 kB warning (631 kB with the menu and dialog): lazy-load the file
  dialogs if it grows further.
