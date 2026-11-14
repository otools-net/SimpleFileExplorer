# Transfers between the library and a connection

Code: `src/Sfe.Core/Transfers/` (`TransferJobs`, `TransferRun`, `TransferStart`, `TransferStream`,
`TransferFileWriter`, `TransferCalls`, `TransferRetries`, `TransferRetry`, `TransferSummary`, `TransferTally`,
`ContentHashes`, `IncrementalContentHash`, `TransferHashes`, `ITransferEndpoint`, `ITransferWrite`,
`LibraryTransferEndpoint`, `LibraryTransfers`, `LibraryTransferWrite`, `LibraryDisk`, `RemoteTransferEndpoint`,
`RemoteTransferWrite`, `RemoteTransferSide`, `RemoteTransferChecks`, `IConnectionJobAccess`); Api
`TransferEndpoints.cs`, `TransferContracts.cs`, `TransferOpening.cs`, `ConnectionJobAccess`; client
`files/place-transfers.ts`, `file-transfers.ts`, `operations-panel.component.ts`, the folder picker's Places level.
Tests: `TransferJobsTests`, `TransferRouteTests`, `TransferRetryTests`, `TransferCallsTests`,
`LibraryTransferEndpointTests`, `RemoteTransferEndpointTests`, `place-transfers.spec.ts`. Inside one place:
[move-and-copy](move-and-copy.md) (library), [connection-files](connection-files.md) (a connection's `transfer`).

## Routes and jobs

- `POST api/files/jobs/transfer` (`TransferRequest`: kind move / copy, from {connection?, paths}, to {connection?,
  path}, conflicts) starts a job, 202. Same place on both sides: 400 (their own routes). Connection to connection is
  allowed: the bytes go through the server.
- `TransferJobs` is **per library**, on the library's `JobBoard` (the same ids, list, conflict questions and cancel as
  `FileJobs`): followed, answered and cancelled through the library's job routes; `FileJobView.FromConnection` /
  `ToConnection` name the places. Local to local stays in `FileJobs`.
- The route checks the sides at once (`TransferOpening`: usable, rights, the target folder by its rules); the job then
  opens its own storage (`ConnectionService.OpenAsync`), since the request's is disposed with the response. Each job
  owns its two sides and disposes them when it ends.

## The two sides (`ITransferEndpoint`)

- Get, list (paged), read from an offset, `BeginWriteAsync` → `ITransferWrite` (offset, write a part, commit, abort),
  create folder, free name, replace, delete source, free space, hash name.
- `LibraryTransferEndpoint`: the checks of the library's jobs (rights and folder rules read again per item, protected
  places and the work folder not found, links not followed in folders), writes into a `.sfe-partial` then renames,
  indexes each arriving file (the folders' default tags apply; else the next scan does), replace and a moved
  source go to SFE's trash (or deleted for good when the trash is set so).
- `RemoteTransferEndpoint` over `IRemoteStorage` + `TransferCalls`: each call takes a `ConnectionGates` turn, waits
  out the provider's `RateLimited` instead of failing, retries when the provider is down or busy (5 times, backoff 1
  to 16 s), one write at a time per connection (Dropbox's `too_many_write_operations`), `RemoteListings.Forget`.
  Deletes go to the provider's own trash. Access per item through `IConnectionJobAccess`
  ([connection-folder-rules](connection-folder-rules.md)).

## Running (`TransferRun`)

- Folders walked depth first, page by page (bounded memory for millions of items); a move deletes a folder's files
  only after its listing is read (paged cursors shift otherwise).
- Conflicts per item with the library's choices (ask / replace / skip / keep both, `ConflictChoice`).
- `TransferStream`: library → connection by upload-session parts of at most 32 MB (or the target's `MaxUploadPart`),
  connection → library by ranged reads into the partial file; the provider's offset followed; a failed part sent
  again twice.
- Verification (`TransferFileWriter`): size always; the content hash computed while streaming when either side has
  one (`ContentHashes`: `dropbox`, `sha256`, `md5`), for copies too; the source's hash checked before commit, the
  target's after (a mismatch removes the written file). A move removes each source file only once verified.
- Free space before each file on both sides: the library's `MinimumFreeSpaceMegabytes`, a connection's `SpaceAsync`
  when it tells. `Files:Uploads:MaxFileSizeMegabytes` holds for files going into a connection (`TransferRun.CheckSize`,
  before reading; that item fails, the others go).
- An ended sign-in marks the connection needs reconnecting (both probed when both sides are connections) and fails
  the rest.
- Leaving the library by a move is a delete there: tags don't travel (the dialog says so); arriving files get the
  folder's default tags. A library folder with rules may be moved out (as for a delete, its rules go into the trash
  entry, [trash](trash.md); no warning of its own).
  A connection folder moved out loses its rules ([connection-folder-rules](connection-folder-rules.md)).

## Retry (`TransferRetries`)

- `POST api/files/jobs/{id}/retry`: an ended job keeps, in memory for 24 h, its asked items not done nor skipped
  (failed, or not reached after a cancel); a restart forgets them. The owner only (404 otherwise); 409 `job-running`
  / `nothing-to-retry`. Started once, as a **new** job (202 with its view), access checked again as at start.
- `TransferStart.Retry`: a folder already at the target is gone into; a file there of the same size counts as done
  for a copy; for a move (which then deletes its source) the content hashes must match too, else the conflict
  question is asked; a file whose writing broke is written again from its start (no resume point).

## History and logs

- Each job is one task-history run (`BackgroundTaskKind.Transfer`, history only: not in the task list nor
  schedulable): `TransferSummary` (account, items, both places named by `TransferOpening.NameOf`, no e-mail), a log
  line per item.

## Client

- Clipboard and drag data carry the place; `PlaceTransfers` sends same-place work to the old routes and between
  places to the transfer route (`between`), followed by `FileTransfers` like a library job; the panel names both
  places (`journey`, names from `UsableConnections`) and offers **Retry** for failed items.
- The folder picker of Move to / Copy to has a top level, **Places** (the library and the connections one writes to,
  `FolderBrowser.offer`); sidebar connections take drops onto their root.
- Copies out of a connection one only reads are offered (`atPlace` keeps `files.copy`); pasting into it never.

## Traps

- "A root singleton must never depend on a Core type" ([tenants](../architecture/tenants.md)): the transfer code
  takes a library's services per job, never by injection into a server-level singleton.
- The connection side's `RemoteListings.Forget` after writes, or the listing shows the old snapshot for 5 minutes.
