# Files of a connection: browse, read, change, upload, properties

Code: Api `ConnectionFileEndpoints` (browse, raw, thumbnails, properties), `ConnectionSearchEndpoints`,
`ConnectionFileServices` (`OpenAsync`, `RunAsync`), `ConnectionBrowsing`, `ConnectionArchiveEndpoints`,
`ConnectionTagEndpoints`, `ConnectionTagChanges`, `ConnectionWriteEndpoints`, `ConnectionWriteChecks`,
`ConnectionUploadEndpoints` (`ConnectionUploadSteps`,
`ConnectionUploadNames`), `ConnectionProblems`, `OpenConnection`, `RemoteFileResult`, `RemoteRanges`; Core
`Connections/RemoteListings` (`RemoteSnapshot`, `RemoteSort`), `Connections/Indexing/RemoteIndexListings`,
`RemoteArchives`, `RemoteUploads`, `ConnectionGates`. Client `library/connection-pages.ts`, `place-files.ts`,
`place-transfers.ts`, `place-properties.ts`, `upload-places.ts`, `files/drag-items.ts`, `file-clipboard.ts`. Tests:
`ConnectionFileRouteTests`, `ConnectionSearchRouteTests`, `RemoteIndexSearchTests`, `ConnectionWriteRouteTests`,
`ConnectionUploadRouteTests`, `ConnectionGatesTests`, `RemoteListingsTests`, `place-*.spec.ts`. The connection itself:
[connections](connections.md); who sees what inside it: [connection-folder-rules](connection-folder-rules.md); copies
with the library: [transfers](transfers.md).

## Opening a connection for a request

- Every route opens through `ConnectionFileServices.OpenAsync`: usable by the session or 404, then the route's
  `files.*` / `library.browse` right, then `ConnectionService.OpenAsync`; `write: true` answers 403 for a connection
  the session only reads (or where the right is allowed nowhere). The storage is disposed with the response
  (`RegisterForDisposeAsync`), since a raw read streams from the provider's `HttpClient` after the handler returns.
- Paths go through `RemotePaths.Normalize` (null = 404). An encoded `%2F` stays in a catch-all route value: it is a
  name with `%2F` inside the root, never a separator (as in the library's routes).
- `ConnectionFileServices.RunAsync` runs every provider call in a turn of `ConnectionGates` (singleton: 4 calls at
  once per connection, 30 s to get a turn, else 429); after a `RateLimited` answer the connection is left alone until
  its `Retry-After` (5 s when none): the routes answer 429 with the time left without calling the provider. A raw
  read gives its turn back when its answer starts. Per caller: policy `RequestLimits.Connections` (6 at once, 100
  queued) on browse, raw and zips; thumbnails keep `Media`.
- A provider's error becomes `ConnectionProblems` (problem types): `connection-needs-reconnecting` (409, marks the
  connection), `connection-rate-limited` (429 + `Retry-After`), `connection-unavailable` (502), `connection-refused`,
  `connection-not-supported`, `connection-unusable`.

## Browsing and reading

- `GET api/connections/{id}/browse` answers `ConnectionListing`: the library's `DirectoryListing` plus `access`,
  `sorted`, `hasMore`, `capabilities`.
- `ConnectionBrowsing` picks the source per page: the library's index when it holds the folder whole
  (`RemoteIndexListings`: synced within 30 minutes, past its full listing, of the current root and account, the
  folder listed; sorted and counted by the database, paged by key from the last row of the page before; no provider
  call, no gate turn), else the provider below, its items marked `indexed` when the index has them. Later pages of a
  provider read stay with it (`RemoteListings.Holds`). Details:
  [connection-index](connection-index.md#listings-from-the-index).
- `RemoteListings` (singleton): offset 0 reads the whole folder (up to 20,000 items) and sorts it as `LibraryBrowser`
  does; later pages reuse that snapshot for 5 minutes (16 snapshots at most); a larger folder comes in the provider's
  order through a window that reads forward (`hasMore`, sorting off). Providers list unordered and in pages of their
  choice, hence the snapshot. `Forget(id)` drops a connection's reads: every write route and transfer calls it.
- `files/raw/{**path}` serves one range (`RemoteRanges`; 416 past the end) under the library's sandbox
  (`RemoteFileResult`: `FilePolicy`, attachment for types a page could run); a provider without `RangedReads` serves
  the whole file with 200 and no `Accept-Ranges`.
- `thumbnails/{**path}`, `files/text`, `streaming/probe|play`: [connection-previews](connection-previews.md)
  (provider thumbnails in batches, the server-wide content cache, ffmpeg on cached media only).
- Zips: `POST api/connections/{id}/archive` and its one-time link (`ArchiveLinks` with scope `connection:{id}`: the
  library's route never takes such a link, nor uses it up), written from the provider by `RemoteArchives`; a file
  without content (Dropbox Paper) is named in `SFE - left out.txt`.

## Searching (`ConnectionSearchEndpoints`)

- `GET api/connections/{id}/search` (right `connections.use`, then `library.browse` through `OpenAsync`; 404 for a
  connection the session may not use or a folder it doesn't see): the library's search on the library's index of
  the connection (`RemoteIndexSearch`), no provider call, no gate turn. Answer `ConnectionSearchResults`: the
  library's `SearchResults` (paths relative to the connection's root), name, access, capabilities, `coverage`
  (`none`: no index of this root and account in the library; `partial`: listing or synced over 30 minutes ago;
  `complete`). A search starts the index like a browse (`ConnectionIndexes.OpenedAsync`).
- Rows `Removed` are never found. Folder rules are query conditions (`RemoteSearchScope`): the regions of
  `ConnectionFolders.Visible()` (`FolderPolicy.FilterFor(library.browse)`) as key trees, minus the files whose
  provider id `HiddenIds()` holds and the trees of folders with such an id (renamed outside SFE: their files have
  other ids); every item shown then passes `ConnectionFolders.At(path, id)`.
- A key tree is `[key/, key0)` on SQLite (binary key column: a seek) and `LIKE 'key/%'` plus the start compared on
  SQL Server (its CI collation would not keep `/` and `0` adjacent).
- Client: `connectionSearchPage` (`?c=&q=`) makes the connection the place as its folders do; the listing tells a
  coverage that isn't complete. "Everywhere" (`?q=&all=1`, `c` and `path` kept for going back):
  `library/everywhere/` asks the library and each usable connection apart (`EverywhereSearch`), groups paged alone;
  the scope menu of the search box (`topbar/search-scope.ts`) switches.

## Tags

- `api/connections/{id}/file-tags` (GET, POST) and `…/scope` (GET, POST): the library's `file-tags` routes on the
  rows of the library's index (`RemoteFileTags`, `RemoteScopeTagger`); right `connections.use`, then `tags.view` /
  `tags.assign` through `OpenAsync` and at each path by folder rules (and by the row's provider id). Details, choices
  and measurements: [connection-index](connection-index.md#tags).
- Rows of another root or account are never read (`RemoteTagPlace.IsCurrentAsync`). A file missing from the index is
  fetched (20 a request at most) and written by `ConnectionIndexUpdates`; no index of the connection: 409
  `connection-not-indexed`. Listing pages get their files' tags where the folder allows `tags.view`.
- Client: `atPlace` keeps the tag rights but `tags.folderDefaults`; dialogs call `tagsRoute(connection)`.

## Changing files (`ConnectionWriteEndpoints`)

- `POST api/connections/{id}/files/folders`, `rename`, `delete`, `transfer` (move or copy inside the connection,
  server side at the provider): Write access or 403, one gate turn, at most 1000 items, a result per item for delete
  and transfer. An error stopping the provider (sign-in ended, wait, unreachable) answers the usual problem and doesn't
  list what was done before it.
- No SFE trash: a delete goes to the provider's own (`OwnTrash`), no Undo; the delete dialog says which applies
  (`place.ownTrash`).
- Rename and move carry the folder rules along ([connection-folder-rules](connection-folder-rules.md)).
- Client: `atPlace` gives `files.createFolders` / `rename` / `delete` back for write access, `move` / `copy` with
  `ServerSideMove` / `ServerSideCopy`; selection bar, menus, F2 / Shift+N and batch rename follow. `PlaceFiles` picks
  the library's or the connection's routes for the dialogs. The clipboard (`FileClipboard.connection`) and the drag
  data (`drag-items.ts`: `{ connection, paths }`, a plain array = the library) carry the place; `PlaceTransfers`
  routes one connection → its `transfer`, the library → the library's jobs, two places →
  [transfers](transfers.md).

## Uploads (`ConnectionUploadEndpoints`)

- `connections/{id}/files/uploads…`: the library's routes (check, start, list, get, PUT chunk, finish, cancel),
  paths, bodies and answers. Each chunk goes to the provider's upload session as it comes (`AppendUploadAsync`):
  nothing is kept on the server's disk. `RemoteUploads` keeps the records in memory (20 unfinished per person, swept
  after 24 h idle; a restart loses them); the provider's session id is never answered.
- A wrong offset: the provider says where it is (`WrongOffset` with the offset); the record follows it and the route
  answers 409 with the upload, as the library does, and the client goes on from there.
- Finish with ask or skip looks the name up first, so a conflict doesn't use up the provider's session; replace needs
  `files.delete`. The space's `Files:Uploads:MaxFileSizeMegabytes` is checked at start; free space is the
  provider's (413).
- Client: `files.upload` comes back with write access and `UploadsInParts`; `UploadBatch.connection` is set when the
  batch starts (`uploadsRoute`), so navigating away keeps the target; unfinished uploads into a connection are not
  offered again after a reload.

## Properties

- `GET api/connections/{id}/files/properties?path=` (`ConnectionItemProperties`): the item, the provider's kind, its
  content hash and the hash's name (`ProviderCapabilities.ContentHash`), whether it has content; the provider account
  only on the caller's own account-level connection (not a library's or the server's, even to administrators).
- Client: `place-properties.ts`; the dialog hides what reads the library (counts, totals, checksums, tags, full path).
