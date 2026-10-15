# Missing files and relink

Code: `src/Sfe.Core/FileTracking/` (`MissingFileRecorder`, `RelinkService`), entities in `Data/Entities/MissingFiles/`;
client `src/app/tracking/`.

- The indexer never deletes a tagged file outright: `MissingFileRecorder` (static, avoids a DI cycle RelinkService →
  TagService → DirectoryIndexer) copies it to `MissingFiles`/`MissingFileTags` in the delete's transaction.
- Matching runs once per scan after `IndexAsync`.
- Auto-relink: single candidate (size + `Sha1`, or name/ext/size/mtime without fingerprint) registered *after* the
  original `RegisterTime`, same name breaks ties; the thumbnail/trickplay cache moves with it.
- Tests: "registered after" ordering needs separate scans (one scan is parallel); junctions/hard links with
  `cmd /c mklink /J|/H` ([testing-backend.md](../development/testing-backend.md)).
