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
