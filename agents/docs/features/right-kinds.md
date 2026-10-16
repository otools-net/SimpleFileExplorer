# Right kinds: Read/Write and what a folder must allow

Code: `src/Sfe.Core/Access/RightCatalog.cs` (`RightKind`, `FolderLevel`, `RightDefinition.Kind`/`AtFolder`/
`ChangesFiles`, `WriteSelector`, `FileWriteSelector`); client `core/models.ts`, `access/access-parts.ts`
(`rightMark`, `changingRights`, `fileChangingRights`), `group-editor`, `space-editor`. Tests: `RightCatalogTests`,
`AccessTests.TheServerCanSwitchOffEveryRightThatChangesFiles`/`TheServerCanBeMadeReadOnly`. Folder rules, which use
`AtFolder`: [folder-access.md](folder-access.md).

## Classifying a right (every new right sets both)

- `Kind`: `Write` when **any** route it opens changes something (files, index, tags, settings, accounts, own data);
  `Read` when it only looks. Classify by the right's most powerful route (`tracking.missing` lists and relinks: Write).
- `AtFolder`: what a folder must allow for the right to apply in it. `Write` **only** for rights that change the
  library's files on disk (`files.edit`, `tracking.duplicatesRemove`, the future file-management rights); `Read` for
  rights about files or folders that change at most the index (tags, relinks, removal from the index, quick links);
  null for rights of the whole space (settings, tasks, logs, accounts, tag definitions) and for server rights.
- Invariants tested: a Write right is never `GuestAllowed`; `AtFolder = Write` implies `Kind = Write`; server rights
  have no `AtFolder`; the rights changing files are exactly the expected list (update the test with a new one).
- Space-wide Read rights that **see every folder's names** (`tasks.view`, `logs.view`: run logs and log text carry
  paths) won't be filtered by folder rules: say so in their description when folder rules ship.
- Future file-management rights: `Write`, `AtFolder = Write`, `GuestAllowed = false`, **`DefaultGroups = []`** (with
  `since`, `GrantNewRightsAsync` then gives them to nobody: only server administrators and groups ticked by hand).

## Selectors

- `RightCatalog.Matches` accepts `kind:write` (every Write right) and `folder:write` (every right that changes files)
  next to ids and `prefix*`, case-insensitively: they work in `Sfe:Rights:Disabled` (read-only server, set by whoever
  runs it) and show in `api/access/rights`' `disabledByServer` without client changes.
- Setting keys use `RightCatalog.MatchesName` (text only): `SettingCatalog.Matches` must never see the selectors.
- `TenantDisabledRights` stores plain ids: the space editor's **Files read-only** / **Read-only** buttons tick the
  ids of `fileChangingRights` / `changingRights` (no new column, nothing to migrate).

## Client

- `rightAreas` lists each area's Read rights first; `rightMark` gives the badge: **Changes things** (Write) or
  **Changes files** (`atFolder: 'write'`, `danger` colour), in the group and space editors.
