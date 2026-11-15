# Plan: the C# client library (`Sfe.Client`)

Files: new `src/Sfe.Client/`, `tests/Sfe.Client.Tests/`, `samples/Sfe.Client.Sample/`; `Sfe.slnx`,
`Directory.Build.props`, `build/build.ps1`, `build/build.sh`, `.csharpierignore`, `agents/tools/readability/`.
Part 4 of the [epic](external-access-epic-plan.md); needs plan 1 (done:
[api-versioning](api-versioning.md)) and api-tokens (done: [access-tokens](../features/access-tokens.md)) finished.
Delete this plan once its last item is ticked.

## Goal

A third-party app adds one package and manages files without knowing routes, headers, chunk sizes or job polling:

```csharp
using var sfe = new SfeClient(new Uri("http://nas:5000"), token);
await foreach (var item in sfe.Files.ListAsync("Photos/2026"))
    Console.WriteLine(item.Name);
await sfe.Files.UploadAsync("Photos/2026/new.jpg", stream, progress);
var job = await sfe.Files.MoveAsync(["Photos/a.jpg"], "Archive");
await job.WaitAsync();
```

**Done when:** the package is built with each release, its facade covers the list in "Decisions", its tests run
against the real server in process, and a server change that would break it fails a test before it ships.

## Decisions

- Two layers in one package: a **generated** client of every v1 route (`sfe.Raw`), and a **hand-written facade**
  for files, jobs, trash, properties, search, tags, zip downloads, libraries, and connections once they exist.
  Administration is reached through `Raw` only.
- No reference to `Sfe.Core` or `Sfe.Api`: the contracts are generated from the committed v1 document
  (`tests/samples/openapi/v1.json`, [the compatibility net](api-versioning.md#the-compatibility-net)).
- Targets `netstandard2.0` and `net8.0`; System.Text.Json; as few dependencies as the generator allows.
- Version = the app's (`Directory.Build.props`); the library works with every server that has v1 and says so when a
  function is missing there.
- Not published to nuget.org: `dotnet pack` output (`.nupkg`, symbols) joins the release artifacts.

## Progress

### Phase 0: spike (result written here before phase 1)

- [x] Generate from the real document with Kiota, NSwag and Refitter; choose by: reads the document as emitted
  (OpenAPI 3.1 by .NET 10, or 3.0 if the server must emit that for the generator), both targets, streaming request
  and answer bodies (uploads, ranged downloads), dependencies pulled into the host app, no network at build
  - As built (2026-11-14, Kiota 1.38.0, NSwag 14.8.0, Refitter 2.3.1, on the baseline as committed):
    - **Kiota**: reads the 3.1 document as is (type arrays `["integer","string"]`, `["null",…]`, `oneOf` with
      `null`); builds on `netstandard2.0` and `net8.0` with no error or warning. 486 files (a request builder per
      path segment, a model per schema). Unknown properties go to `AdditionalData`, unknown enum words read as
      `null`: the v1 rule (never throw on a newer server) holds. Three warnings: `files/explore`, `…/search`,
      `…/unlock` answer different schemas on 200 and 202, Kiota keeps the 200 one. Runtime: Kiota's Abstractions,
      Http.HttpClientLibrary and Serialization.Json (2.1.2) with JSON only registered (`--serializer`,
      `--deserializer`); they bring Std.UriTemplate, Microsoft.IO.RecyclableMemoryStream and
      DependencyInjection.Abstractions, plus System.Text.Json 8 and the usual BCL packages on `netstandard2.0`.
    - **NSwag** (`openapi2csclient`, System.Text.Json): one 34,000-line file, no runtime dependency (System.Text.Json
      and System.ComponentModel.Annotations on `netstandard2.0`). Does **not** read 3.1 as emitted: defaults of
      `integer|string` numbers come out as strings (`int X = "0"`, 24 compile errors). Through a 3.0 down-conversion
      (type arrays to one type plus `nullable`, `pattern` dropped on numbers, `oneOf` with `null` to `allOf` plus
      `nullable`) it builds on both targets, but its enums carry only `[EnumMember]`, which System.Text.Json ignores
      before .NET 9: every enum word would fail to read without a hand-written converter.
    - **Refitter** (Refit interfaces, NSwag's models): the same 3.1 errors and enum problem as NSwag, plus the Refit
      package (and its source generator) in the host app.
    - Streaming: the v1 document describes no binary body (raw files, chunks and zips have no content type in it),
      so no generator produces streaming methods for them: uploads, ranged downloads and zips are hand-written in the
      facade on the same `HttpClient` (`ResponseHeadersRead`, `StreamContent`) whatever the choice.
    - **Choice: Kiota.** The only one reading the document as the server emits it, with no conversion step and no
      server change, and the only one tolerant of new enum words; the price is three small Microsoft packages
      (instead of none for NSwag) and many generated files.
- [x] Decide whether the generator runs at build (`dotnet-tools.json`) or its output is committed (preferred: committed,
  with a test that regenerating changes nothing)
  - As built: committed. Kiota is a local tool (`dotnet-tools.json`, pinned 1.38.0) run only to regenerate, never at
    build; `GenerationTests.RegeneratingTheClientChangesNothing` runs the script's `--check` (needs node, as the client
    build does; the script runs `dotnet tool restore` first, so a fresh CI runner fetches Kiota once).

### Phase 1: project and generated layer

- [x] `src/Sfe.Client` in `Sfe.slnx`, package metadata, XML docs file, nullable, the analyzers of the solution
  - As built: `netstandard2.0;net8.0`, `RootNamespace` `Otools.Net.Sfe.Client` (project, folder, assembly and
    package id `Sfe.Client`), `ImplicitUsings` off (the generated files write their own usings), package id, authors, MIT expression,
    `snupkg` symbols; version from `Directory.Build.props`. No analyzer package in the solution: the `.editorconfig`
    rules and the SDK's default analyzers apply. Builds with no warning.
- [x] Generated client under `Generated/` (excluded from CSharpier and `measure.mjs`), the regeneration command in
  [commands](../environment/commands.md) and a README in `agents/tools/` if it needs a script
  - As built: `agents/tools/client-generation/generate.mjs` (README there): Kiota into a temporary folder, its
    `kiota-lock.json` dropped (a path relative to that folder), files written with LF, `--check` to compare.
    `SfeRawClient` in `Otools.Net.Sfe.Client.Generated` (models in `.Models`). `measure.mjs` skips
    `src/Sfe.Client/Generated/` and measures `tests/Sfe.Client.Tests` with the server tests.
- [x] `tests/Sfe.Client.Tests`: the in-process server of `tests/Sfe.Tests/Infrastructure` (its `HttpClient` given to
  the library), a token made by the test
  - As built: references `Sfe.Tests` (the helpers stay there, one copy; MSTest runs only this assembly's tests).
    `Infrastructure/TokenServer` sets up the server (`Sfe:Mode=External` from `TestLibrary.Settings`), makes the
    administrator's token (`TokenClients.CreateAsync`) and gives `TokenClients.Bearer`'s `HttpClient` to Kiota's
    `HttpClientRequestAdapter` with `AnonymousAuthenticationProvider` (the header is already on the client); the
    base URL loses its last `/` (Kiota's templates start with one). `GeneratedClientTests`: a folder listed, a tag
    made with the token only.

### Phase 2: core of the facade

- [x] `SfeClient`, `SfeClientOptions` (base address, token, library, timeouts, retries), `AddSfeClient` for DI
  (`IHttpClientFactory`)
  - As built (2026-11-15): the facade does **not** go through Kiota: `Http/SfeTransport` sends hand-written JSON
    calls (System.Text.Json, `Http/SfeJson`: camelCase, numbers from strings, unknown enum words read as `Unknown`
    by `TolerantEnumConverter`) on the one `HttpClient`, so retries, timeouts and errors are in one place and the
    public models are the facade's own (`SfeItem`, `SfeJobStatus`…), not the generated ones. `Raw` (the Kiota
    client) shares the `HttpClient`, with `SfeAuthenticationProvider` (Kiota's bearer provider refuses `http`); it
    has no retries and throws Kiota's `ApiException`. The token and `X-SFE-Tenant` go on each request, never on
    the `HttpClient` (one from a factory stays shareable). Three constructors: `(Uri, token)` and `(options)` make
    and dispose their own client (`Timeout` infinite: `RequestTimeout` covers until headers only, so downloads
    may last); `(HttpClient, options)` leaves it open. `AddSfeClient` registers it transient over a named client
    (`SfeClient.HttpClientName`) and answers the `IHttpClientBuilder`; adds `Microsoft.Extensions.Http` 8.0.1.
    Options are copied at construction. Upload chunk size is `internal` (`InternalsVisibleTo` the tests).
- [x] Errors: `SfeApiException` (status, `type` word, detail) with subclasses for not found, refused, conflict,
  rate limited, server not ready (`setup-required`, `migration-required`, `local-only`)
  - As built: `SfeException` base; `SfeApiException` (`StatusCode`, `ProblemType` = the word only, a link type
    reads as null, `Title`, `Detail`, `Errors` of a 400); `SfeNotFoundException` (404), `SfeRefusedException`
    (401/403), `SfeConflictException` (409), `SfeRateLimitedException` (429, `RetryAfter`),
    `SfeServerNotReadyException` (by word first, whatever the status). `ProblemReader` tolerates a non-JSON body.
    Also `SfeJobConflictException` (a job waits on a clash nobody answers).
- [x] Retries on 429 and 503 honouring `Retry-After`; cancellation tokens on every call; no retry of non-idempotent
  calls without an id the server knows
  - As built: `SfeRequest.CanRetry`: GET, HEAD, DELETE, plus calls marked safe (`uploads/check`, chunk `PUT`s whose
    offset the server checks); every `POST` else never. `Retry-After` longer than `MaxRetryDelay` fails at once;
    without it, `RetryDelay` doubled. A 503 with a not-ready word is never retried. Tested on a stub handler
    (`TransportTests`): the real server hardly gives these statuses.
- [x] `sfe.Server.GetInfoAsync()` (version, functions on) read once; `SfeNotSupportedException` when a function is
  missing on that server
  - As built: from `GET config` (version, commit, mode, rights, current and reachable libraries), kept after the
    first read (a failed read is tried again). `SfeFunction`: `Thumbnails`, `Trickplay`, `Connections`,
    `AccessTokens` from the configuration's parts; `Server.RequireAsync` throws. No phase-3 method needs it (all
    its routes are in every v1 server); phase 5 checks `Connections` with it.

### Phase 3: files

- [x] `Libraries`, `Files.ListAsync` (`IAsyncEnumerable`, pages fetched as read), `GetAsync`, `ExistsAsync`
  - As built: `Libraries.ListAsync` / `GetCurrentAsync` read `config` each time (not the kept info). `ListAsync`
    pages `browse` by offset (`SfeListOptions`: sort, descending, page size 500); `GetAsync`/`ExistsAsync` use
    `files/properties` (right `files.browse`). `SfePath`: `Normalize`, `Combine`, `Parent`, `Name` (a `\` stays a
    name character). `netstandard2.0` gets `Microsoft.Bcl.AsyncInterfaces` 8.0.0 and an `IsExternalInit` polyfill.
- [x] `DownloadAsync` (to a stream, ranges, resume), `OpenReadAsync`
  - As built: `SfeDownloadOptions` (`Offset`, `Length`, `Progress`); a body cut off goes on with `Range` from the
    bytes written and `If-Range` with the first answer's `Last-Modified` (a 200 then means the file changed: fails);
    resuming a partial file is `Offset = destination.Length`. Tested with `CuttingHandler` in front of the real
    server. `OpenReadAsync` answers a stream that disposes the answer.
- [x] `UploadAsync` (resumable chunks, progress, resume after a failure, conflict choice), folder uploads
  - As built: `Uploads/FileUploads` + `UploadChunks`: one byte-array buffer of 8 MB (each chunk a `ByteArrayContent`,
    so no target streams a body of unknown length: the .NET Framework trap), 413 halves it to 256 KB, a chunk cut off
    asks `GET uploads/{id}` and goes on from `received`, a 409 at an offset goes on from the server's. `Ask` checks
    the name first (`uploads/check`) and throws `file-exists` before sending; `Skip` answers null. `Resume`
    (default on, seekable streams): an unfinished upload of the same folder, name, size and date (2 s) is found in
    `GET uploads` and continued. Cancelled: the upload is deleted; failed: kept for a later call. Bounded memory is
    tested on net10 only (a 24 MB non-seekable stream never read more than a chunk ahead of the progress); the
    `netstandard2.0` asset is built, not run. `UploadFolderAsync`: a folder of its name in the target, the files'
    dates kept, progress over all files; empty folders are not made.
- [x] `CreateFolderAsync`, `RenameAsync`, `MoveAsync`, `CopyAsync`, `DeleteAsync`; `SfeJob` (`WaitAsync`, progress,
  conflicts answered by a callback or a fixed choice, cancel)
  - As built: `DeleteAsync` / `DeletePermanentlyAsync` (no flag), batches of 1000, a result per path. `SfeJobOptions`
    (`Conflicts`, `KeepAccess`, `KeepTags` default false as the API). `SfeJob`: `Status`, `RefreshAsync`,
    `WaitAsync(SfeJobWaitOptions)` (`Progress`, `OnConflict` callback; none: `SfeJobConflictException`, the job
    keeps waiting), `AnswerAsync` (a 409 = nothing asked now, ignored), `CancelAsync`. Trap: the server takes an
    answer at once but its job leaves `waiting` a moment later (the continuation runs asynchronously):
    `AnswerAsync` asks again for up to 1 s until it doesn't wait, else a second wait would see the old clash.
- [x] `Trash`, `Properties` (totals, checksums), `Archives` (zip of several items to a stream)
  - As built: `Trash` (`ListAsync` paged by `before`, `CountAsync`, `RestoreAsync`, `DeleteAsync`, `EmptyAsync`);
    `Properties` (`GetAsync`, `GetTotalsAsync`, `CountAsync` and `ChecksumAsync` follow their background job and
    stop it when cancelled); `Archives.DownloadZipAsync` (the one-time link never retried). Routes of these areas
    not in the facade are named in `FacadeCoverageTests` (the test of the rules below).

### Phase 4: search and tags

- [ ] `Search.QueryAsync` (query text as the app's language, sorts, cursor paging as `IAsyncEnumerable`, count)
- [ ] `Tags` (list, tags of files, add and remove on files)

### Phase 5: connections (moved here from the connections plan, closed 2026-11-14)

The server side is built ([connections](../features/connections.md)); the facade comes on phases 2 and 3.

- [ ] `sfe.Connections`: `ListAsync` (usable ones: `GET api/v1/connections`, its `usable` part: id, name, kind,
  level, access, capabilities), and a connection as a **location** the file methods take
  (`sfe.Connections[id].Files`, or a location argument on `sfe.Files`), mapping to:
  - list / get: `GET connections/{id}/browse` (paged, `hasMore`, `sorted`), `files/properties?path=`;
  - download / open read: `GET connections/{id}/files/raw/{path}` (ranges only when the capability says so);
    several items as a zip: `POST connections/{id}/archive` then its one-time link;
  - upload (resumable, conflict choice): `connections/{id}/files/uploads` (check, start, PUT chunk, finish, cancel;
    a 409 wrong offset gives where to go on from);
  - create folder, rename, delete, move / copy inside it: `POST connections/{id}/files/folders`, `rename`, `delete`,
    `transfer` (a result per item; a 409 `access-gains` answered by a `keepAccess` choice);
  - transfers between the library and a connection or two connections: `POST files/jobs/transfer` as an `SfeJob`
    (the library's job routes follow, answer, cancel it), `POST files/jobs/{id}/retry`;
  - folder rules (administration of a library): `GET`/`PUT connections/{id}/folder-rules`, `GET …/folders?path=`
    (through `Raw` is enough unless the facade covers library folder rules too).
- [ ] Errors: the `connection-*` problem types (`needs-reconnecting`, `rate-limited` with `Retry-After`,
  `unavailable`, `refused`, `not-supported`, `unusable`) as `SfeApiException` subclasses or a kind; retries on
  `connection-rate-limited` as on 429.
- [ ] Capabilities checked before a call (`SfeNotSupportedException` when the provider can't: thumbnails, ranges,
  server-side copy / move, uploads in parts); tests on the `FakeProvider` registered in the in-process server; the
  baseline of the v1 document updated.

### Phase 6: compatibility, sample, packaging

- [ ] Public API snapshot test (`PublicApiAnalyzers`, or a text snapshot of the public surface)
- [ ] `samples/Sfe.Client.Sample` (console: list, upload, download, move); run by a test as a smoke test
- [ ] `dotnet pack` in `build/build.ps1` and `build/build.sh`; the package among the artifacts
  ([portable-build](../environment/portable-build.md))
- [ ] `docs/reference/client-library.md` (install from the file, token, examples, errors, versions); package README
- [ ] New note `agents/docs/features/client-library.md`; this plan deleted

## Rules for the facade

- A method per task, not per route; no route, header or chunk size in a public signature.
- Paths are the API's root-relative paths with `/` (`SfePath` helper: combine, parent, name; no OS paths).
- Unknown properties and enum words from a newer server never throw (the v1 rule of `api-versioning.md`).
- Thread-safe; one `HttpClient` per `SfeClient`; `IDisposable` only disposes a client it created.
- XML docs on every public member; the limits of [readability](readability.md) apply to the hand-written code.
- Every facade method has a test against the real server; a new v1 route in the facade's areas gets its method in the
  same commit (a test lists the routes of those areas and the methods covering them).

## Traps

- The test server has no remote address: tests set `Sfe:Mode=External` (`TestLibrary.Settings`), as the others do.
- The generated client must send `Authorization` only; `X-SFE-Request` is not needed with a bearer.
- `netstandard2.0` has no `IAsyncEnumerable` in the box (`Microsoft.Bcl.AsyncInterfaces`), no `Range` header helpers
  on some paths, and `HttpClient` there doesn't stream request bodies on .NET Framework without care: test uploads of
  a large file with bounded memory on both targets.
- Two test projects sharing `tests/Sfe.Tests/Infrastructure`: reference the project or move the helpers to a shared
  one; don't copy them.
- `info.version` of the document is the app's version: the committed baseline changes at each release unless the
  comparison ignores that field.
