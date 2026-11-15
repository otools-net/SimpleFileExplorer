# C# client library (`Sfe.Client`)

Code: `src/Sfe.Client/` (`SfeClient`, `SfeClientOptions`, `SfeServiceCollectionExtensions`, one folder per area:
`Files/`, `Uploads/`, `Downloads/`, `Jobs/`, `Trash/`, `Details/`, `Archives/`, `Search/`, `Tags/`, `Libraries/`,
`Server/`, `Errors/`, `Http/`; `Generated/` by Kiota), `samples/Sfe.Client.Sample/`, `tests/Sfe.Client.Tests/`,
`agents/tools/client-generation/`, `build/build.ps1` / `build.sh` (pack). User docs:
[client-library](../../../docs/reference/client-library.md). Plan of what is left (connections):
[client-library-plan](../development/client-library-plan.md).

## Design

- Two layers in one package (`netstandard2.0` + `net8.0`, namespace `Otools.Net.Sfe.Client`, package id
  `Sfe.Client`): the **facade** (`sfe.Files`, `Search`, `Tags`, `Trash`, `Properties`, `Archives`, `Libraries`,
  `Server`) and **`sfe.Raw`**, Kiota's client of every v1 route (administration included), on the same `HttpClient`.
- The facade does **not** call through Kiota: `Http/SfeTransport` sends hand-written JSON calls (`SfeJson`:
  camelCase, numbers from strings, unknown enum words read as `Unknown` by `TolerantEnumConverter`, unknown
  properties ignored), so retries, timeouts and errors are in one place and the public models are its own (`SfeItem`,
  `SfeTag`…). Answers are read into `internal` contract classes (`…Contracts.cs` per folder) or the public model
  itself.
- No reference to `Sfe.Core` / `Sfe.Api`: the generated layer comes from the committed v1 document; the facade's
  routes are strings in `Http/Routes`.
- The token and `X-SFE-Tenant` go on each request, never on the `HttpClient` (one from `IHttpClientFactory` stays
  shareable). `(Uri, token)` / `(options)` make and dispose their own client; `(HttpClient, options)` leaves it open.
- A method per task, not per route; no route, header or chunk size in a public signature; XML docs on every public
  member; paths are the API's with `/` (`SfePath`).

## Errors and retries

- `SfeApiException` (`StatusCode`, `ProblemType` = the word only, `Title`, `Detail`, `Errors`), subclasses
  `SfeNotFoundException` (404), `SfeRefusedException` (401/403), `SfeConflictException` (409),
  `SfeRateLimitedException` (429, `RetryAfter`), `SfeServerNotReadyException` (by word first: `setup-required`,
  `migration-required`, `local-only`); `SfeNotSupportedException` (a function the server lacks:
  `Server.RequireAsync(SfeFunction)`), `SfeJobConflictException`.
- 429 and 503 are retried (`Retry-After`, else `RetryDelay` doubled; longer than `MaxRetryDelay` fails at once) only
  when `SfeRequest.CanRetry`: GET, HEAD, DELETE, and calls marked safe (`uploads/check`, chunk `PUT`s). A `POST` never.
  A 503 with a not-ready word is never retried. `RequestTimeout` covers until the headers (the client's own
  `HttpClient.Timeout` is infinite, so downloads may last).

## Jobs, uploads, downloads, paging

- Moves and copies answer an `SfeJob` (`WaitAsync` polls every `PollInterval`, clashes answered by a fixed choice or
  `OnConflict`; none: `SfeJobConflictException`). After an answer the server's job leaves `waiting` a moment later:
  `AnswerAsync` asks again for up to 1 s.
- Uploads: 8 MB chunks from one byte buffer (`ByteArrayContent`: no body of unknown length, the .NET Framework trap),
  413 halves it, a cut-off chunk asks the server's offset and goes on, an unfinished upload of the same file is resumed
  (`Resume`), a cancelled one deleted.
- Downloads: a body cut off goes on with `Range` + `If-Range` (the first answer's `Last-Modified`).
- Folder listings page by offset; search pages by the server's keyset cursor (`next` → `after`, the same sort), the
  first page carrying the count (`Search.CountAsync` asks a page of 1). Tag changes go in batches of 1000 files (the
  server's `MaxFilesPerChange`), a sum of the answers.

## Compatibility and tests

- `tests/Sfe.Client.Tests` runs the real server in process (`Infrastructure/TokenServer`: a token of the
  administrator, `Sfe:Mode=External`; `IndexAsync` scans for the search tests). Every facade method has a test there.
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

## Packaging

- Version from `Directory.Build.props` (the app's); `build/build.ps1` and `build.sh` run
  `dotnet pack src/Sfe.Client -c Release -p:Version=<version> -o artifacts` for every target: `Sfe.Client.<v>.nupkg`
  and `.snupkg` join the release artifacts. Not published to nuget.org.
- The package holds both targets' DLLs and XML docs, and `src/Sfe.Client/README.md` (`PackageReadmeFile`).
  Dependencies: Kiota's Abstractions, HttpClientLibrary and Serialization.Json, `Microsoft.Extensions.Http`, and
  `Microsoft.Bcl.AsyncInterfaces` on `netstandard2.0`.
- The sample references the project; a program outside the repository references the package.
