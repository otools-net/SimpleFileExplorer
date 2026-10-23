# Work folder (`.sfe`)

Code: `src/Sfe.Core/Files/WorkFolder.cs`, `LibraryPaths.WorkFolderName`/`IsWorkFolderRelative`/`TryResolve`,
`LibraryBrowser.ToItem`, `DirectoryIndexer.IsIgnoredDirectory`, `FileEndpoints.Protected`; setting `Files:WorkFolder`
(`WorkFolderLocation`). Tests: `FileOperationsTests.TheWorkFolder…`, `FileRouteTests.NoRouteReachesTheWorkFolder`.

- Where file management keeps the trash and unfinished uploads, per library. A library setting: `Library`
  (`<root>/.sfe`, hidden attribute on Windows) or `Server` (`{Thumbnails:CacheDirectory}/work`; libraries after the
  first already have their own cache folder). `work` is not hex, so the cache layout step (`V1_40_0/ShardCacheLayout`)
  never touches it.
- Default `Server` since 1.82.0 (was `Library`). The step `V1_82_0/KeepWorkFolderInLibraries` gives every library of
  an updated install that took the default its own row `files.workFolder` = `Library`
  (`SettingLayers.KeepPreviousDefaultAsync`, reusable for any changed default; skips a value of the configuration or
  the server, turns a bare server lock into a server value). With `MigrateOnStartup=false` the steps run at each start:
  the marker `work-folder-kept.txt` beside the settings file stops it pinning libraries created later. Fresh installs
  run no step. Tests: `WorkFolderUpgradeTests`; tests of the `.sfe` location set `Sfe:Files:WorkFolder=Library`.
- The step only runs once the app version is ≥ 1.82.0: the merge into `main` must bump the **minor** version.
- `WorkFolder.Get("trash")` creates it on demand. `Contains(full)` is true for **both** locations.
- The name `.sfe` at the root is reserved whatever the setting: `LibraryPaths.TryResolve` refuses it as the first
  segment, after `GetFullPath` (so `.sfe.` and `ChildDir/../.sfe` too; on Windows also its 8.3 name, through
  `GetLongPathNameW`, only when the path holds `~`). Every route that resolves a path therefore answers 404. Listings
  (`ToItem` returns null), siblings, covers and scans (`IsIgnoredDirectory`) leave it out, also with `ShowHiddenItems`.
- An install that already had a folder `.sfe` at its root loses sight of it (said in `docs/upgrading.md`, 1.55).
- The file routes also refuse a place whose real path (links resolved) is in a work folder or among `ServerFiles`.
