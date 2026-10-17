# Uploads (file management, phase 5)

Code: `src/Sfe.Core/Files/Uploads.cs` (`IUploadAccess`), `Api/Endpoints/UploadEndpoints.cs`,
`RequestLimits.Chunks`; client `app/files/uploads.ts` (`UploadQueue`, `droppedFiles`, `pickedFiles`),
`uploads-panel.component.ts` (in the operations panel), `uploads-section.component.ts` (admin), upload buttons in
`location.component.ts`, desktop drops in `operations-panel.component.ts`. Tests: `UploadsTests`, `UploadRouteTests`,
`uploads.spec.ts`. User docs: `docs/user-guide/managing-files.md#upload`, `docs/administration/file-management.md#uploads`.

## Decisions (2026-10-17)

- No size limit by default; `Files:Uploads:MaxFileSizeMegabytes` sets one (0 = none). The free space kept is the copies'
  `Files:MinimumFreeSpaceMegabytes`.
- Names taken: asked before sending (`POST check`), with "the same for the rest"; Replace needs the delete right
  (to the trash, as for moves).
- Resume **also after a reload**: unfinished uploads are listed from the server; the same file (name, size, date
  within 2 s) picked or dropped into the same folder goes on from `received`.
- Space administrators (`settings.files`) see and cancel everyone's unfinished uploads.

## How it works

- Own protocol: `POST api/files/uploads` (target, name = path below it for folder uploads, size, modified) → 201 with
  `id`; `PUT …/{id}?offset=` raw body ≤ 16 MB (`RequestSizeLimit`), offset ≠ received → 409 with the upload (resend
  from there); `POST …/{id}/finish {conflicts}`; `GET` (own; `all=true` with `settings.files`), `DELETE`.
- Storage: `{work folder}/uploads/{id}.part` + `{id}.json` (the `UploadInfo`, written via a temp file), read into memory
  on first use (again when the work folder location changes; uploads in the old location are then forgotten). Ids are
  32 hex digits (route regex, `SafeId`): never a path.
- Each chunk: per-upload semaphore, truncate to `received` first (bytes of a cut-off write dropped), append, flush to
  disk, save the record. More than the size or the chunk limit → 400 and nothing kept.
- Rights: `files.upload` at the target and at every folder a folder upload makes, checked at start and **again at
  finish** with the caller's access then; hidden target → 404; names checked per segment with `FileNames.Check`
  (no `..`, `.`, empty, reserved `.sfe` at the root, protected places through links). Max 32 levels.
- Finish: folders made on the way, `File.Move` from the part (same drive when the work folder is in the library), the
  file's date set, `IndexFilesAsync`, listing invalidated. Scan gate per destination.
- Abandoned: removed when idle more than `Files:Uploads:KeepUnfinishedHours` (48), checked at most every 10 minutes
  when uploads are started or listed (no background task).

## Client

- `UploadQueue`: batches per drop/pick; check (≤ 1000 names per request) → `asking`/`queued`; two files at a time;
  8 MB chunks (`Blob.slice`), halved down to 256 KB after a 413 (a proxy's limit); 5 tries with backoff; 404 = gone,
  start again; `beforeunload` warning while busy; `resume()` at page load lists interrupted uploads.
- Folder drops walk `webkitGetAsEntry` (read synchronously in the drop handler: the items are emptied after it).
- Not done: upload into a folder chosen in a picker; per-chunk progress inside a chunk (progress moves per chunk).
