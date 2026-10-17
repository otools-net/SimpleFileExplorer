# New folder and rename (file management, phase 1)

Code: `src/Sfe.Core/Files/FileOperations.cs`, `src/Sfe.Api/Endpoints/FileEndpoints.cs`; client `app/files/`
(`name.dialog.ts`, `item-menu.ts`), `ListingStore.applyAdded`/`applyRenamed`/`highlight`, the library page's keys,
`location` (New folder button), `styles/file-management.css`. Tests: `FileOperationsTests`, `FileRouteTests`,
`files.spec.ts`. User docs: `docs/user-guide/managing-files.md`. Plan:
[file-management-plan.md](../development/file-management-plan.md).

## Server

- Rights `files.createFolders`, `files.rename` (`since: 5`, `DefaultGroups = []`, Write, `AtFolder = Write`).
- `POST api/files/folders {path, name}` needs the right at `path`. `POST api/files/rename {path, name}` needs it at the
  item **and its parent** (the parent's list of names changes), and for a folder `AllowsThroughout` (403 with a
  message). Both answer 404 for a place whose real path is a work folder or among `ServerFiles` (a link in the library).
- Answers: 200 with the item through `TagService.AttachIndexDataAsync` (as listings show it), 400 `errors.name`, 404,
  403, 409 with problem `type` `file-exists` / `scan-running` / `folder-rules`, 500 for disk refusals (the client's
  `Api` resolves 409s with their body, so other failures must not be 409s).
- `FileOperations`: names (`FileNames`), a clash on `File.Exists || Directory.Exists` (hidden or not: "exists" only),
  `ScanCoordinator.TryBeginOperation(paths)` (null while a scan runs over an overlapping tree → `ScanRunning`; a scan
  started meanwhile waits for the operations on its tree), the index path-length check on SQL Server, folder rules
  (`BeginMoveAsync`; its `Conflict` = rules left at the target), `Directory.Move`/`File.Move(overwrite: false)`,
  case-only renames through a temporary name, 3 tries 250 ms apart on a sharing violation, `PathMoves`,
  `CompleteAsync`.
- Messages never carry server paths: `DiskFailure` maps `UnauthorizedAccessException` (no write access), sharing and
  lock violations (`HResult & 0xFFFF` 32/33: in use) and `0x80070005` as an `IOException` (what Windows answers for
  `Directory.Move` of a folder holding an open file).
- A new folder gets an index row at once when its parent has one.
- Logged with the account (`LogInformation`, `{Account} renamed {Path} to {Name}`).

## Client

- Phones: the ⋮ is always shown (`hover: none`); Android's long press sends `contextmenu`, iOS Safari's doesn't.
- `ItemMenu` (root service) + `sfe-item-menu` (one per page, `position: fixed`, moved back inside the window after
  render) + `button[sfeItemMenu]` (⋮). A right click opens it only when `hasActions(item)` (today: may rename), so
  people without file rights keep the browser's menu; Shift+right click always gives the browser's. Entries: Rename,
  Tags, Copy full path. Closes on a pointerdown outside (not on its own trigger: the ⋮ toggles), Esc/Tab, scroll,
  resize, `generation`.
- Tiles: the ⋮ takes the tag button's place and pushes the tag and copy buttons left only when it shows (`:has()`).
- `NameDialog` (`dialogs.name`): `newFolder(parent)`, `rename(item)`; a file's name is selected up to its extension;
  400/409 messages show under the field; on success `store.applyAdded` (sorted by name among the loaded items of its
  kind, first of its kind for other sorts; counts updated) or `applyRenamed` (in place; selection and anchor follow).
  Both highlight the item (`store.highlight`).
- `ListingStore.accept` drops items already loaded (a folder changed since its first page repeats one on the next).
  A rename to a name sorting later can still skip one item on the next page until a reload.
- Keys (library page): `F2` = the focused item (`closest('[data-path]')`) or the only selected file; `Shift+N` (and
  Ctrl+Shift+N where the browser lets it through: Chrome and Edge keep it for a private window) = new folder, only in
  a folder that isn't path-only. Listed in the search help for those with the rights.
- The library page is in the initial bundle: these components count toward its 600 kB budget warning (631 kB at
  1.55.0).
