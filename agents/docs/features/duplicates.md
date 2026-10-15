# Duplicates

Code: `src/Sfe.Core/FileTracking/` (`DuplicateFinder`, `DuplicateRemover`, `FileIdentity`),
`src/Sfe.Core/Indexing/FileFingerprint.cs`; client `src/app/tracking/`.

- Group by `(Size, Sha1)` on `IX_Files_Size_Sha1`; keep the query shape (no `InTree` for the whole library, order
  `Size DESC, Sha1 DESC`) for SQLite's covering index plan.
- The fingerprint (`FileFingerprint`) is SHA1 of the first MB.
- Pages by key (`after` cursor, no `OFFSET`).
- `DuplicateRemover` must keep the byte comparison (`FileIdentity.SameContentAsync`) and the same-file refusal
  (`FileIdentity.SameFile`: links in every segment, volume/file id on Windows); order merge tags → delete file →
  delete row.
