# Plan: index, search, tags and previews of connection files

Files: `src/Sfe.Core/Data/SfeDbContext.cs`, `Data/Migrations/`, `DatabaseInitializer`, new
`Core/Connections/Indexing/`, `Core/Search/`, `Core/Tagging/`, `Core/Thumbnails/`, `Core/Streaming/`, `Core/Tasks/`;
`Endpoints/Connection*Endpoints.cs`; client `app/library/` (search, viewer), `app/tags/`, `app/player/`,
`app/formats/`. Part 6 of the [epic](external-access-epic-plan.md); builds on the connections as built
([connections](../features/connections.md); plan 5, done 2026-11-14). Delete this plan once its last item is ticked.

## Decisions

- **Index rows of a connection live in the library's database**, in their own tables (`RemoteDirectories`,
  `RemoteFiles`, `RemoteFileTags`), keyed by connection id. The local tables (`Directories`, `Files`, `FileTags`) are
  not touched: scans, duplicates, missing-file tracking and similar videos never see remote rows.
- Tags are **the library's** (`Tags`, parents, aliases, colors): one vocabulary, one picker.
- An account-level connection is indexed in each library where its owner opens it, on first use (metadata only);
  its rows there are visible to the owner only.
- The index is filled from the provider's **metadata** (name, size, dates, content hash, media info when given) and
  kept current by its **change cursor**; no file is downloaded to index it.
- Search runs on **one location at a time**; "Everywhere" runs the same query on the library and each visible
  connection and shows the results **grouped by location** (count and first page per group, more on demand). No
  query sorts local and remote rows together.
- Previews: content streamed through SFE; thumbnails from the provider when it makes them; a **bounded content
  cache** for what ffmpeg must read (video thumbnails, conversion while playing).
- Not built: trickplay, fingerprints, duplicates, similar videos, missing-file tracking, default tags of folders,
  folder covers and the picture editor's save on connection files (each listed in "Left for later").

## Progress

### Phase 0: checks (results written here)

- [x] How `SearchPredicateBuilder`, `FileSearchService` (keyset cursors, match count) and `TagService` are tied to
  `FileEntry`: choose between a shared interface or projection both row types satisfy and a second builder sharing
  the parser; measure the chosen one on a few million remote rows on both providers
- [x] What Dropbox metadata gives (dimensions, duration, `content_hash`, `media_info` being deprecated or not) and
  what its thumbnail API covers (formats, sizes, batch)
- [x] Sizes: a first sync of a large Dropbox (calls, time, rows per batch); the cursor after long pauses (reset)
- [ ] A person confirms the sync numbers below on a real large Dropbox with
  [dropbox-check](../../tools/dropbox-check/README.md) before the release (no account was available in phase 0)

As built (2026-11-15; tool: [connection-index-measure](../../tools/connection-index-measure/README.md)):

**Search shape: (a), a generic builder over an interface**

- The builder reads only `Name`, `FullName`, `Extension`, `Size`, `LastWriteTime`, `Width`, `Height`, `Duration`,
  `Pixels` and `TagLinks.TagId`; `FileSearchService.Order` / `After` / `ReadAsync` read the same columns.
- Chosen: `ISearchableFile<TSelf>` with those properties (same names on `FileEntry` and `RemoteFile`), plus a
  `static abstract HasAnyTag(int[])` per type (the tag navigation differs); the builder, `Order`, `After` and the
  keyset become generic `where T : class, ISearchableFile<T>`; the row projection with tags stays per type.
- Why not (b): a second builder copies ~285 lines and every future term twice (the negation rule of
  [search-terms](../features/search-terms.md) would drift); (a) changes signatures only.
- EF translates interface members of a generic `T` on both providers; the tool checks a generic predicate gives
  **the same SQL text** as one written on the entity: the local search's SQL can't change. Phase 3 adds that check
  as a test on `FileEntry` (`ToQueryString` before / after, both providers).
- Interface names are mapped columns: `RemoteFile.FullName` is the lowered path (the key, compared and sorted by,
  [Traps](#traps)); the display path is another column. An explicit interface property would not translate.
- `InTree` builds `Path.DirectorySeparatorChar` patterns: remote rows need a `/` variant (a parameter).

**Measured: 3 000 000 rows, 2 400 000 in the connection searched** (ms, median of 5; page = keyset page of 100 with
tags; count = `Take(10001).Count`)

| Query | SQLite page | SQLite count | SQL Server page | SQL Server count |
|---|---|---|---|---|
| name contains a common word, path sort | 16 | 117 | 12 | 1 112 |
| same, name sort, 2nd page (keyset) | 2 | 118 | 4 778 | 581 |
| name contains a word matching nothing | 4 427 | 399 | 9 361 | 4 091 |
| `type:video s:>3GB`, size descending | 1 | 124 | 753 | 88 |
| `e:jpg` (40% of files), modified descending | 1 161 | 1 | 2 300 | 3 |
| a tag (1% of files, 3 ids), modified descending | 27 | 2 056 | 95 | 255 |
| a tag and a word (737 matches), path sort | 1 059 | 672 | 112 | 4 310 |
| a folder and below, path sort | 270 | 259 | 1 | 5 |

- Indexes (all lead with `ConnectionId`, so no query reads another connection's rows): unique `(ConnectionId,
  FullName)` and `(ConnectionId, ProviderId)`; `(ConnectionId, DirectoryId)`, `Name`, `Extension`, `Size`,
  `LastWriteTime`, `Duration`, `Pixels`; `RemoteFileTags` key `(FileId, TagId)` and index `TagId`.
- Common terms are fast (the sorted index is walked until 101 matches); slow: a word matching little (`LIKE '%x%'`
  reads every row of the connection), a broad filter sorted by another column (`e:jpg` by date sorts ~1 M rows), and
  SQL Server's keyset on `Name` (`Name > @v OR Name = @v AND …` is not a seek). The local search has the same
  shapes; phase 3 measures it the same way before tuning both.
- Tuning for phase 3: the tag term as `Id IN (SELECT FileId FROM RemoteFileTags WHERE TagId IN …)` (starts from
  the `TagId` index; the `EXISTS` form scans the connection: SQLite count 2 s); the folder term as a range on the
  lowered path (`>= 'a/b/' AND < 'a/b0'`) instead of `LIKE … ESCAPE`, which SQLite can't seek (270 ms).
- Size: SQLite 1.5 GB (~510 bytes a row), SQL Server 3.1 GB (~1 KB a row, `nvarchar`) for 3 M rows with tags.

**Dropbox facts** (SDK `Dropbox.Api` 7.4.0 XML docs; spec `files.stone` of github.com/dropbox/dropbox-api-spec;
docs.dropboxapi.com/dropbox-api/docs/performance)

- Listing gives per file: `id`, `path_lower`, `path_display`, `size`, `server_modified`, `client_modified`, `rev`,
  `content_hash`, `is_downloadable`. Folders have no date.
- `media_info` (dimensions, `time_taken`, GPS, video `duration` in ms) is **not set by `list_folder` /
  `list_folder/continue` / `get_thumbnail_batch` since 2019-12-02**; `include_media_info` there has no effect
  (`FileMetadata.MediaInfo`, `ListFolderArg.IncludeMediaInfo`). Still given by `get_metadata(include_media_info)` and
  by `get_thumbnail` / `get_thumbnail_v2` (unless `exclude_media_info`); it can answer `pending`.
- So width, height and duration are not in the first sync: they come when SFE fetches one thumbnail (its metadata,
  free) or passes the file through the content cache; never a `get_metadata` per file (1 M calls).
- Thumbnails: `get_thumbnail(_v2)` and `get_thumbnail_batch` (**at most 25** a call, `too_many_files`; base64 in
  JSON); images only, extensions jpg, jpeg, png, tiff, tif, gif, webp, ppm, bmp (SDK 7.4.0; the spec lists fewer);
  photos over **20 MB** get none (`unsupported_image`). Video thumbnails are not documented: check them by hand.
- Thumbnail formats jpeg (default), png, webp (`preserve_transparency` for png/webp, v2 only); sizes 32×32, 64×64,
  128×128, 256×256, 480×320, 640×480, 960×640, 1024×768, 2048×1536; modes strict, bestfit, fitone_bestfit,
  original.
- `list_folder`: `limit` 1–2000 and approximate ("slightly more" possible); `DropboxStorage.PageSize` is 500 for
  browsing, the sync asks 2000. `recursive` lists the whole tree, in an order to apply as is (file = store, folder =
  store without touching children, deleted = remove it and its children); the SDK warns it may fail on very large
  trees, the fallback being folder by folder.
- Pages are chained by the cursor: one listing is sequential. Many simultaneous identical calls by one app and user
  answer a rate limit.
- Cursor: no documented lifetime; it can be invalidated at any time (`reset` from `list_folder/continue` and
  `longpoll`; maintenance, a moved app folder): then a full pass matched by `id` (see Traps). `DropboxStorage
  .ChangesAsync` already answers `Reset`. Keep the cursor's date to log how old a reset one was.
- `list_folder/longpoll`: `timeout` 30–480 s (default 30) plus up to 90 s of jitter, `backoff` seconds to honour, no
  auth, host `notify.dropboxapi.com` (not in the rule hosts today); not needed: the scheduled task polls `continue`.
- Rate limits: numbers not published, per linked user (per team for team apps); 429 `too_many_requests` with
  `Retry-After` always (already `RateLimited` + `RetryAfter`); `too_many_write_operations` for parallel writes in one
  namespace (already serialised).

**First sync of 1 M files** (estimate, not measured on Dropbox)

- Calls: ~1.05 M entries (files and folders) / 2000 = **~525 sequential `list_folder/continue` calls**; at 1–2 s a
  page (assumed) **10–20 minutes**, ~0.5–1.5 MB of JSON a page.
- Rows per batch: one page (≤ 2000 entries) written in one transaction with the cursor and progress, so a restart
  resumes at the last page; nothing held across pages (bounded memory).
- Writing never holds the sync back: measured 3.5 k rows/s with EF (`AddRange`, `SaveChanges`, `ChangeTracker
  .Clear`, as `DirectoryIndexer`) on both providers at 50 k rows; a prepared `INSERT` / `SqlBulkCopy` 16 k / 14 k
  rows/s at 50 k. Into a table growing to 3 M: SQLite 2.6 k rows/s (19 min; the indexes on random keys), SQL Server
  12.5 k rows/s (bulk). 1 M rows ≈ 5–6 min on SQLite, under the listing's time: EF is enough.
- Memory: managed heap ≤ 14 MB and working set flat (70–110 MB) over 3 M rows in batches of 1 000.
- To confirm by a person with a real account: seconds per page of 2000, recursive on a large tree (or its errors),
  thumbnails of videos, `media_info` from `get_thumbnail_v2`, and a `reset` after a pause of weeks.

**Caches: one store per connection, server-wide** (decides the cache trap below)

- Connection ids are server-wide (the access database), and a remote file's bytes and thumbnails are the same in
  every library: a `ConnectionCache` keyed by connection, provider id and `rev`, under `{CacheDirectory}/connections
  /{id}/`, registered in the application and handed to each library like `FfmpegLimits`
  ([tenants](../architecture/tenants.md)); all levels use it, so a server connection is cached once, not per library.
- The cache never decides access: every route asks `ConnectionAccess` and the library's folder rules first, then
  reads it. Its keys never come from the request (the server looks up id and `rev` in the library's index row).
- One size limit for the server, a share per connection, removed with the connection; index rows stay per library.

### Phase 1: tables and sync

- [ ] Entities and both upgrade paths (SQL Server migration; SQLite tables **and indexes** created in existing
  files); path columns within `SfeDbContext.MaxPathLength`, longer remote paths reported, not truncated
- [ ] `ConnectionIndexer`: first listing in batches (resumable after a restart: cursor and progress stored), then
  changes by cursor; renames keep rows (provider ids) so tags stay on their files; cursor reset = a full pass that
  keeps tags by id
- [ ] A background task per connection and library ("Connection index"), with a schedule, run history and logs
  ([background-tasks](../features/background-tasks.md), [schedules](../features/schedules.md)); started at first
  open; a "Sync now" action
- [ ] Changes made through SFE (upload, rename, move, delete, transfers) update the rows at once; the change feed
  tells open listings ([change-feed](../features/change-feed.md))
- [ ] Removing a connection, or its offer to a library, removes its rows there (batched)

### Phase 2: listing from the index

- [ ] Listings of a connection read the index when it is complete for that folder (sorts, counts, paging as the
  library's), the provider otherwise; files not in the index yet shown as the library does
  ([not-indexed-marks](../features/not-indexed-marks.md))
- [ ] Folder rules filter remote rows (`Visible()` on queries, `Seen()` on shown items) through `ConnectionAccess`

### Phase 3: search

- [ ] The query language on remote rows (name, type, size, dates, `w:` `h:` `r:` `d:` when known, tags), same sorts,
  cursors and match count ([search-terms](../features/search-terms.md), [search-sort](../features/search-sort.md))
- [ ] Routes under `api/v1/connections/{id}/search`; the client's search box searches the current location; search
  explain and history unchanged
- [ ] "Everywhere": a scope switch; one request per location from the client (each paged alone), results in groups
  with the location's name; only locations the person may use; a slow or unreachable location never holds the others

### Phase 4: tags

- [ ] `RemoteFileTags` with the tag dialogs, picker, quick tagging and scope tagging on remote rows
  ([tags](../features/tags.md), [scope-tagging](../features/scope-tagging.md)); tag counts say what they count
- [ ] Deleting or merging a tag covers remote rows; tag rights as for library files
- [ ] A file moved between the library and a connection by a transfer keeps its tags (copied to the other table)

### Phase 5: previews

- [ ] Thumbnails: the provider's, cached in the thumbnail cache under a key of connection, id and revision
  ([thumbnails](../features/thumbnails.md)); pictures the provider doesn't thumbnail made from the content cache
- [ ] Content cache (`ConnectionContentCache`): size and age limits (settings), eviction, one download per file at a
  time, per-caller limits, never under a folder the app serves; ffmpeg reads it only through the checks of
  `Ffmpeg.LibraryInputAsync` (extended to this cache, real media formats only)
- [ ] Viewer and player on a connection: pictures, video (direct when the browser plays it, else
  [video-streams](../features/video-streams.md) from the cache), audio, text read-only first, then the formats of
  `app/formats/` through `FILE_SOURCE`; each format listed with what works
- [ ] Width, height and duration filled when a file passes through the cache (never by a pass downloading everything)

### Phase 6: close

- [ ] The C# library: search and tags over a connection location
- [ ] Feature notes (`connection-index.md`, `connection-previews.md`), [security](security.md) vectors, `docs/`
  pages with screenshots; this plan deleted

## Attack vectors to test

- Search, counts, tag counts and "Everywhere" never tell about rows of a connection, or of its hidden folders, to
  someone who can't see them (including an account-level connection's rows to the library's administrators).
- A private connection's thumbnails and cached content by another account (keys guessed, cache paths, links).
- The content cache filled by one caller; a huge file; many parallel previews; a file that changes while cached.
- A remote `.html`, `.svg`, playlist or script named as media given to the viewer or ffmpeg.
- Rows left after a connection is removed, disabled, or no longer offered to a library.

## Traps

- **Scale:** a remote folder can hold a million entries: batches, keyset paging, no per-request work proportional to
  it ([scale](scale.md)); the first sync must not hold a transaction or memory for the whole listing.
- SQLite upgrade path creates only new tables, indexes and nullable columns ([database](database.md)); SQL Server
  needs max lengths on indexed strings and one cascade path.
- Dropbox compares paths case-insensitively: index and compare on the lowered path, show the display path.
- A cursor can expire or be reset by the provider: the full pass must match rows by provider id, or tags are lost.
- Two libraries indexing one account-level connection each call the provider: share one listing pass per connection
  when both are due, and stay inside its rate limits.
- Thumbnail and content caches are per library ([tenants](../architecture/tenants.md)): a server-level connection
  used by three libraries would be cached three times; decided in phase 0: one server-wide store per connection.
- The picture editor never loads through another origin ([picture-editor](../features/picture-editor.md)): remote
  pictures come through SFE's own routes, so the canvas stays clean, but saving back is left for later.

## Left for later (each a small plan or a bullet when wanted)

- Trickplay, fingerprints, duplicates and similar videos across locations; missing-file tracking for connections.
- Default tags of folders and folder covers in connections; the picture and text editors saving to a connection.
- One merged, sorted search across locations.
- More providers: WebDAV, SFTP/SCP, FTP, Google Drive (how to add one:
  [connection-providers](../features/connection-providers.md)).
