# Item access: read-only or writable shown

Code: `src/Sfe.Api/Endpoints/ListingAccess.cs` (`SfeReason`, `DiskReason`, `AccessView`), `ItemAccessReport.cs`
(Properties), `FolderPolicy.RuleFolder`,
`src/Sfe.Core/Files/DiskWrites.cs` (`DiskWrite`), `src/Sfe.Core/Library/ReadOnlyReason.cs`,
`EffectiveAccess.SpaceDisabled` (`RightsEvaluator`), `LibraryBrowser.ToItem` (`LibraryItem.ReadOnly`); client
`library/read-only.ts` (`readOnlyReason`, `readOnlyText`, `itemLock`), `LockMarkComponent` (`items/item-parts.ts`),
the chip in `location/location.component.html`, `.read-only-chip` (`styles/location.css`), `.lock-mark`
(`styles/items.css`); `placeRefusal` with `ItemCommands.onDisk` and `FolderMenu.onDisk` (menus);
`files/access-texts.ts` and `files/item-access.component.*` (the Access section, `styles/item-access.css`).
Connections: `ConnectionAccessReport`, `RemoteReadOnlyFolders`, `ConnectionFileServices.ProviderRefusesAsync`,
`ConnectionFolders.RuleFolder` / `ChangesFiles`, `RemoteItem.ReadOnly`, `ProviderCapabilities.ReadOnlyItems`,
`UsableConnection.ReadOnly`. Tests: `ListingReadOnlyRouteTests`, `ItemAccessRouteTests`,
`ConnectionReadOnlyRouteTests`, `FolderPolicyTests.TheFolderARuleIsSetOnIsTold`, `DiskWritesTests`,
`read-only.spec.ts`, `properties.spec.ts`, `place-properties.spec.ts`; on Linux through WSL with
`agents/tools/linux-tests/run.sh --readonly-mount` (`access(2)` refused and `EROFS` checked there, 2026-11-26).
Done in plan steps 4.6a (library listings), 4.6b (Properties and menus), 4.6c (connections), 2026-11-26/27.

## What a listing says (`GET api/browse`)

- `readOnly` on the listing: why the asker can change **no file** in the folder (none of the rights with
  `ChangesFiles`, i.e. `AtFolder = Write`, applies there), the **first** that applies, SFE before the disk:
  `server` (`Sfe:Rights:Disabled` takes every such right), `space` (they are all in the space's disabled rights, the
  space editor's **Files read-only**, or in both lists together), `visitor` (visitors and guest accounts: such rights
  are never `GuestAllowed`), `rights` (the groups give none), `rule` (folder rules leave none here), `link` (behind a
  link leaving the library, `FileEndpoints.ProtectedFromWrites`), `diskReadOnly`, `diskRefused`. Null when it can.
- One right that changes files is enough to count as writable (rename only, say): the mark is about "nothing can be
  changed here", menus already grey each action by its right ([context-menu](context-menu.md)).
- The disk is asked only when SFE allows writing (no probe for a read-only user).
- `access.readOnly` and `access.children[name].readOnly`: none of that entry's rights changes files (folder rules);
  only sent when folder rules restrict the asker (`access` is null otherwise).
- `readOnly: true` on a **file** item: the disk marks it read-only (`FileInfo.IsReadOnly`, read from what the
  enumeration already has: on Windows the attribute, on Unix .NET's check of the mode bits against the process's
  user and group). Null otherwise; never on folders (see gotchas). Search results (from the index) don't have it.

## What Properties says (`access` on `GET api/files/properties`)

- `actions`: the actions that apply, in this order, each `allowed` or with the first `reason` refusing it (SFE's
  before the disk's): a file `open` (`files.view`), `download`, `saveOver` (the Replace rule: `files.delete` at
  it, `files.createFiles` in its folder, then its folder's disk, then `fileReadOnly`), `rename`, `move`, `delete`
  (to the trash); a folder `open` (`library.browse`), `download` (a zip), `createInside` (any of
  `files.createFolders` / `createFiles` / `upload`, then the folder's own disk), `rename`, `move`, `delete`; the
  top has no `rename` / `move` / `delete`.
- Rights are checked at the item (`FolderChecks.At`: links and their targets, the stricter); a refused right says
  `server` (`Sfe:Rights:Disabled`), `space` (its disabled rights), `visitor` (a guest and the right isn't
  `GuestAllowed`), `rights` (the groups), or `rule` (the asker has it, not here). A folder deleted or moved also needs
  the rights below it (`AllowsThroughout`): only checked when it is done, not told here.
- Renaming, moving and deleting change the item's **parent** folder: its disk is asked; adding inside asks the
  folder's own. One probe per folder per request (`ItemAccessReport` keeps them), `DiskWrites` keeps them 30 s.
- `disk`: the item's place (a folder itself, a file's folder) as the disk sees it, **whatever the asker's rights**
  (Properties is one item: no cost to save; listings ask only when SFE allows writing).
- `ruleFolder`: `FolderPolicy.RuleFolder`, the nearest folder at or above the item with some principal's rule (the
  table's entries); empty for a rule on the top; null without rules, or at the top when only default levels apply.
  Canonical path, not the real one behind a link.
- `fileReadOnly` is a reason word of `ReadOnlyReason` that listings never send (added in 4.6b; additive).

## Menus greyed by the disk

- `placeRefusal(listing)`: the folder listed's `readOnly` when it is `link`, `diskReadOnly` or `diskRefused` (the
  disk is only asked when the rights allow writing, so the rights' own greying is never covered over).
- Item menus (`ItemCommands.entry`, also the selection bar): `cut`, `rename`, `moveTo`, `delete`, the archive's
  `extractBeside` / `extractEach` / `compress`, and `paste` into a sub-folder except for `diskRefused` (the
  sub-folder may take writes), greyed with `readOnlyText(reason)`. Edit entries stay: the editors also save a copy
  elsewhere or download.
- The background menu (`FolderMenu.onDisk`): the `new` group (New folder…, New file…, uploads) and Paste.
- Searches never grey (no listing reason); the location bar's buttons (upload, new folder) aren't greyed (left for
  now: the server's error says it).

## Connections (`GET api/connections/{id}/browse`, `…/files/properties`)

- The listing's `readOnly`, the first that applies: the space's reasons as in the library (`server`, `space`,
  `rights`: `ListingAccess.SfeReason` without a folder; visitors never use connections), then the connection's when
  the folder's `FolderAccess` has no right that changes files: `connection` (`Connection.ReadOnly`, the cap, also for
  owners and server administrators), `rule` (a rule of the asker at or above the folder:
  `ConnectionFolders.RuleFolder`), else `level` (the groups' row is Read). Then, only when SFE lets the asker
  write, `provider`: the storage refuses changes in the folder.
- `provider` needs `ProviderCapabilities.ReadOnlyItems` (Dropbox: `sharing_info.read_only` of a file or folder, set
  on a folder shared with the account for viewing and on everything in it). The folder's own metadata is asked
  (`GetAsync`) by `RemoteReadOnlyFolders`, kept 30 s per connection and path, at most 1,000; outside the route's
  turn it takes one of the gate (`ProviderRefusesAsync`), and nothing while the provider asks to wait. A call that
  fails counts as writable. The singleton is per server (DI, not static: tests get their own).
- Children: `access.children[name].readOnly` from the connection's rules (as in the library), and `readOnly` on
  items the provider marks (`ConnectionListings.ToItem`; folders too, unlike the library). Listings from the index
  don't have the provider's mark (the index keeps no such column): the lock shows only on listings read from the
  provider; the folder's own chip doesn't depend on it.
- Properties: `access` (`ItemAccess`, as the library's) from `ConnectionAccessReport.Of`: rights checked in the space
  (`ItemAccessReport.Unheld`), then at the item (`Folders.At(path, id)`: rules set on it elsewhere too), then the
  provider: the item's own mark refuses saving over a file and adding inside a folder, its parent's (asked through
  `RemoteReadOnlyFolders` in the route's turn) renaming, moving and deleting. `disk` is the storage's side (the
  folder, or the file's folder); the client labels it with the provider's name.
- Menus: `placeRefusal` counts `provider` with the disk's reasons (writes greyed); the SFE reasons are already in
  the folder's rights (`access.rights`), which grey the menus as before.

## The disk probe (`DiskWrites`)

- Never writes. Windows: the volume's `FILE_READ_ONLY_VOLUME` flag (`GetVolumePathName` + `GetVolumeInformation`),
  then `CreateFile` on the folder with `FILE_ADD_FILE`, else `FILE_ADD_SUBDIRECTORY` (a drive's root often takes
  folders only), `FILE_FLAG_BACKUP_SEMANTICS` to open a folder: the system checks the ACLs, and an SMB server the
  share's permissions, on that open. Error 5 → `Refused`, 19 → `ReadOnlyVolume`. Unix: `access(path, W_OK)`;
  `EROFS` → `ReadOnlyVolume`, `EACCES` / `EPERM` → `Refused`.
- Anything else (gone, network error, no libc) → `Accepted`: the mark stays quiet when unsure, the write itself still
  says what went wrong.
- `Of` keeps each answer 30 s per full path (`KeptFor`), at most 1,000 folders (cleared when full): one probe per
  folder per 30 s whatever the pages and listings ([scale](../development/scale.md)). `Probe` asks now (tests).

## Client

- The chip **Read only** (`.read-only-chip`, lock icon) in the location bar's summary, after the counts; its `title`
  is the reason's sentence (`readOnlyText`). Browsing only: searches never show it. Nothing when writable: the usual
  case stays quiet.
- `sfe-lock-mark` beside `sfe-partial-mark` on tiles, rows and folders: shown only when the folder is writable and
  the item isn't (a child rule `readOnly` and not `pathOnly`, else a file's `readOnly`); its hint says which.

## Gotchas

- On Windows a **folder's** read-only attribute means "customised folder" to Explorer (`desktop.ini`), not "can't
  write": it is never read or shown.
- Share permissions are only known by trying; the `CreateFile` open does ask the server, but a quota or a full disk
  isn't seen before writing.
- An elevated process or a service with `SeBackupPrivilege` / `SeRestorePrivilege` **enabled** passes the open
  through backup semantics even where its writes would fail; a service doesn't have them enabled by default.
- `access(2)` checks the real user, not the effective one: the same for a service.
- The listing cache (`LibraryBrowser`) is keyed by the folder's write time: a file's attribute changed alone shows
  after the folder changes or the cache drops it.
- The probe is static (server-wide), keyed by full path: two spaces on the same folder share answers, as they share
  the disk.
- A Dropbox member may rename or move the shared folder itself (its mount) even when it is shared to view only:
  Properties asks the mount's parent for those, so they stay allowed; inside it everything is refused.
- Two `[AsParameters]` records of one route can't have properties of the same name (ASP.NET: "An item with the same
  key has already been added"): `ConnectionFileServices.ServerOptions`, not `Options` (the browse services have one).
