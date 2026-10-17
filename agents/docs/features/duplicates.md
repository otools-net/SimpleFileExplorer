# Duplicates

Code: `src/Sfe.Core/FileTracking/` (`DuplicateFinder`, `DuplicateRemover`, `FileIdentity`),
`src/Sfe.Core/Indexing/FileFingerprint.cs`; client `src/app/tracking/` (`duplicates-panel.component.ts`).

- Group by `(Size, Sha1)` on `IX_Files_Size_Sha1`; keep the query shape (no `InTree` for the whole library, order
  `Size DESC, Sha1 DESC`) for SQLite's covering index plan.
- The fingerprint (`FileFingerprint`) is SHA1 of the first MB.
- Pages by key (`after` cursor, no `OFFSET`).
- `DuplicateRemover` must keep the byte comparison (`FileIdentity.SameContentAsync`) and the same-file refusal
  (`FileIdentity.SameFile`: links in every segment, volume/file id on Windows); order merge tags → trash.
- Removing goes through the trash ([trash.md](trash.md)), holding the scan gate: `Trash.MoveInAsync` (row out, the
  copy's own tags into `TrashTags`, entry owned by the caller's `FileActor`), or `DeleteForGoodAsync` when
  `Files:DeletePermanently` (`DuplicateResults.RemovesPermanently` tells the client). The route answers `trashId`.
- A restore gives the copy its own tags back; the tags merged into the kept copy stay there.
- Client: Undo in the toast restores (`api.restoreFromTrash`); a dissolved group is `[hidden]`, not dropped, so the
  Undo can bring it back (`restored` output, summary counted back).
