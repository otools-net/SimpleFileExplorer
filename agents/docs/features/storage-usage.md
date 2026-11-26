# Space used and free

Code: `Core/Files/DiskSpace.cs`, `StorageUsage.cs`, `TrashSize.cs`, `Trash.SizeAsync`;
`Sfe.Api/Endpoints/StorageEndpoints.cs`, `StorageUsageCache.cs`, `FileEndpoints.ListTrash`;
`src/Sfe.Web/src/app/library/sidebar/storage-bar.component.ts`, `storage-texts.ts`, `files/trash-texts.ts`.

## Routes

- `GET api/v1/storage` (right `library.browse`): the disk holding the library's folder. Numbers only: the path is
  never sent (space administrators don't see the server's folders).
- `GET api/v1/connections/{id}/storage` (`connections.use`, opened as the browse route: 404 / 403 / 409 the same):
  the provider's `IRemoteStorage.SpaceAsync`, only when its capability `FreeSpace` says so, else `reported: false`
  without calling it. Dropbox gives `used` and, for individual and team allocations, `total`.
- Answer `StorageUsage(reported, used, total, free)`; `total` / `free` null when a provider gives only `used`.
- `StorageUsageCache` (singleton) keeps each answer two minutes, keyed `library:<root>` / `connection:<id>`: every
  session of a space shares one read; a provider counts calls. The library's read runs on the thread pool with a
  5-second wait: a share that doesn't answer is "not reported", the request not held.

## Measuring a disk

- Windows: `GetDiskFreeSpaceEx` on the folder (with a trailing separator). `DriveInfo` wants a drive letter and throws
  on `\\server\share`; this takes shares and folders a drive is mounted on. Free = available to the caller (quotas).
- Elsewhere: `new DriveInfo(folder)` on the folder itself. On Unix .NET passes the name to `statvfs`, which answers
  for the file system holding it (a Docker volume on `/data`), where `Path.GetPathRoot` would give `/`.
- Both follow junctions, mounted folders and links to the volume they lead to.
- The free-space margins (`Files:MinimumFreeSpaceMegabytes`) of copies, moves, extractions, compressions, uploads and
  transfers all read `DiskSpace.FreeBytes(path)`: the closest folder that exists (the path, a file's folder, or a
  parent of folders not made yet), measured as above. Never `DriveInfo(Path.GetPathRoot(...))`: the drive letter of
  the path is the wrong volume behind a junction, throws on a share (check skipped) and is `/` on Linux.
- An upload checks two volumes at start: the work folder holding its parts and its target folder (the part is moved
  there at the end, a copy when they differ). Tests: `DiskSpaceTests` (a junction to another fixed drive; inconclusive
  when the machine has only one).

## The sidebar's bar

- In the sidebar's footer, above Manage tags and Trash. The place is `ConfigService.place()` (null = library); read
  again when its id changes, then every 5 minutes while `document.visibilityState` is visible (the interval does
  nothing when hidden; coming back after 5 minutes reads at once).
- Orange (`--accent`) from 85 % used, red (`--danger`) from 95 % (`NEARLY_FULL`, `FULL` in `storage-texts.ts`). No bar
  without a total; hidden when the read fails (a connection to sign in to again: its listing says so).
- Texts say **disk** for the library's storage (glossary in [ui-texts](../development/ui-texts.md)), never "drive".

## The trash's total

- `TrashPage.Size` (`TrashSize(bytes, unknown)`): `SUM(Size)` and a count of null sizes, two queries whatever the
  trash's length; given on the first page only, to those who see every entry (`SeesWholeTrash`: server
  administrators, or `tenant.members` with no folder rules) — a total of entries someone can't see would tell about
  them. Sizes are what the index knew at deletion (`TrashEntry.Size`); not indexed = `unknown`.
- The page subtracts an entry restored or deleted for good from the total; emptying reloads it.

## Not done

- No per-user quota (owner's choice). `Sfe.Client` leaves the routes to `Raw` (6.1's parity matrix decides).
