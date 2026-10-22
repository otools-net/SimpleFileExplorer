# Data keyed by path following a rename (`PathMoves`)

Code: `src/Sfe.Core/Files/PathMoves.cs`, `DirectoryIndexer.ForgetAsync`/`FolderIdAsync`,
`SettingsService.MoveFolderPathsAsync`, `QuickLinkService.MovePathsAsync`, `LibraryBrowser.Invalidate`. Tests:
`FileOperationsTests.ARenamed…`, `FileRouteTests.ARenamedFolderKeepsItsRulesAndQuickLinks`.

- `MoveAsync(fromFull, toFull, isFolder)` runs **after** the disk changed: each part has its own try/catch (logged as
  a warning, left to the next scan or to lazy rebuilding). Never pass it the request's token: the person leaving must
  not stop it halfway.
- Index rows: `ForgetAsync(to)` first (stale rows at the target; tagged ones kept as missing files; skipped for a
  case-only rename), then one transaction: `ExecuteUpdate` of every row `InTree(from)` to
  `to + FullName.Substring(from.Length, 32767)` (the two-argument `Substring`: SQL Server's `LEN` drops trailing
  spaces), the folder row's `Name`/`ParentId`; a file's `Name` (stem), `Extension`, `DirectoryId` (folder row made
  when missing, else the file row is dropped). Ids stay, so tags, fingerprints and resolutions stay.
- Folders only: `Scan:ExcludedFolders`, `Trickplay:Folders`, `Trickplay:ExcludedFolders`, `Thumbnails:ExcludedFolders`, `Editor:OutputFolder` at or below the folder (saved as
  library rows, or in the server store without levels), `TaskSchedules.Path`. Quick links of every user (needs
  `QuickLinkService`, handed to each library by `TenantHost`; null in Core tests).
- Caches: a file's thumbnail and trickplay folder (with its fingerprint) at once; a folder's in the background
  (`CacheMoves`, a chain of tasks that tests await), paging its index rows by id: unindexed files lose theirs.
- Listings and covers of both parents and both paths are invalidated (`LibraryBrowser.Invalidate`), tag counts too.
- Folder rules are not here: `FolderRuleService.BeginMoveAsync` → disk → `CompleteAsync`/`AbortAsync` wrap the disk
  step (`FileOperations.RenameAsync`).
