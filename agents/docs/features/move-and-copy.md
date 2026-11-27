# Move and copy (file management, phase 4)

Code: `src/Sfe.Core/Files/FileJobs.cs` (jobs, `IFileJobAccess`), `Files/Jobs/` (`JobRun` answers, `LibraryClashes`,
`LibraryCopies` writing copies, `MergeTally`), `Api/Security/FileJobAccess.cs`,
`Api/Endpoints/FileEndpoints.cs` (`api/files/jobs…`); client `app/files/` (`file-transfers.ts` service and clipboard,
`transfer.dialog.ts` picker and confirmations, `operations-panel.component.ts` panel and drops). Tests: `FileJobsTests`,
`FileJobRouteTests`, `FolderMergeTests`, `transfer.spec.ts`. User docs:
`docs/user-guide/managing-files.md#move-and-copy`.
Extractions are jobs of the same kind: [archive-extraction](archive-extraction.md).

## Decisions (2026-10-17)

- Every move or copy is a **background job** (one code path), even for one small file. Jobs live in the library's
  `FileJobs` singleton (memory only, last 50 finished kept): nothing in the database, nothing to migrate; a restart
  forgets them (a job cut off by a restart leaves what was done; a half-written copy stays as `name.sfe-partial`).
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
- Copies: written as `name.sfe-partial` (a file or the whole folder) shown in listings with their progress, renamed
  when whole ([partial-files](partial-files.md)); streams with 1 MB buffers counting bytes; modified time kept; a
  cancelled or failed item deletes its partial file/folder; leftovers of a stopped job in the target are removed first. Links and work folders are never copied (reported). Free space: refused when the volume would keep less
  than `Files:MinimumFreeSpaceMegabytes` (the volume the target really is on: [storage-usage](storage-usage.md)). Then the originals' thumbnail/trickplay caches are copied, files indexed at
  once (`IndexFilesAsync`), folders by a scan of each (`ScanCoordinator.TryStart`, skipped while another scan runs).
- Disk refusals: an original the server's account may not read throws `UnreadableException` (an
  `UnauthorizedAccessException`, so every catch still takes it) from `LibraryCopies.OpenSource`; a copied folder
  rethrows it with the file's library path (`Inside`). `FileOperations.DiskFailure` then says "may not read it" /
  "may not read “path” in it" instead of "may not change files there", which only fits writes. Transfers do the
  same for library files read or folders listed (`LibraryTransferEndpoint.OpenReadAsync`, `LibraryDisk.Children`).
- Clash: see [Name clashes](#name-clashes).
- Scan gate: each item takes `ScanCoordinator.TryBeginOperation` (source and destination); a scan over it = that item
  fails with "a scan is running".

## Name clashes

`LibraryClashes.ClashAsync` (moves, copies, an extraction's top items, a compression's zip); transfers in
`TransferRun.NameAsync`.

- Before 4.1 (2026-11-24): **Replace** (what is there to the trash, or for good when the space always deletes
  permanently; needs the delete right at the item there, at the target, and throughout a folder; never what holds the
  item itself: moving X/X next to X), **Skip**, **Keep both** (`FileNames.KeepBoth`: ` (2)`). A folder replaced went
  whole to the trash. A copy into its own folder always keeps both. A name kept for SFE (the work folder) is refused.
- The choice of the request, else the job waits (`Waiting`, `conflict` in the view: both sides, `canReplace`,
  `canMerge`) for `answer`. Answering `replace` without the right gives a Forbidden result for that item.
- **Merge** (`ConflictChoice.Merge`, step 4.1): offered (`canMerge`) only between two folders, neither a link, the one
  there not holding the one coming, and with the job's right (move, copy, extract: `ClashCheck.MergeRight`) in the
  folder there. `answer` with `merge` when it can't: 409.
  - Moves and copies: `FileJobs.MergeAsync` runs each entry of the folder through `ItemAsync` with the folder there as
    target, so every rule of a single item holds (rights read again, scan gate, partial copies, a clash asked, merged
    again below); a copy leaves links out. One result for the folder (`MergeTally`): done, or the first problem's
    status (failed if any failed) with "N items were left out" and its reason; `newPath` the folder there, `merged`.
  - A moved folder emptied is removed (`Directory.Delete`, never recursive): its own folder rules
    (`TakeTreeAsync`, dropped: its sub-folders took theirs along, `keepAccess` pinned them), its index row and default
    tags (`PathMoves.ForgetMergedAsync`); settings, schedules and quick links naming it stay, as for a deleted folder.
    One still holding what was left out stays with its rules.
  - Extraction: `FileJobs.PlaceAsync` places a merged folder's entries recursively with the same questions.
  - Transfers: the name is kept; `CreateFolderAsync` of both sides returns the folder there, and each child is
    already asked. Moves inside a connection (provider moves, `PlaceConflict`) don't merge.
  - Uploads are files: `merge` behaves as `ask`.
- **For all** (`JobRun`): two answers, one for clashes that may merge (`canMerge`) and one for the rest, so "Replace
  for all" on files never replaces a folder that could be merged. A request's `replace` / `skip` / `keepBoth` fills
  both; its `merge` only the first (other clashes are asked).
- The access check before a move (`jobs/check`, `CompareMoveAsync`) compares the folder as if moved whole: for a merge
  it is an approximation (the items take the access of the folder there).
- Client: **Merge** first and primary when `canMerge` (Keep both primary otherwise); "Do the same for the next
  folders"; **Undo** after a move leaves out `merged` results (moving the folder there back would take what it held).

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
- Not done: merging folders inside a connection, dropping onto a quick link, "new folder" inside
  the picker, cancelling SFE's own work on a file in use.
