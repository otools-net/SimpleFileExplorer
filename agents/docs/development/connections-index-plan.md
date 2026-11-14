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

- [ ] How `SearchPredicateBuilder`, `FileSearchService` (keyset cursors, match count) and `TagService` are tied to
  `FileEntry`: choose between a shared interface or projection both row types satisfy and a second builder sharing
  the parser; measure the chosen one on a few million remote rows on both providers
- [ ] What Dropbox metadata gives (dimensions, duration, `content_hash`, `media_info` being deprecated or not) and
  what its thumbnail API covers (formats, sizes, batch)
- [ ] Sizes: a first sync of a large Dropbox (calls, time, rows per batch); the cursor after long pauses (reset)

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
  used by three libraries is cached three times unless the cache of connections is server level (decide in phase 0).
- The picture editor never loads through another origin ([picture-editor](../features/picture-editor.md)): remote
  pictures come through SFE's own routes, so the canvas stays clean, but saving back is left for later.

## Left for later (each a small plan or a bullet when wanted)

- Trickplay, fingerprints, duplicates and similar videos across locations; missing-file tracking for connections.
- Default tags of folders and folder covers in connections; the picture and text editors saving to a connection.
- One merged, sorted search across locations.
- More providers: WebDAV, SFTP/SCP, FTP, Google Drive (how to add one:
  [connection-providers](../features/connection-providers.md)).
