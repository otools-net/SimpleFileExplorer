# Previous versions of a file

Code: `Core/Files/PreviousVersions.cs`, `Trash.CopyInAsync` (where versions come from), `PathMoves.MoveVersionsAsync`;
`Api/Endpoints/VersionEndpoints.cs` (`VersionContracts`, `VersionServices`, `FoundVersion`); client
`files/versions.dialog.ts` (+ `.html`, `version-texts.ts`), `OrganisingCommands.versions`, the Properties line
(`PropertiesDialog.loadVersions`), `styles/versions.css`. Tests: `PreviousVersionsTests`, `VersionRouteTests`,
`versions.spec.ts`, `version-texts.spec.ts`, `item-commands.spec.ts`. User docs: `docs/user-guide/managing-files.md`
(Previous versions). Screenshot `previous-versions.png` (docs-screenshots scene `versions`).

## Decisions (release 2.0 step 4.2, owner's choice)

- **The trash stays the trash**: a version is a `TrashEntry` with `Kind = PreviousVersion` (made by the text and picture
  editors' save over, once per editing session: [text-editor](text-editor.md)). No table, no column, no migration;
  technical names (`api/files/trash`, `TrashEntries`, `SfeTrash`) unchanged. The trash page still lists them
  ("previous version", "Saved over …"), its Restore still puts one back next to the file.
- **Who sees them**: whoever may open the file (`files.view` at it), not the trash's own rule (deleter, those who may
  delete in the folder): a version is content the file had, readable by the same people.
- **Restore over** = the rights of saving over the file (`TextEndpoints.AccessTo(...).Overwrite`), refused in a space
  that always deletes for good (it would put the current content in a trash that space doesn't use). **Restore a
  copy** = `files.createFiles` in the folder (`SaveCopy`). **Open** = `files.view`; `?download=true` also
  `files.download`. Every route refuses a file behind a link leaving the library (`ProtectedFromWrites`).
- Restore over puts the current content in the trash first (`CopyInAsync`: a version of its own, its id in
  `trashId`), then swaps the version in whole (copy beside as `.name.guid.sfe-tmp`, `FileContent.ReplaceAsync`) with
  **a new modification date** (thumbnails, editors' versions and listings see a change; the cached thumbnail is keyed
  by the date); the version restored is purged. A swap that fails purges the copy just kept: nothing changes.
- Restore a copy keeps the version (`KeepBoth` name next to the file, new date, indexed at once).
- Versions **follow their file** on rename and move (`PathMoves` step "previous versions": the entry's `Path` only;
  its `Name` is its file name in the trash folder, so it stays the old name). Deleted entries keep where they were.

## Gotchas

- `TrashEntries.Path` has no index (no length limit: `SfeDbContext.ConfigureTrash`): listing reads every
  `PreviousVersion` row and compares paths with `LibraryPaths.PathComparer` in memory. Fine while the trash is purged
  (30 days by default); SQLite and SQL Server compare case differently, which is why it isn't done in SQL.
- At most `PreviousVersions.MaxListed` (100) are listed; the Properties line says "More than 100 versions" then.
- A connection's files have no versions (the storage service keeps its own); the menu entry is hidden there.
- `Trash.PurgeAsync` of a version keeps the live file's previews (they are keyed by the file's path).
