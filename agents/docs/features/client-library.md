# C# client library (`Sfe.Client`)

Code: `src/Sfe.Client/` (`SfeClient`, `SfeClientOptions`, `SfeServiceCollectionExtensions`, one folder per area:
`Files/`, `Uploads/`, `Downloads/`, `Jobs/`, `Trash/`, `Details/`, `Archives/`, `Search/`, `Tags/`, `Libraries/`,
`Server/`, `Connections/`, `Errors/`, `Http/`; `Generated/` by Kiota), `samples/Sfe.Client.Sample/`,
`tests/Sfe.Client.Tests/`, `agents/tools/client-generation/`, `build/build.ps1` / `build.sh` (pack). User docs:
[client-library](../../../docs/reference/client-library.md). Part 4 of the
external-access epic (plan done 2026-11-15, epic finalized 2026-11-19).

## Design

- Two layers in one package (`netstandard2.0` + `net8.0`, namespace `Otools.Net.Sfe.Client`, package id
  `Sfe.Client`): the **facade** (`sfe.Files`, `Search`, `Tags`, `Trash`, `Properties`, `Archives`, `Libraries`,
  `Server`, `Connections`) and **`sfe.Raw`**, Kiota's client of every v1 route (administration included), on the
  same `HttpClient`.
- Why Kiota (spike of 2026-11-14 against NSwag and Refitter): the only generator reading the OpenAPI 3.1 document as
  the server emits it (type arrays, `oneOf` with `null`) without a conversion, and tolerant of new enum words (read
  as `null`); NSwag and Refitter failed on `integer|string` defaults, and their enums need a converter before .NET 9.
  Kiota keeps the 200 schema where a route answers another on 202 (`files/explore`, `…/search`, `…/unlock`). The v1
  document describes no binary body, so uploads, ranges and zips are hand-written whatever the generator.
- The generated output is committed (Kiota is a pinned local tool, never run at build); CSharpier and `measure.mjs`
  skip `Generated/`. `ImplicitUsings` off; `netstandard2.0` gets `Microsoft.Bcl.AsyncInterfaces` and an
  `IsExternalInit` polyfill.
- The facade does **not** call through Kiota: `Http/SfeTransport` sends hand-written JSON calls (`SfeJson`:
  camelCase, numbers from strings, unknown enum words read as `Unknown` by `TolerantEnumConverter`, unknown
  properties ignored), so retries, timeouts and errors are in one place and the public models are its own (`SfeItem`,
  `SfeTag`…). Answers are read into `internal` contract classes (`…Contracts.cs` per folder) or the public model
  itself.
- No reference to `Sfe.Core` / `Sfe.Api`: the generated layer comes from the committed v1 document; the facade's
  routes are strings in `Http/Routes` and `Http/FilePlace` (the library's or a connection's file routes).
- The token and `X-SFE-Tenant` go on each request, never on the `HttpClient` (one from `IHttpClientFactory` stays
  shareable). `(Uri, token)` / `(options)` make and dispose their own client; `(HttpClient, options)` leaves it open.
  Options are copied at construction. `Raw` uses `SfeAuthenticationProvider` (Kiota's bearer provider refuses `http`).
- A method per task, not per route; no route, header or chunk size in a public signature; XML docs on every public
  member; paths are the API's with `/` (`SfePath`). Thread-safe; unknown properties and enum words of a newer server
  never throw.

## Connections

- `sfe.Connections.ListAsync()` = `GET connections`'s `usable` part, after `Server.RequireAsync(Connections)`, each
  with its kind's capabilities from the kept `config.connections.kinds` (`SfeServerInfo.ConnectionKinds`, internal).
  `sfe.Connections[id]` is a handle (no call; kept per id): `GetInfoAsync`, `GetCapabilitiesAsync` (kept; from the
  list, else a one-item browse of the root; each listing page refreshes it).
- **Shape: `sfe.Connections[id].Files` (`SfeConnectionFiles`), not a location argument on `SfeFiles`.** A
  connection's routes are the library's below `connections/{id}/`, so the shared code takes an internal `FilePlace`:
  `FileDownloads`, `FileUploads` / `UploadChunks`, `ZipDownloads` and `PathBatches` serve both and the public classes
  stay thin. A location argument would have doubled every `SfeFiles` overload and mixed what differs: inside a
  connection moves and copies are done at once with a result per item (`files/transfer`;
  `SfeConnectionTransferOptions`: Ask and Skip send `fail`, `KeepAccess` null lets the server answer 409
  `access-gains`), deletes go to the provider's trash (no permanent delete, no trash id), properties carry the
  content hash.
- Transfers with the library or another connection are on `SfeConnection` (`CopyFromLibraryAsync`,
  `MoveFromLibraryAsync`, `CopyToLibraryAsync`, `MoveToLibraryAsync`, `CopyToAsync`, `MoveToAsync`):
  `POST files/jobs/transfer` as an `SfeJob` (only `SfeJobOptions.Conflicts` is used); `SfeJob.RetryAsync`
  (`POST files/jobs/{id}/retry`) answers the new job; `SfeJobStatus.FromConnection` / `ToConnection`.
- Capabilities are checked before the call, `SfeNotSupportedException` with `Capability` (`Function` =
  `Connections`, `ServerVersion` null): `RangedReads` (an offset or a length; without it a cut-off download is not
  resumed), `UploadsInParts` (chunks are also clamped to `MaxUploadPart`: the server refuses larger ones with 400),
  `ServerSideMove` / `ServerSideCopy`. Rename is not checked (the server doesn't). Thumbnails stay with `Raw`, as the
  library's do.
- Search and tags in the library's index of a connection: `SfeConnection.Search` (`SfeConnectionSearch`: the
  library's `SfeSearchOptions`, cursors and count; pages are `SfeConnectionSearchPage` / `SfeConnectionSearchCount`
  with `Coverage` = `SfeIndexCoverage` None/Partial/Complete, read from the answer's `coverage` beside its `results`)
  and `SfeConnection.Tags` (`SfeConnectionTags`: the library's file methods plus `GetScopeTagsAsync` /
  `ChangeScopeAsync` on an `SfeTagScope`). The routes are built by `FilePlace` (`SearchRoute`) and the tag calls
  shared with `SfeTags` (`Tags/FileTagCalls`). 409 `connection-not-indexed` is `ConnectionProblem.NotIndexed`.
- `SfeConnection.GetIndexAsync` / `SyncIndexAsync` (`connections/{id}/index`, `…/index/sync`; for who manages the
  connection) answer `SfeConnectionIndex`: a program waits for the first sync before tagging (the tests' wait:
  `ConnectionSearchTests.SyncedAsync`). The library's scope tagging (`file-tags/scope`) stays with `Raw`.
- Folder rules (`connections/{id}/folder-rules`, `…/folders`), text, streaming, thumbnails, management and sign-in
  routes stay with `Raw` (`FacadeCoverageTests` names each).
- Traps: an upload's target folder must exist (only a folder upload's names below it are made); a transfer's target
  folder must exist (items answer `NotFound`).

## Errors and retries

- `SfeApiException` (`StatusCode`, `ProblemType` = the word only, a link type read as null, `Title`, `Detail`,
  `Errors`), subclasses `SfeNotFoundException` (404), `SfeRefusedException` (401/403), `SfeConflictException` (409),
  `SfeRateLimitedException` (429, `RetryAfter`), `SfeServerNotReadyException` (by word first: `setup-required`,
  `migration-required`, `local-only`); `SfeNotSupportedException` (a function the server lacks:
  `Server.RequireAsync(SfeFunction)`, or a provider's `Capability`), `SfeJobConflictException`. `ProblemReader`
  tolerates a non-JSON body.
- A connection's problem keeps its status class and adds `SfeApiException.ConnectionProblem` from the `connection-…`
  word (`NeedsReconnecting`, `RateLimited`, `Unavailable`, `Refused`, `NotSupported`, `Unusable`; another
  `connection-` word is `Unknown`). `connection-rate-limited` is a 429, retried as any 429 (the server's
  `ConnectionGates` keeps answering 429 while the provider's wait lasts).
- 429 and 503 are retried (`Retry-After`, else `RetryDelay` doubled; longer than `MaxRetryDelay` fails at once) only
  when `SfeRequest.CanRetry`: GET, HEAD, DELETE, and calls marked safe (`uploads/check`, chunk `PUT`s). A `POST` never.
  A 503 with a not-ready word is never retried. `RequestTimeout` covers until the headers (the client's own
  `HttpClient.Timeout` is infinite, so downloads may last).

## Jobs, uploads, downloads, paging

- The server is read once (`Server.GetInfoAsync`, `GET config`, kept; a failed read is tried again); `Libraries`
  reads `config` each time.
- Moves and copies answer an `SfeJob` (`WaitAsync` polls every `PollInterval`, clashes answered by a fixed choice or
  `OnConflict`; none: `SfeJobConflictException`). After an answer the server's job leaves `waiting` a moment later:
  `AnswerAsync` and `CancelAsync` ask again for up to 1 s, so a `WaitAsync` right after doesn't see the old clash.
- Uploads: 8 MB chunks from one byte buffer (`ByteArrayContent`: no body of unknown length, the .NET Framework trap),
  413 halves it, a cut-off chunk asks the server's offset and goes on, an unfinished upload of the same file is resumed
  (`Resume`), a cancelled one deleted.
- Downloads: a body cut off goes on with `Range` + `If-Range` (the first answer's `Last-Modified`; a 200 then means
  the file changed: fails); resuming a partial file is `Offset = destination.Length`. A zip's one-time link is never
  retried.
- Folder listings page by offset (a connection's while `hasMore`); search pages by the server's keyset cursor
  (`next` → `after`, the same sort), the first page carrying the count (`Search.CountAsync` asks a page of 1). Tag
  changes go in batches of 1000 files (the server's `MaxFilesPerChange`), a sum of the answers; deletes and moves
  inside a connection in batches of 1000 paths (`PathBatches`).

## Compatibility and tests

- `tests/Sfe.Client.Tests` runs the real server in process (`Infrastructure/TokenServer`: a token of the
  administrator, `Sfe:Mode=External` since the test server has no remote address; `IndexAsync` scans for the search
  tests; `StartAsync(…, provider:)` registers the server tests' `FakeProvider`, `ConnectAsync` makes a connection
  through `ConnectionService`; `ConnectedServer` wraps both). It references `Sfe.Tests` for the helpers (one copy).
  Every facade method has a test there.
- `FacadeCoverageTests`: the v1 routes of the facade's areas (prefixes in `Areas`) each name the method covering them
  or why they stay with `Raw`; a new route there fails it until the same commit says.
- `Compatibility/PublicApiTests`: the public surface (reflection, `PublicApiWriter`, the generated namespace left out)
  against the committed `Compatibility/PublicApi.txt`. Rewrite it after a meant change:
  `SFE_PUBLIC_API=write dotnet test tests/Sfe.Client.Tests --filter FullyQualifiedName~PublicApiTests`.
- `GenerationTests` runs `node agents/tools/client-generation/generate.mjs --check`: regenerate
  (`generate.mjs` without `--check`) in the commit that changes `tests/samples/openapi/v1.json`.
- `SampleTests` runs `samples/Sfe.Client.Sample` (`SampleRun.RunAsync`, the server's `HttpClient`) twice in a row.
- `TransportTests` cover retries and errors on a stub handler (the real server hardly answers 429/503).
- Bounded upload memory is tested on net10 only; the `netstandard2.0` asset is built, not run.
- `info.version` of the v1 document is the app's version: it changes at each release.

## Packaging

- Version from `Directory.Build.props` (the app's); `build/build.ps1` and `build.sh` run
  `dotnet pack src/Sfe.Client -c Release -p:Version=<version> -o artifacts` for every target: `Sfe.Client.<v>.nupkg`
  and `.snupkg` join the release artifacts. Not published to nuget.org.
- The package holds both targets' DLLs and XML docs, and `src/Sfe.Client/README.md` (`PackageReadmeFile`).
  Dependencies: Kiota's Abstractions, HttpClientLibrary and Serialization.Json, `Microsoft.Extensions.Http`, and
  `Microsoft.Bcl.AsyncInterfaces` on `netstandard2.0`.
- The sample references the project; a program outside the repository references the package.
