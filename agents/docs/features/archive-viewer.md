# Archive viewer (exploring archives)

Code: server `Api/Endpoints/ExploreEndpoints.cs` (`api/files/explore`), `ArchiveCatalogs` (store) in
`Core/Files/Compression/`, `ArchivesConfig` in `ConfigEndpoints.cs`; client `app/formats/archive/archive-view.component.ts`,
`ARCHIVE_FORMAT` / `isExplorable` / `setArchives` in `formats/formats.ts`, `pathToken` in `core/paths.ts`, the archive part of
`styles/reader.css`. Tests: `ExploreRouteTests`, `ArchiveCatalogsTests`, `archive-view.component.spec.ts`, rows in
`formats.spec.ts`; real app: `agents/tools/visual-check/archive-viewer.mjs`; docs scene `archive`. User docs:
`docs/user-guide/archives.md`. The core (formats, names, catalogs): [archive-reading](archive-reading.md); what is left:
[compressed-archives-plan](../development/compressed-archives-plan.md).

## Routes

- `GET api/files/explore?path=&folder=|at=&after=&limit=` (`files.view`, `FolderScoped`; missing, hidden, server
  files, work folder → 404): one folder of the archive (id, or `at` its path; the root without), a keyset page
  (default 200, at most 1000), folders first. 202 `{ read }` while the catalog is built (the client asks again every
  600 ms, not while the tab is hidden); 409 `not-explorable` (the bytes aren't an archive SFE reads, `.gz` alone, a
  compressed tar over the limit); 422 `archive-unreadable` (the build failed: asked again after a minute). The answer
  carries the summary (`complete`, `stopped`, `needsPassword`, `entries`, `leftOut`), the folder with its totals, its
  trail (`Trail(id, 4)`: the last four folders and how many skipped), the items and `next` (an opaque cursor:
  base64url of `d|f`, id, `\n`, name).
- `GET api/files/explore/entry/{archive}/{id}/{name}` (`files.download` + `files.view` at the archive, `RequestLimits.Media`):
  `archive` = `EpubEntries.Token` of the path, `id` the entry's catalog id, `name` must be its name (404 otherwise; it
  gives the browser the file name). Only when the catalog is ready. `FilePolicy` sandbox, inline only for
  `LibraryEndpoints.OpensInline` types (an `.html` entry is an attachment), `?download=true` always an attachment;
  over `MaxPreviewMegabytes` only with `download` (413). `Size / Packed > MaxCompressionRatio` refused (422), only when
  the format gives packed sizes per entry (solid 7z/rar give 0: the declared size still bounds the data).
  `private, no-cache` + ETag (catalog key, id): 304 while the archive is the same.
- `/api/config` `archives: { maxCompressedTarMegabytes }` (with `files.view`): the client's `isExplorable` rule.

## Catalog store (`ArchiveCatalogs`, one per library)

- `{Thumbnails:CacheDirectory}/archives/v{Version}/{KeyOf(path, size, mtime)}.db` (the key hashes the path and the
  file's size and time, not its content: a whole hash would read the archive). Older `v*` folders deleted at the
  first build.
- `Get` answers Ready (the file is current), else Building (a build started now if none runs: one `Build` per key,
  shared by every caller) or Failed (kept a minute). Builds run off the request, through `ArchiveBuildGate` (one for
  the server, as `FfmpegLimits`; `CatalogBuilds`), waiting their turn; a build nobody asked about for `Abandoned` (a
  minute) is given up, waiting or running.
- Last use = the catalog file's write time (touched at most once a minute); after each build, past
  `CatalogMegabytes` the least recently used are deleted (one being read is skipped).

## Client

- `ARCHIVE_FORMAT` (priority 20, `page: true`) matches `isExplorable(item)`: `.zip .7z .rar .tar`, compressed tars up to
  the config's size, never `.gz` alone; off without `config.archives`.
- The view (host class `text-view archive-view`: the reader's colours, a flex column): toolbar (Up, the trail with
  `…` for skipped folders, Go to) as an overflow row whose items all stay; notes (password, stopped, left out); a
  `VirtualRows` grid (32 px rows) whose height is the folder's whole count from the first page, rows past those loaded
  drawn as placeholders, the next page (1000) asked when the drawn rows come within 60 of the end. Files open in a
  new tab (an `<a target=_blank>` on the entry route) with `files.download`; each row has its context menu (Open /
  Open in new tab, Download). Keys: ↑ ↓, Enter, Backspace (up); the view owns its keys (`data-owns-keys`).
- An `api.get` 409 resolves with its problem: the answer is told apart by shape (`items`, `read`, else a problem).

## Measured (2026-10-26)

- Real app (`archive-viewer.mjs`): a folder of 3 000 entries draws 33 rows, its scroll height is the whole folder's,
  scrolling to the end loads the pages; Go to 300 folders down shows "296 folders in between"; phones keep name and size.
