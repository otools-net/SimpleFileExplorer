# Zips of several files or a folder (file management, phase 6)

Code: `src/Sfe.Core/Files/Archives.cs` (`IArchiveAccess`, `ArchiveVerdict`), `Api/Endpoints/ArchiveEndpoints.cs`,
`Api/Security/ArchiveLinks.cs`; client `app/files/downloads.ts` (`FileDownloads`), the Download button of
`selection-bar.component.ts`, the `download` entry of `item-menu.ts`. Tests: `ArchivesTests`, `ArchiveRouteTests`,
`downloads.spec.ts`. User docs: `docs/user-guide/managing-files.md#download-several-files-or-a-folder`.

## Decisions (2026-10-17)

- Right: the existing `files.download` (members have it), checked with `files.view` at each item and each folder.
- Hidden files (dot files, the hidden attribute) are always included; folders hidden by folder rules never.
- Two zips at once per caller; `Files:MaxArchiveMegabytes` (0 = none, the default) checked from the index.

## How it works

- `POST api/files/archive {paths}` (≤ 10 000) → `Archives.Plan` (exists, not protected, seen, downloadable; items
  inside a chosen folder dropped; same names get ` (2)`; zip named after the item or the common parent, the root's
  name otherwise) → a one-time link `api/files/archive/{48 hex}` (`ArchiveLinks`: 2 minutes, same tenant and caller,
  in memory). The client clicks an `<a download>` on it: the browser streams to disk, no blob in memory.
- `GET` takes the link, plans again with the access now, then `Archives.WriteAsync` straight into the response
  (`ZipArchive.CreateAsync` on a non-seekable stream: data descriptors, Zip64 past 4 GB, tested with 4.7 GB). No
  `Content-Length`.
- `ResponseStream` wraps `Response.BodyWriter`: Kestrel refuses synchronous writes and the zip writes small parts
  (headers, descriptors) synchronously; those are buffered in the pipe and sent with the next async write.
- Walk: links never followed (listed as left out), `IsProtectedReal` per entry (the server's files, work folder) with
  the real path built from the parent's (no `RealPath` per file), `Check` per folder (Hidden → silent, Refused →
  listed). Unreadable files are listed; the list goes into `SFE - left out.txt` at the end of the zip (≤ 1000 lines).
- Stored for everything but a list of compressible extensions (text, subtitles, BMP/TIFF, WAV…), `Fastest` for those.
- Entry names: `\` replaced by `_` (a plain character on Linux, a folder on Windows extractors: zip-slip).
- Cancel: the request's token; a client gone stops the walk. An I/O error or the size passed mid-way (index behind)
  aborts the connection, so the browser shows a failed download instead of a cut zip that looks complete.

## Gotchas

- `IDbContextFactory<SfeDbContext>` belongs to a library container: route tests can't take it from the factory's
  services; index through `POST /api/scan` instead.
- Download is not in `ItemMenu.hasActions`: members (who all have it) keep the browser's right-click menu; the entry
  only shows when the menu opens for a file-management right.
