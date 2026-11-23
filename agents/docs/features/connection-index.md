# Index of connection files (sync, listings, search, tags)

Code: Core `Connections/Indexing/` (`ConnectionIndexer`, `ConnectionIndexes`, `ConnectionIndexWorker`,
`ConnectionIndexUpdates`, `ConnectionChanges`, `RemoteIndexKeys`, `RemoteIndexScope`, `RemoteIndexTransactions`,
`RemoteNameKeys`, `RemoteIndexListings`, `IndexListing*`, `RemoteIndexSearch`, `RemoteSearchScope`,
`RemoteFileTags`, `RemoteScopeTagger`, `RemoteFileTagRows`, `RemoteTagTerms`, `RemoteTagPlace`,
`GatedIndexSource`), `Data/Entities/Remote/`, `Data/RemoteIndexModel`, `Core/Search/` (`ISearchableFile`,
`SearchPages`, `SearchCursor`, `SearchTagTerms`, `SearchTermSettings`), `NaturalComparer.KeyOf`,
`Transfers/LibraryTransferTags`, `TransferTagSides`; Api `ConnectionIndexEndpoints`, `ConnectionBrowsing`,
`ConnectionSearchEndpoints`, `ConnectionTagEndpoints`, `ConnectionTagChanges`, `ConnectionFeed`; client
`connections/connection-index.ts` (Manage panel), `library/everywhere/`, `topbar/search-scope.ts`,
`core/connection-place.ts` (`tagsRoute`). Tests: `ConnectionIndexerTests`, `ConnectionIndexUpdateTests`,
`ConnectionIndexRouteTests`, `RemoteIndexUpgradeTests`, `RemoteIndexListingsTests` (+ `…SqlServerTests`),
`RemoteIndexSearchTests` (+ `…SqlServerTests`), `SearchSqlTests`, `ConnectionSearchRouteTests`, `RemoteFileTagsTests`,
`ConnectionTagRouteTests`, `TransferTagRouteTests`, e2e `everywhere*.spec.ts`, `search-scope.spec.ts`,
`connection-tags.spec.ts`. Measuring tool: [connection-index-measure](../../tools/connection-index-measure/README.md);
real account: [dropbox-check](../../tools/dropbox-check/README.md#index-of-a-large-dropbox). Previews:
[connection-previews](connection-previews.md). User docs: `docs/administration/connections.md`,
`docs/user-guide/search.md`, `docs/user-guide/tags.md`.

## Decisions

- A connection's index rows live **in the library's database**, in their own tables keyed by connection id; the
  local tables (`Directories`, `Files`, `FileTags`) are untouched, so scans, duplicates, missing files and similar
  videos never see remote rows.
- Tags are **the library's** (one vocabulary, one picker). An account's own connection is indexed in each library
  where its owner opens it; its rows there are its owner's only.
- Filled from the provider's **metadata** and kept current by its **change cursor**; no file is downloaded to index
  it. Width, height and duration come later, from previews ([connection-previews](connection-previews.md)).
- Search runs on **one location at a time**; "Everywhere" asks each location apart and groups the results. No query
  sorts local and remote rows together.
- Not built (later plans): trickplay, fingerprints, duplicates, similar videos, missing-file tracking, default tags
  of folders, folder covers, the editors saving to a connection, one merged sorted search across locations, other
  providers (WebDAV, SFTP/SCP, FTP, Google Drive: [connection-providers](connection-providers.md)).

## Tables and upgrade

- `RemoteDirectories`, `RemoteFiles`, `RemoteFileTags` and `RemoteIndexes` (the sync's state per connection: phase
  `Listing`/`Following`, pass number, change cursor and its date, the folder and page cursor a listing stopped at,
  counts, `TooLong`, `LastError`, root path and a SHA-256 of the provider's account: another root or account drops
  the rows first). SQL Server migrations `ConnectionIndex`, `ConnectionIndexListings`, `ConnectionIndexSearch`
  (`RemoteIndexUpgradeTests` on LocalDB); SQLite through `DatabaseSchema` (tables and indexes made in old files).
- `RemoteFile` carries the `ISearchableFile` names: `Name` without the extension (as `FileEntry`), `FullName` = the
  key (path lowered when the provider ignores case: Dropbox), `DisplayPath` to show. An explicit interface member
  would not translate in EF: the names are mapped columns.
- Paths over 260 characters are left out on **both** providers, counted in `TooLong`, logged per path (100 a sync;
  an account's own connection: the count only). Ids over 200 and hashes over 128 characters are not kept.
- Every index leads with `ConnectionId` (no query reads another connection's rows): unique `(ConnectionId,
  FullName)`, `(ConnectionId, ProviderId)`; listing indexes `(ConnectionId, DirectoryId, NameKey, FullName,
  Removed)` and the same with `LastWriteTime`, `Size`, `Extension` before `NameKey` (≤ 1452 bytes of key on SQL
  Server); folders `(ConnectionId, ParentId, NameKey, FullName, Removed)`; search `(ConnectionId, NameKey,
  FullName)`, `Name`, `Extension`, `Size`, `LastWriteTime`, `Duration`, `Pixels`; `RemoteFileTags` key `(FileId,
  TagId)` and index `TagId`. `Removed` is never a leading column: EF writes `NOT "Removed"` on SQLite, and the planner
  then walked the connection (checked with `EXPLAIN QUERY PLAN`).
- Size: SQLite ~510 bytes a row, SQL Server ~1 KB (`nvarchar`), measured on 3 M rows with tags.

## Sync (`ConnectionIndexer`)

- Listing **folder by folder** through `IRemoteStorage.ListAsync` (Dropbox pages of 500), not one recursive
  listing: one call per folder more than a recursive one. The change cursor is taken before the walk; pending folders
  are rows (`Pending`), so memory holds one page; each page is written in one transaction with `WalkFolderId` /
  `WalkCursor`, so a restart **resumes** at the last page written (a page cursor refused on resume lists that folder
  again from its start).
- Folders are keyed by path (only files carry tags): a folder renamed at the provider is a new row, its files
  re-pointed by id. Files by provider id first, then path; a row at a path another file now holds is set aside
  (`RemoteIndexKeys.Parked`) so a swap keeps it. Deletes mark rows `Removed`; they are deleted at the end of the sync
  (batches of 1000, deepest folders first). Every reader filters `Removed`.
- A full pass marks unseen rows (`Pass`) removed at its end. **A cursor reset** is a full pass matched by provider
  id, so files keep their tags; a second reset within one sync fails it. A provider without a change cursor is
  listed in full at each sync. Provider calls go through `TransferCalls` (gates, waits, retries).
- SFE's own changes (uploads, rename, move, delete, transfers) are told to `ConnectionChanges` (server-wide) and
  written at once by each library's `ConnectionIndexUpdates` (a folder moved: rows rewritten by prefix); the change
  feed tells open listings (`ConnectionFeed`). A folder copied by the provider is added unlisted; its items come with
  the next sync. During a full listing a folder SFE changed keeps its pass (`RemoteIndexScope.BySfe` /
  `PassUnderWay`), else the listing took it as met and dropped its other items (fixed in phase 2).
- While a folder of the connection is open in a tab, its changes are read every 30 s without a history run
  (`ConnectionWatchers`, `ConnectionIndexes.FollowAsync`: [change-feed](change-feed.md)).
- Task `ConnectionIndex` ("Connection index"): one history run per connection synced; Run now and schedules sync
  every connection the library indexes, one after the other; started by the browse route
  (`ConnectionIndexes.OpenedAsync`: first open, or older than 10 minutes; state read at most once a minute). Routes
  `GET api/v1/connections/{id}/index` and `POST …/index/sync` (the Manage panel's **Sync now**): rights
  `connections.own|library|server` + `ConnectionManagers.MayManage` + the library uses it.
- Logs under `Otools.Net.Sfe.Core.Connections.Indexing.ConnectionIndexer`: Debug per page (entries, ms reading, ms
  writing), a resumed listing, a cursor reset with its age.
- Removal: `ConnectionIndexWorker` prunes connections the library no longer reaches whenever
  `AccessChanges.Version` moved (checked every 30 s), batched. A disabled connection keeps its rows (no route reads
  them). Account-level connections stay indexed while they exist.

## Listings from the index

- A folder is listed from the index when **complete** (`RemoteIndexListings.CompleteFolderAsync`): state of the
  current root and account, phase `Following`, synced within `MaxAge` (30 min), the folder's row `Listed`, not
  `Pending`, not `Removed`. Otherwise from the provider, with `indexed` marks per item
  ([not-indexed-marks](not-indexed-marks.md)).
- Order: folders first, then files by the sort's value, then name, then key, **all in the same direction** (one
  index walked one way: ties of a descending sort go by name descending; the library's go ascending).
- **`NameKey`** (both tables): `NaturalComparer.KeyOf` = hex of `CompareInfo.GetSortKey(name, IgnoreCase |
  NumericOrdering)` (invariant culture), whose ordinal order is `NaturalComparer`'s; cut at 400 characters;
  `Latin1_General_BIN2` on SQL Server. Without ICU (invariant globalization: the AppImage on a system without it)
  the key is the upper-cased name, not natural order. Rows without a key are filled by `RemoteNameKeys` at the start
  of each sync; a folder holding one is listed from the provider. Keys depend on the ICU version: after an upgrade,
  old and new rows may order slightly apart until a sync rewrites them (paging reads stored keys: never wrong pages).
- Paging keeps `offset`/`limit`; `IndexListingPlaces` (per library, 1024 places, 5 min) remembers the last row of
  each page so the next one seeks after it (three seeks: an `OR` is not a seek on SQL Server); a page without a
  place uses `OFFSET`. Counts read at the first page.
- `ProviderPaging` keeps a scroll begun from the provider on the provider's snapshot; an index listing that stops
  being complete mid-scroll (a cursor reset) switches to the provider for later pages (the client drops duplicates).
- Folder rules: `IndexListingFilter` and `ConnectionFolders.HiddenIds()` as query conditions (totals and pages never
  hold hidden items), then `ConnectionFolders.Seen` on every shown item. The index path calls no provider and takes
  no gate turn.

## Search

- The library's builder and keyset are generic over `ISearchableFile<T>` (`FileEntry`, `RemoteFile`) with static
  abstract `SortName`, `SortNameMayBeMissing`, `IsTagged`, `HasAnyTag`; `SearchSqlTests` proves the library's SQL
  text unchanged on both providers (`tests/samples/search-sql/`).
- `GET api/v1/connections/{id}/search`: the query language, sorts, cursors and count of the library
  ([search-terms](search-terms.md), [search-sort](search-sort.md)); name sort by `NameKey`, rows without one after
  the others by path (cursor `/{path}`); answer `coverage`: `none` (no index of this root and account), `partial`
  (listing, or older than `MaxAge`), `complete`. A search starts the index and never calls the provider.
- Scope as conditions (`RemoteSearchScope`): `!Removed`, the connection, the folder (a range `[key/, key0)` on
  SQLite, `LIKE` plus the start on SQL Server, whose CI collation doesn't keep `/` and `0` adjacent), folder rules
  as key trees and hidden ids (also the trees of indexed folders with those ids: renamed outside SFE).
- Tag term rewritten by `RemoteTagTerms` into `Id IN (SELECT FileId FROM RemoteFileTags WHERE TagId IN …)` (the
  `EXISTS` form scanned the connection).
- Client: a search keeps `?c=`; `?all=1` is **Everywhere**: `EverywhereSearch` asks the library and each usable
  connection apart (20 a page, Show more), each group with its own loading, error (Retry), count and coverage note.
  Everywhere rows have no thumbnails; on phones the scope is under the search's ⋯ › Search in.

## Tags

- `api/v1/connections/{id}/file-tags` (GET, POST) and `…/file-tags/scope` (GET, POST): the library's bodies and
  answers, paths from the connection's root. `tags.view` / `tags.assign` at each path by the connection's folder
  rules, by path and again by provider id; one file refused fails the change (400); `tags.assign` is `AtFolder`
  Read. Rows read are of the current root and account only (`RemoteTagPlace.IsCurrentAsync`).
- A file the index doesn't hold (POST): asked of the provider (one gate turn, at most 20 a request), written into the
  index, then tagged; 409 **`connection-not-indexed`** when the library has no index of the connection (its first
  sync is started) or more files are missing. A scope (folder, tree, search) changes only rows the index holds.
- **Tag counts** (`api/tags`, the sidebar) stay **library files only**: a count over connections would need each
  asker's access and rules, and could tell about an account's own connection. Connection files are counted per
  connection by `…/file-tags/scope`.
- Writes in batches of 1000 under the connection's lock (`RemoteIndexTransactions.WriteAsync`); deleting a tag
  deletes its remote links first, 1000 a statement.
- Transfers carry tags (`ITransferEndpoint.TagsAsync` / `TagAsync`): given to each file once written and verified;
  the arriving file's row is made at once from the provider's answer. A connection this library never indexed has no
  rows to hold them: files arrive **without their tags** (logged, "arrived without its tags"). A moved library file
  keeps its tags in SFE's trash.

## Measured (phase 0, local, 3 M rows, 2.4 M in the connection searched; ms, median of 5)

| Query | SQLite page | SQLite count | SQL Server page | SQL Server count |
|---|---|---|---|---|
| name contains a common word, path sort | 16 | 117 | 12 | 1 112 |
| same, name sort, 2nd page (keyset) | 2 | 118 | 4 778 | 581 |
| name contains a word matching nothing | 4 427 | 399 | 9 361 | 4 091 |
| `type:video s:>3GB`, size descending | 1 | 124 | 753 | 88 |
| `e:jpg` (40% of files), modified descending | 1 161 | 1 | 2 300 | 3 |
| a tag (1% of files), modified descending | 27 | 2 056 | 95 | 255 |
| a tag and a word (737 matches), path sort | 1 059 | 672 | 112 | 4 310 |
| a folder and below, path sort | 270 | 259 | 1 | 5 |

- Slow shapes (the library's search has them too): a word matching little (`LIKE '%x%'` reads every row), a broad
  filter sorted by another column, SQL Server's keyset on `Name`. Tuned since: tag term as `IN` (SQLite count of a
  tag 1 074 → 109 ms), folder term as a key range on SQLite (270 ms → a seek).
- Writing never holds the sync back: EF 3.5 k rows/s (`AddRange`, `SaveChanges`, `ChangeTracker.Clear`); into a table
  growing to 3 M, SQLite 2.6 k rows/s. Memory flat (managed heap ≤ 14 MB, working set 70–110 MB over 3 M rows).

## Measured on a real Dropbox (2026-11-19)

App folder account filled by `seed.mjs` (2 546 files, 76 folders; one folder of 1 300, a tree 10 deep), SQLite,
this machine ([dropbox-check](../../tools/dropbox-check/README.md#results)). Not measured yet: 100 000 files or more,
a cursor reset after weeks, SQL Server.

- **First sync**: 79 calls for 2 621 entries, ~30 s (26 s reading, 2 s writing); a call took 274 ms median, 306 ms
  p90, one 4 s (no 429, no retry); full pages of 500 about 450 ms read, 150 ms written. Calls ≈ folders + entries /
  500: **a million files in 10 000 folders ≈ an hour**, not the plan's 10–20 min (which assumed the recursive
  listing). Working set flat 180–250 MB. SQLite grew ~0.9 KB a row (small table: the indexes weigh more).
- **Killed mid-listing** (722 files written, 27 folders pending): the next sync logged "goes on where it stopped",
  listed only the rest (39 calls) and ended with the same 2 546 / 76, no row twice. The task's counts started again
  at 0 (fixed: `ConnectionIndexer.CountMetAsync` counts what the pass already met).
- **Changes at Dropbox** (a file added, one renamed, a folder of 20 moved, one deleted): Sync now read them by cursor
  in **1.5 s** (one page of 46 changes: a moved folder is its items removed and added). The renamed file kept its
  row (its id), the moved folder's tagged file kept its tag, the deleted one left index and search.
- **Search**: first pages 13–125 ms, `coverage` complete; scope tags of 1 300 files counted in 0.11 s, removed in
  0.10 s. Deleting a tag took its links off connection files.
- **Thumbnails**: 27 pictures and videos asked by 6 parallel requests made **20 calls of 1 or 2** (each request's
  checks take longer than the 30 ms gather, and a browser sends 6 at a time): batching to 25 doesn't happen as
  designed; reopening asked Dropbox nothing. Dropbox made thumbnails of **videos** (WebM, MP4, HEVC MKV, AVI) and of
  a **25 MB** 7200×4800 JPEG (documented: none over 20 MB). **`get_thumbnail` gave no `media_info`** for any of 11
  files asked alone (uploaded an hour before): widths, heights and durations come only from files passed through the
  content cache.
- **Converted play** (HEVC MKV, 2 MB): downloaded once into the cache (probe 4.2 s, then 0.7 s), played at once
  after; its 1280×720 and 8 s then in the row, found by `w:>1000` and `d:>5s`.
- **Ranged reads** (seeking a direct video): 206 with the right range, ~1.5 s each (a temporary link asked per
  read; keeping links for their 4 hours would halve it).

## Gotchas

- Dropbox compares paths ignoring case: index and compare on the lowered path, show the display path.
- Case-sensitive providers on SQL Server would need another collation on the key columns (none exists today).
- Two libraries indexing one connection each list it on their own (one shared pass per connection is not built).
- The recursive Dropbox listing (fewer calls on large trees) is not used: an optional tree listing on
  `IRemoteStorage` would be needed. A large account's first sync is about an hour per million files (measured).
- The index's status counts (`files`, `folders` of `GET …/index`, the Manage panel) are the last sync's: rows SFE
  writes meanwhile (uploads, transfers) are in the index and in searches at once, but counted at the next sync (seen:
  0 files after 607 uploads).
- Thumbnail requests rarely share a Dropbox call (calls of 1 or 2 with a browser's 6 requests at a time): a longer
  gather, or the client asking a page's thumbnails in one request, would be needed for batches of 25.
