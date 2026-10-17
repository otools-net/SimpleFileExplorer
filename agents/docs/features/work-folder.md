# Work folder (`.sfe`)

Code: `src/Sfe.Core/Files/WorkFolder.cs`, `LibraryPaths.WorkFolderName`/`IsWorkFolderRelative`/`TryResolve`,
`LibraryBrowser.ToItem`, `DirectoryIndexer.IsIgnoredDirectory`, `FileEndpoints.Protected`; setting `Files:WorkFolder`
(`WorkFolderLocation`). Tests: `FileOperationsTests.TheWorkFolder…`, `FileRouteTests.NoRouteReachesTheWorkFolder`.

- Where file management will keep the trash and unfinished uploads (phases 3, 5), per library. The user chose a
  library setting: `Library` (default: `<root>/.sfe`, hidden attribute on Windows) or `Server`
  (`{Thumbnails:CacheDirectory}/work`; libraries after the first already have their own cache folder). `work` is not
  hex, so the cache layout step (`V1_40_0/ShardCacheLayout`) never touches it.
- `WorkFolder.Get("trash")` creates it on demand. `Contains(full)` is true for **both** locations.
- The name `.sfe` at the root is reserved whatever the setting: `LibraryPaths.TryResolve` refuses it as the first
  segment, after `GetFullPath` (so `.sfe.` and `ChildDir/../.sfe` too; on Windows also its 8.3 name, through
  `GetLongPathNameW`, only when the path holds `~`). Every route that resolves a path therefore answers 404. Listings
  (`ToItem` returns null), siblings, covers and scans (`IsIgnoredDirectory`) leave it out, also with `ShowHiddenItems`.
- An install that already had a folder `.sfe` at its root loses sight of it (said in `docs/upgrading.md`, 1.55).
- The file routes also refuse a place whose real path (links resolved) is in a work folder or among `ServerFiles`.
