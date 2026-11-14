# Plan: the C# client library (`Sfe.Client`)

Files: new `src/Sfe.Client/`, `tests/Sfe.Client.Tests/`, `samples/Sfe.Client.Sample/`; `Sfe.slnx`,
`Directory.Build.props`, `build/build.ps1`, `build/build.sh`, `.csharpierignore`, `agents/tools/readability/`.
Part 4 of the [epic](external-access-epic-plan.md); needs [api-v1-plan](api-v1-plan.md) and
[api-tokens-plan](api-tokens-plan.md) finished. Delete this plan once its last item is ticked.

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
  (`tests/samples/openapi/v1.json` of [api-v1-plan](api-v1-plan.md)).
- Targets `netstandard2.0` and `net8.0`; System.Text.Json; as few dependencies as the generator allows.
- Version = the app's (`Directory.Build.props`); the library works with every server that has v1 and says so when a
  function is missing there.
- Not published to nuget.org: `dotnet pack` output (`.nupkg`, symbols) joins the release artifacts.

## Progress

### Phase 0: spike (result written here before phase 1)

- [ ] Generate from the real document with Kiota, NSwag and Refitter; choose by: reads the document as emitted
  (OpenAPI 3.1 by .NET 10, or 3.0 if the server must emit that for the generator), both targets, streaming request
  and answer bodies (uploads, ranged downloads), dependencies pulled into the host app, no network at build
- [ ] Decide whether the generator runs at build (`dotnet-tools.json`) or its output is committed (preferred: committed,
  with a test that regenerating changes nothing)

### Phase 1: project and generated layer

- [ ] `src/Sfe.Client` in `Sfe.slnx`, package metadata, XML docs file, nullable, the analyzers of the solution
- [ ] Generated client under `Generated/` (excluded from CSharpier and `measure.mjs`), the regeneration command in
  [commands](../environment/commands.md) and a README in `agents/tools/` if it needs a script
- [ ] `tests/Sfe.Client.Tests`: the in-process server of `tests/Sfe.Tests/Infrastructure` (its `HttpClient` given to
  the library), a token made by the test

### Phase 2: core of the facade

- [ ] `SfeClient`, `SfeClientOptions` (base address, token, library, timeouts, retries), `AddSfeClient` for DI
  (`IHttpClientFactory`)
- [ ] Errors: `SfeApiException` (status, `type` word, detail) with subclasses for not found, refused, conflict,
  rate limited, server not ready (`setup-required`, `migration-required`, `local-only`)
- [ ] Retries on 429 and 503 honouring `Retry-After`; cancellation tokens on every call; no retry of non-idempotent
  calls without an id the server knows
- [ ] `sfe.Server.GetInfoAsync()` (version, functions on) read once; `SfeNotSupportedException` when a function is
  missing on that server

### Phase 3: files

- [ ] `Libraries`, `Files.ListAsync` (`IAsyncEnumerable`, pages fetched as read), `GetAsync`, `ExistsAsync`
- [ ] `DownloadAsync` (to a stream, ranges, resume), `OpenReadAsync`
- [ ] `UploadAsync` (resumable chunks, progress, resume after a failure, conflict choice), folder uploads
- [ ] `CreateFolderAsync`, `RenameAsync`, `MoveAsync`, `CopyAsync`, `DeleteAsync`; `SfeJob` (`WaitAsync`, progress,
  conflicts answered by a callback or a fixed choice, cancel)
- [ ] `Trash`, `Properties` (totals, checksums), `Archives` (zip of several items to a stream)

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
