# Delete and the trash (file management, phase 3)

Code: `src/Sfe.Core/Files/Trash.cs`, `FileTree.cs`, `FileOperations.DeleteAsync`, `Data/Entities/Trash/`
(`TrashEntries`, `TrashTags`), `FolderRuleService.TakeTreeAsync`/`PutBackAsync`, `BackgroundTaskKind.TrashPurge`;
`Api/Endpoints/FileEndpoints.cs` (delete, trash routes); client `app/files/` (`delete.dialog.ts`, `file-actions.ts`,
`trash-section.component.ts`), `ListingStore.applyRemoved`, `ToastService` actions. Tests: `TrashTests`,
`TrashRouteTests`, `delete.spec.ts`. User docs: `docs/user-guide/managing-files.md`, `docs/administration/file-management.md`.

## Decisions (2026-10-17)

- SFE's own trash per library in the work folder ([work-folder.md](work-folder.md)): `trash/{guid}/{name}`; the entry
  stores its `Location`, so changing `Files:WorkFolder` later doesn't lose it.
- Links are acted on as links (deleted, trashed, moved), never followed.
- A trashed folder's **folder rules** leave `FolderRules` (`TakeTreeAsync`) into `TrashEntries.Rules` (JSON) and come
  back on restore (`PutBackAsync`, dropping principals gone meanwhile): no stale rule applies to a new folder of that
  name meanwhile. Its **default tags** do the same through `TrashEntries.DefaultTags` ([default-tags.md](default-tags.md)).
- **Tags** of trashed files go to `TrashTags` (path inside the item); index rows are deleted, **not** recorded as
  missing files.

## How it works

- Order: entry row first, then the disk (`FileTree.Move`), then tags/rows, then rules. A disk failure deletes the
  entry; a crash after the entry leaves one to purge, never a lost file.
- `FileTree`: `Move` renames, or for a folder on another drive copies then deletes (`MoveByCopy`; .NET refuses a
  folder move across roots before asking the system, Windows says 17, Linux EXDEV 18). A folder holding links is never
  copied (`LinksInsideException` → `trashUnavailable`, the client offers a permanent delete). `Delete` walks itself:
  **.NET 10's recursive `Directory.Delete` fails with "access denied" on a folder holding a junction** (target safe).
  Enumerations use `AttributesToSkip = 0` (the default skips hidden and system files).
- Permanent delete: disk, index rows, rules taken and dropped, cached previews deleted in the background
  (`CacheDeletes`). Purge walks the trashed item to delete the previews of its original paths.
- Restore: parent made again if gone, `KeepBoth` on a clash, rules and tags back (tagged files indexed at once), then
  `ScanCoordinator.TryStart(folder, Automatic)` indexes the rest of a folder.
- Purge: `TryStartPurge` (task `TrashPurge`, schedulable, `TaskRuns`); also started by itself when the trash is used,
  at most every 12 h (`PurgeWhenDue`); `RetentionDays` 0 = never.
- Routes: `POST api/files/delete {paths, permanent}` → a result per path (`done` + `trashId`, `notFound`, `forbidden`,
  `scanRunning`, `trashUnavailable`, `failed`); rights at the item, its parent and throughout a folder, as renames.
  `Files:DeletePermanently` forces permanent (then `files.deletePermanently` is needed).
- Who sees an entry: server administrators, whoever deleted it, those who may delete in its parent; an entry holding
  folder rules only to `tenant.members` (its own rules may have hidden it from people allowed in the parent).
- Client: `FileActions` (rights), `DeleteDialog` (asks, applies `applyRemoved`, Undo toast restores then reloads at the
  same position, offers permanent for `trashUnavailable`), `applyRemoved` shifts the next page's offset back.
- Shortcut: **Trash** at the bottom of the library's sidebar (`SidebarComponent`, shown with `files.delete` or
  `files.deletePermanently`, like the admin sub-page) links to `admin?section=files&page=trash`. Its icon is `trash`
  when empty, `trash-full` with a badge otherwise, from `GET api/files/trash/count` → `{ count, more }`
  (`Trash.CountAsync`: what `SeesInTrash` lets through, read newest first in batches of 200, stopping past
  `Trash.CountCap` = 99, shown "99+"). `SidebarService.trash`, read on init and again (300 ms debounce) on the feed's
  `trash` event.
- `Trash.EntriesChanged` fires after an entry comes in (`MoveInAsync`, `CopyInAsync`) or leaves (`RestoreAsync`,
  `PurgeAsync`, so also Empty and the purge task); not for `ForgetAsync` (a failed delete, nothing told yet).
