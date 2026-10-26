# Archive viewer (exploring archives)

Code: server `Api/Endpoints/ExploreEndpoints.cs` (`api/files/explore`), `ArchiveCatalogs` (store) in
`Core/Files/Compression/`, `ArchivesConfig` in `ConfigEndpoints.cs`; client `app/formats/archive/archive-view.component.ts`,
`ARCHIVE_FORMAT` / `isExplorable` / `setArchives` in `formats/formats.ts`, `pathToken` in `core/paths.ts`, the archive part of
`styles/reader.css`. Tests: `ExploreRouteTests`, `ArchiveCatalogsTests`, `archive-view.component.spec.ts`, rows in
`formats.spec.ts`; real app: `agents/tools/visual-check/archive-viewer.mjs`; docs scene `archive`. User docs:
`docs/user-guide/archives.md`. The core (formats, names, catalogs): [archive-reading](archive-reading.md); what is left:
[archives-next-plan](../development/archives-next-plan.md).

## Routes

- `GET api/files/explore?path=&folder=|at=&after=&limit=` (`files.view`, `FolderScoped`; missing, hidden, server
  files, work folder → 404): one folder of the archive (id, or `at` its path; the root without), a keyset page
  (default 200, at most 1000), folders first. 202 `{ read }` while the catalog is built (the client asks again every
  600 ms, not while the tab is hidden); 409 `not-explorable` (the bytes aren't an archive SFE reads, `.gz` alone, a
  compressed tar over the limit); 422 `archive-unreadable` (the build failed: asked again after a minute). The answer
  carries the summary (`complete`, `stopped`, `needsPassword`, `entries`, `leftOut`), the folder with its totals, its
  trail (`Trail(id, 4)`: the last four folders and how many skipped), the items and `next` (an opaque cursor:
  base64url of `d|f`, id, `\n`, name).
- Limits per caller: pages share `RequestLimits.Media` (8 at once, queued); a read (catalog build) starts at most
  `CatalogBuildLimits.PerMinute` (30) a minute (`ArchiveCatalogs.Get(…, mayStart)` asks only before starting one: an
  archive read already never counts) → `ArchiveCatalogStatus.Limited` → 429. List, unlock and extract (chosen entries)
  pass the limiter; the entry route passes `() => false`: it never starts a read (404 until the list is read).
- `GET api/files/explore/entry/{archive}/{id}/{name}` (`files.download` + `files.view` at the archive, `RequestLimits.Media`):
  `archive` = `EpubEntries.Token` of the path, `id` the entry's catalog id, `name` must be its name (404 otherwise; it
  gives the browser the file name). Only when the catalog is ready. `FilePolicy` sandbox, inline only for
  `LibraryEndpoints.OpensInline` types (an `.html` entry is an attachment), `?download=true` always an attachment;
  over `MaxPreviewMegabytes` only with `download` (413). `Size / Packed > MaxCompressionRatio` refused (422), only when
  the format gives packed sizes per entry (solid 7z/rar give 0: the declared size still bounds the data).
  Reaching an entry of a solid 7z/rar or a compressed tar unpacks the ones before it: their declared sizes (7z: back to
  the block's start, `IsSolid` = shares the previous one's block; rar's reader marks every file of a solid archive
  solid; plain tars and other rars seek) past `MaxSkippedMegabytes` (2048) → 413 "extract it", checked before each skip
  (`ArchiveException.TooFar`). The unlock check takes a password unchecked when its entry is that far.
  `private, no-cache` + ETag (catalog key, id): 304 while the archive is the same. The checks are `OpenEntryAsync`,
  shared with the text route.
- `GET api/files/explore/entry/{archive}/at/{**at}`: the same, the file found by its path in the archive
  (`ArchiveCatalog.FindFile`: as written, else any case; a folder or nothing: 404): what the links and pictures of a
  document of the archive lead to. `OpenEntryAsync` takes an `EntryKey` (id and name, or path).
- `GET api/files/explore/text?path=&id=|at=&encoding=` (same rights and checks, `X-SFE-Archive-Unlock`): a `TextFile` as
  `api/files/text` answers (`TextCodec.Decode`), up to `TextEndpoints.MaxBytes` (413, no parts), 415 not text,
  `access` all false (never saved), `version` the entry's ETag.
- Items have `media` (`image`/`video`/`audio` by `MediaClassifier`, the library's `Sfe:FileTypes`): the client
  knows no media extensions.
- `/api/config` `archives: { maxCompressedTarMegabytes }` (with `files.view`): the client's `isExplorable` rule.

## Search (`GET api/files/explore/search?path=&q=&after=&limit=`)

- `ArchiveCatalog.Search(text, after, limit, budget)`: no index on names (a `LIKE '%x%'` couldn't use one anyway): the
  `Folders`, then the `Entries`, looked through by ranges of 20 000 ids (`SearchChunk`), `holds(Name, $text)` a .NET
  function (`OrdinalIgnoreCase`: any letters, not only ASCII); a page ends when full (its `next` = the last found) or
  when its time is spent (1 s, `SearchBudgetMilliseconds`; `next` = the end of the range looked at, maybe with
  nothing found). The cursor is `d<id>`/`f<id>`. Each item's `where` (its folder's path) from one walk up per folder,
  shared by the page's results, at most `WhereLevels` (8) folders: deeper, `…/` and the last 8 (a result 9 999 down
  cost ~1 s when whole); `in` is its folder's id, which the client opens ("in …" by id, not by path: no address limit).
- **One folder** (`folder=<id>`, 404 when there is none): `ArchiveCatalog.FindIn(folder, text, after, limit, budget)`
  looks only at what is right inside it, through its own index `(ParentId/FolderId, Name, Id)` in ranges of 20 000
  names (the range's end: `LIMIT 1 OFFSET 19 999` on the index, then `holds` up to it), folders then files, in the
  folder's order; the same 1 s budget; `next` is a listing cursor (base64, as `GET explore`'s), no `where`. Chosen over
  a prefix range (`Name >= x AND Name < x+1`), cheaper but another meaning than Find's "holds", and NOCASE folds A–Z
  only. Client: the folder button beside Find (`data-find-here`, `findHere`), the search asked again at once.
- `files.view`, `RequestLimits.Search` (240 a minute), 2 to 256 characters (400), 202 while the list is read, the
  read limit as for listing. Tests: `ArchiveCatalogTests` (any case, paging with tiny ranges and no time),
  `ExploreRouteTests.AnArchiveIsSearchedByNameWithTheFolderOfEachFound`; one folder:
  `ArchiveCatalogTests.AFolderIsSearchedAloneInItsOrderPageAfterPage`, `ExploreRouteTests.AFolderOfAnArchiveIsSearchedAloneWithListingCursors`.

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

## Passwords

- `POST api/files/explore/unlock {path, password}` (`files.view`, `RequestLimits.Unlock`: 10 a minute per caller, as
  7z derives its key with 2^19 SHA-256 rounds, ~0.3 s of CPU a try: guessing is also a way to load the server;
  429): tried on the archive (`Opens`): an encrypted list, its first entry read; else the smallest encrypted file
  (`ArchiveCatalog.SmallestEncrypted`) read through, up to 16 MB (zip's checks come first, AES' at the end). 422
  `wrong-password` (logged as a warning with who and where from, never the password), 409 `not-protected`, 202 while
  the list is read; else `{ unlock }`.
- `ArchiveUnlocks` (Api, one for the server): 32 random bytes as hex → the password, its library, its owner
  (`user:{id}:{stamp}`: a new password ends it; a visitor without an account: `address:{ip}`) and the archive's full
  path, size and time (a changed archive ends it); 15 idle minutes; 20 per owner; `Forget(owner)` in
  `SfeSession.SignOutAsync`.
- Pages send the unlock in `X-SFE-Archive-Unlock`, entry links as `?unlock=` (an `<a>` sends no header). An archive
  whose list is encrypted (`summary.NeedsPassword` on its disk catalog, which holds nothing) is answered from
  `ArchiveCatalogs.GetInMemory` (key: unlock + archive; read with the password through the same gate; at most
  `MaxInMemory` 4 per library, the least used dropped; 15 idle minutes; `MaxEncryptedListEntries`); its ids are its
  own. An encrypted entry without a valid unlock: 403 `wrong-password`.
- The listing says `protected` (some files encrypted, or the list) and `unlocked` (the unlock sent opens it).

## Client

- `ARCHIVE_FORMAT` (priority 20, `page: true`) matches `isExplorable(item)`: `.zip .7z .rar .tar`, compressed tars up to
  the config's size, never `.gz` alone; off without `config.archives`.
- The view (host class `text-view archive-view`: the reader's colours, a flex column): toolbar (Up, the trail with
  `…` for skipped folders, Go to) as an overflow row whose items all stay; notes (password, stopped, left out); a
  `VirtualRows` grid (32 px rows) whose height is the folder's whole count from the first page, rows past those loaded
  drawn as placeholders, the next page (1000) asked when the drawn rows come within 60 of the end. Files open in a
  new tab (an `<a target=_blank>` on the entry route) with `files.download`; each row has its context menu (Open /
  Open in new tab, Download). Keys: ↑ ↓, Enter, Backspace (up); the view owns its keys (`data-owns-keys`).
- **In place** (`viewing`, `.archive-entry`: an overlay over the list, which stays as it was): pictures (`media`
  image: an `<img>` on the entry route) and the formats of `IN_PLACE` (text, prose, Markdown, JSON, CSV, subtitles,
  PDF) by `formatOf` on a `LibraryItem` made for the entry (path `<archive>#<id>`: positions per entry), their
  component loaded on demand with an injector giving `FILE_SOURCE` (`formats/file-source.ts`: `library: false`, text
  from `explore/text`, bytes and download from the entry route, with the unlock) and a `FILE_VIEW_HOST` allowing only
  `files.view`/`files.download`. A plain click on the name (the link's default prevented) or Enter; Ctrl / middle
  button: the tab. Bar: Back (Esc, unless in an editor or a box), the name, before / after among the rows loaded
  (← → for pictures), new tab, download. Videos, audio, EPUB, nested archives: a tab or a download.
- `FILE_SOURCE` (default: the library) is what the format views read through (`textFile`, `original`, `download`);
  `library: false` hides Follow and parts (a 413 is a message), skips `TextSession.checkDisk` and the subtitles'
  video. `documents` (an entry's): the document's path in the archive (`listing.path`: the first page of a folder at
  most `MaxPathDepth` 64 deep gives its whole path; a search result's whole `where`), from which Markdown resolves its
  relative links (`resolveLibraryPath`: out of the archive → text) to the `at` route; viewable ones open in place
  through `FileViewHost.open` (the archive view's `entryHost`), `Viewing.back` returning to the document. No path
  known (deeper folders, shortened `where`): links and pictures are text.
- **Open as archive** (item menu, `openAsArchive`): `formatOf(item, { asArchive })` gives `ARCHIVE_FORMAT` for
  `isContainer` (a name in `CONTAINER_EXTENSIONS`: `.cbz .cbr .epub .docx .odt .jar .apk .nupkg`…, not explorable
  already); the viewer keeps it as `asText` (`opened().asArchive`; its new tab is `view?path=…&asArchive=1`). The
  server needs nothing: `ArchiveFormats.Detect` reads the bytes, a name lying gives 409 `not-explorable`.
- An `api.get` 409 resolves with its problem: the answer is told apart by shape (`items`, `read`, else a problem).
- **Find** (`data-find`, after 300 ms and 2 characters): `found` replaces the folder's rows in the same grid (each
  with `in <where>`, a button opening that folder through `at`); pages go on as rows scroll in, and at once when a
  page cut short by the server's time came back empty; `endFind()` (Esc, entering a folder, Go to, another archive)
  aborts it and empties the box.
- Passwords: the form shows by itself for an encrypted list, or from **Enter the password** / a click on a locked
  file (a button then, not a link); the unlock is a signal of the view only (dropped with another archive), sent with
  every request and entry link; zips say their names stay readable; 422 `wrong-password` and 429 have their messages.

## Measured (2026-10-26)

- Real app (`archive-viewer.mjs`): a folder of 3 000 entries draws 33 rows, its scroll height is the whole folder's,
  scrolling to the end loads the pages; Go to 300 folders down shows "296 folders in between"; phones keep name and size.
