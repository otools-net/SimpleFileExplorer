# Indexing (scan) and removing folders from the index

Code: `src/Sfe.Core/Indexing/` (`DirectoryIndexer`, `ScanCoordinator` (background scan), `FileFingerprint` (partial
SHA1), `MediaDimensions`); client `src/app/library/scan/` (`ScanPanelComponent`).

- Indexer batches of 1000 + `ChangeTracker.Clear()` ([scale.md](../development/scale.md)).
- What a scan reads per file: resolution ([resolution.md](resolution.md)), duration ([duration.md](duration.md)),
  fingerprint (SHA1 of the first MB, [duplicates.md](duplicates.md)).
- Missing-file matching runs once per scan after `IndexAsync` ([missing-files.md](missing-files.md)).
- **Index folder removal:** `DELETE api/index/folder?path=&exclude=` → `ScanCoordinator.RemoveFromIndexAsync` (409
  while a scan runs) → `DirectoryIndexer.RemoveTreeAsync` (the scan's `DeleteTreeAsync`, so tagged files become
  missing files); `exclude` adds to `Scan:ExcludedFolders` (`SettingsService.ExcludeFolderAsync`), which
  `IsIgnoredDirectory` checks by full path next to the ignored names.
- Excluded folders are skipped but **keep their rows** (since 1.80): [folder-exclusions.md](folder-exclusions.md).
- A scan's errors are only logged: tests pass a `WarningLog` ([testing-backend.md](../development/testing-backend.md)).
- Not-indexed marks in listings: [not-indexed-marks.md](not-indexed-marks.md).

## Scan as you go (2026-11-23, release 2.0 step 3.6)

Code: `Core/Indexing/ListingIndexer.cs`, `ScanCoordinator.TryBeginFolderIndex`, `LibraryEndpoints.Browse`
(`BrowseIndex`); setting `scan.indexWhenListed` (`ScanOptions.IndexWhenListed`, library level, on by default). Tests:
`ListingIndexerTests`, `ListingIndexerRouteTests`.

- **When:** `GET browse` with `offset` 0, after the page is built (its marks are the index's before): `Listed(path)`
  never waits. Later pages and searches don't ask; connections have their own sync on opening.
- **Throttle:** a folder is queued at most once per `Interval` (10 min, by the time it was asked, kept per full path;
  past 10,000 remembered, those older than the interval are forgotten). A bounded queue (64, one folder at a time):
  a folder that doesn't fit is forgotten, so the next listing asks again.
- **What:** `DirectoryIndexer.IndexAsync(recursive: false)`: the folder's files (as a scan: fingerprints, resolutions,
  default tags), the rows of its sub-folders gone from the disk; sub-folders present are not indexed. Removed tagged
  files go through `MissingFileRecorder` as in a scan. No relink pass (it reads every missing file): left to scans.
  Excluded and ignored folders: nothing (`IndexAsync` returns).
- **Never under a scan or an operation:** `TryBeginFolderIndex` refuses while a scan runs over or above the folder,
  or a file operation (`TryBeginOperation`) works in, above or below it; the folder is then forgotten (asked again at
  the next listing). Held, it is an operation: a scan starting meanwhile waits for it. File operations are never
  refused because of it.
- **Removal by name:** the indexer removes the rows it read in that folder **by their full name and folder**, not by
  id: a file renamed or moved meanwhile (`PathMoves` updates its row) is not removed. Protects scans too.
- **Told:** only when files were indexed or removed (`ScanProgress.Files` / `Removed`): `LibraryBrowser.Invalidate`,
  so the feed's `changed` (no origin) refreshes every open listing of the folder; the refresh lists it again on page
  1, which the throttle leaves alone.
- **Remove from index** is undone by opening the folder unless it is also excluded (the dialog and the docs say so).
- Tests' `TestLibrary.Settings()` turns it **off** (it would index what route tests list behind their backs); its
  tests turn it on.
- Gotcha met: `Done?.Invoke(folder, await IndexAsync(…))` never indexed in the app (C# skips the arguments of a
  `?.` call when the event has no listener; the unit tests always listen). Work first, then raise.
