# Item access: read-only or writable shown

Code: `src/Sfe.Api/Endpoints/ListingAccess.cs` (`SfeReason`, `DiskReason`, `AccessView`),
`src/Sfe.Core/Files/DiskWrites.cs` (`DiskWrite`), `src/Sfe.Core/Library/ReadOnlyReason.cs`,
`EffectiveAccess.SpaceDisabled` (`RightsEvaluator`), `LibraryBrowser.ToItem` (`LibraryItem.ReadOnly`); client
`library/read-only.ts` (`readOnlyReason`, `readOnlyText`, `itemLock`), `LockMarkComponent` (`items/item-parts.ts`),
the chip in `location/location.component.html`, `.read-only-chip` (`styles/location.css`), `.lock-mark`
(`styles/items.css`). Tests: `ListingReadOnlyRouteTests`, `DiskWritesTests`, `read-only.spec.ts`; on Linux through WSL with
`agents/tools/linux-tests/run.sh --readonly-mount` (`access(2)` refused and `EROFS` checked there, 2026-11-26). Plan steps 4.6a
(library listings, done), 4.6b (Properties and menus), 4.6c (connections):
[release-2.0-plan](../development/release-2.0-plan.md).

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
