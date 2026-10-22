# Folders excluded from scans, trickplay and thumbnails

Code: `Core/Library/FolderExclusions.cs` (`ExclusionKind`, `FolderExclusionState`, `FolderExclusions`),
`SettingsService.SetExclusionsAsync`, `DirectoryIndexer` (`IndexAsync`, `IndexDirectoryAsync`),
`TrickplayService.EnumerateVideos`/`IsInFolders`, `ThumbnailService.GetAsync`, `ScanEndpoints` (`SetExclusions`,
`Start`), `LibraryEndpoints.Browse`; client `library/items/exclusions.ts`, `library/scan/folder-exclusions.dialog.ts`,
`exclusions-section.component.ts`, `folder-item.component.ts`, `location.component.ts`, `item-menu.ts` (`exclusions`), `main-menu.component.ts`,
`scan.service.ts`. Tests: `FolderExclusionTests`, `FolderExclusionRouteTests`, `TrickplayTests.ExcludedFoldersGetNoTrickplay`,
client `library/scan/folder-exclusions.spec.ts`. User docs: `docs/user-guide/scanning.md#excluded-folders`.

- Three per-library list settings, one per kind: `Scan:ExcludedFolders` (since before 1.80), `Trickplay:ExcludedFolders`,
  `Thumbnails:ExcludedFolders`. Root-relative paths; an entry covers its sub-folders; the root can't be listed. Their
  keys are in `SettingCatalog.PerLibraryKeys`, `SettingsService.Normalize`/`Validate` and `MoveFolderPaths` (renames follow).
- `FolderExclusions.Of(options)` compiles the lists once per options instance (`ConditionalWeakTable`, rebuilt when a
  list reference changes). `Through(kind, path)` walks up the path, O(depth): the listed folder it is excluded through.
- **Scans freeze, never forget** (user's choice, 1.80): an excluded child is skipped *and* kept in the "still on disk"
  set, so its index rows (files, tags) stay unrefreshed. Before 1.80 the next scan of the parent deleted them.
  `DELETE api/index/folder` still forgets a folder at once. A scan started on or inside an excluded folder: the indexer
  returns at once, and `POST api/scan` answers 400 with a message.
- Trickplay: discovery doesn't descend into excluded folders; a player request doesn't queue them. `PassSettings`
  serialises the whole `Trickplay` options, so a list change restarts the pass. Existing sheets are still served.
- Thumbnails: an excluded file gets a fresh cached thumbnail or 404, never an ffmpeg run. Covers go through the same route.
- Right `index.exclude` (Write, `AtFolder` Read, Administrators, `since: 10`). `PUT api/index/exclusions
  { path, scan?, trickplay?, thumbnails? }` (null = leave alone) answers the folder's new `FolderExclusionState`:
  404 for a folder not seen or not a folder, 403 when seen but not allowed, 400 for the root, for letting in a folder
  excluded through a parent, and for a list locked by the configuration (detected after the save: the state didn't change).
- `GET api/browse` carries `exclusions` (the folder) and `excluded` (its folder items), for everyone: computed per
  request from the lists, O(page) (`FolderExclusions.Mark`), null when nothing is excluded.
- Client: a muted dashed chip (`.exclusion-chip`, `styles/folders.css`) after the folder's name, its full wording in
  the hint; the same next to the location bar's counts, a button opening the dialog for holders of the right. Menu
  entry **Scans and previews…** only while browsing (search results have no state). The dialog sends only the boxes
  that differ from the listed state; inherited ones are checked and disabled.
- Admin page **Scanning › Excluded folders** (`library/scan/exclusions-section.component.ts`, `index.exclude`):
  `GET api/index/exclusions` (`ExcludedFolder`: the three lists, `Exists`; folders the caller doesn't see are left
  out), a box per list saved at once, Remove = every list false, Add with the shared `FolderPickerComponent` fed by
  `api.browse` (folder rules apply, no new route). The PUT takes a folder gone from the disk off the lists only (404
  for putting it on one, or for a path on no list).
