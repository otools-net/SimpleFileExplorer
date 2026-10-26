# Move and copy (file management, phase 4)

Code: `src/Sfe.Core/Files/FileJobs.cs` (jobs, `IFileJobAccess`), `Api/Security/FileJobAccess.cs`,
`Api/Endpoints/FileEndpoints.cs` (`api/files/jobs…`); client `app/files/` (`file-transfers.ts` service and clipboard,
`transfer.dialog.ts` picker and confirmations, `operations-panel.component.ts` panel and drops). Tests: `FileJobsTests`,
`FileJobRouteTests`, `transfer.spec.ts`. User docs: `docs/user-guide/managing-files.md#move-and-copy`.
Extractions are jobs of the same kind: [archive-extraction](archive-extraction.md).

## Decisions (2026-10-17)

- Every move or copy is a **background job** (one code path), even for one small file. Jobs live in the library's
  `FileJobs` singleton (memory only, last 50 finished kept): nothing in the database, nothing to migrate; a restart
  forgets them (a job cut off by a restart leaves what was done; a half-written copy file can remain).
- **Replace** needs the right to delete at the item replaced (`files.delete`, or `files.deletePermanently` when the
  space always deletes permanently); the replaced item goes to the trash (`Trash.MoveInAsync`). With `conflicts:
  replace` but no right, the job **asks** instead of failing.
- Copy tags: **asked each time** (`keepTags`, the dialog's checkbox, checked by default).

## How it works

- `POST api/files/jobs` (`FileJobRequest`: kind, paths, target, conflicts, keepAccess, keepTags) answers 202 with the
  first view; `GET api/files/jobs[/{id}]` (only the caller's; others 404), `POST …/{id}/answer` (409 when nothing is
  asked), `POST …/{id}/cancel`. `POST api/files/jobs/check` lists who would gain access (`CompareMoveAsync`, gains
  only; the Guests group is named "Visitors").
- Rights are checked **per item, when the job reaches it**, with the person's access then (`FileJobAccess` reads the
  account and rights again: disabled meanwhile = nothing more). Move: `files.move` at the item, its parent, the
  target, and `AllowsThroughout` for a folder. Copy: `files.copy` at the target, `files.view` at the item (a folder:
  its files), `files.view` at each sub-folder (else left out, reported). Not seen (`library.browse`) = "not found".
- Moves: `FileTree.Move` (rename; another drive: copy + delete), then `PathMoves`, inside `BeginMoveAsync` /
  `CompleteAsync` of the folder rules (`keepAccess` pins levels). Already in the target = skipped.
- Copies: streams with 1 MB buffers counting bytes; modified time kept; a cancelled or failed item deletes its partial
  file/folder. Links and work folders are never copied (reported). Free space: refused when the drive would keep less
  than `Files:MinimumFreeSpaceMegabytes`. Then the originals' thumbnail/trickplay caches are copied, files indexed at
  once (`IndexFilesAsync`), folders by a scan of each (`ScanCoordinator.TryStart`, skipped while another scan runs).
- Clash: a copy into its own folder always keeps both; otherwise the choice of the request, else the job waits
  (`Waiting`, `conflict` in the view) for `answer`; `forAll` keeps the answer for the next clashes.
- Scan gate: each item takes `ScanCoordinator.TryBeginOperation` (source and destination); a scan over it = that item
  fails with "a scan is running".

## Client

- `FileTransfers`: `pick` (dialog), `keep` (Ctrl+X/C), `paste`, `transfer` (paste/drop: a copy asks about tags, a move
  checks gains and asks only when some), `start`, polling every 700 ms while a followed job runs; at the end the
  listing drops moved items, reloads when the target is the folder listed, and toasts (with **Open**). `resume()` at
  page load follows jobs still running.
- Drops: `OperationsPanelComponent` listens on the document; targets are elements with `data-drop` (folder items,
  location crumbs). The drag data is `DRAG_ITEMS` (shared with the quick links, whose `effectAllowed` is now `all`).
  Ctrl at drop = copy.
- The picker is `FolderBrowser` + `<sfe-folder-browser>` (`files/folder-browser.component.ts`), shared with Extract…
  and Compress…: the dialog holds the state (`folderBrowser()`: target, listing, `allows(right)` from the folder's
  `access`), the component shows the way and the subfolders (`closedBecause`: a folder moved can't take itself).
- Not done: merging folders on a clash (only replace/skip/keep both), dropping onto a quick link, "new folder" inside
  the picker, cancelling SFE's own work on a file in use.
