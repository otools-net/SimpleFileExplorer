# API versioning

Code: `src/Sfe.Api/ApiRoutes.cs`, `LegacyApiPaths.cs`, `LegacyPathLog.cs`, `Program.cs` (the route group),
`src/Sfe.Api/Endpoints/*Endpoints.cs`, `ProblemTypes.cs`, `ApiDocument.cs`; client `core/api.service.ts`
(`API_BASE`); tests `LegacyPathTests`, `OpenApiBaselineTests`, `ProblemTypesTests`. Route conventions are in
[api-routes](api-routes.md); the user page is `docs/reference/api.md`. Set up 2026-11-13/14 (plan 1 of the
[epic](external-access-epic-plan.md), done).

- Every `api/` path the server tests or hands out goes through `ApiRoutes`: `IsApi`/`IsServerEntry`/`IsDocs` in the
  middleware, `Link` (relative to the app's address, for links) and `Location` (absolute, for `Created`/`Accepted`).
  `ApiRoutes.Docs` (Swagger UI) is outside the versions and never moves.
- The routes are mapped once, under `ApiRoutes.Base` (`/api/v1`). `LegacyApiPaths`, first in the pipeline, rewrites
  `/api/x` to `/api/v1/x` (not `/api/docs`, not `/api/v2/…`); `app.UseRouting()` is called right after it, because
  the implicit one of WebApplication picks the endpoint before any middleware runs. Mapping the routes twice would
  fail on duplicate `.WithName` and double the OpenAPI document.
- An old path's answer carries `Deprecation: @1794614400` (RFC 9745, the date v1 came: 2026-11-14) and `Link:
  <…/api/v1/x>; rel="successor-version"`, no `Sunset` (2.0 has no date); `LegacyPathLog` writes one warning per caller (account, or address) and UTC day, with the
  path's first two segments only (link tokens stay out of the log), at most 10,000 callers a day.
- `ApiRoutes.OAuthCallback` (`/api/connections/oauth/callback`) is the one unversioned address kept for good: it is
  registered at the providers. It answers through the rewrite without `Deprecation`; `ReturnAddress` hands it out.
- The 404 fallback is mapped on all of `/api/…`, every version: an unknown API path never gets the client's
  `index.html`. Tested by `LegacyPathTests`.
- `LegacyPathTests` is the only test calling old paths: every route of the endpoint data source answers at its old
  path with the status of its new one (anonymous every method; signed in the GETs only, since a route that changes
  something answers its second call differently), parameters filled from the route pattern's constraints, a
  redirect followed (fast sign-in) excusing the missing header; plus unknown paths, the OpenAPI document (v1 paths
  only), the OAuth return address, a player link with an old path and a range, the log line.
- Links handed out before v1 (external player tokens, up to 7 days; Flash links; one-time zip links) carry old paths
  and open through the aliases; a browser tab still holding an older client calls old paths until it reloads.
- `ServerModeTests`, `LocalStartTests` and the setup and migration gates depend on the guard's prefixes
  (`ApiRoutes.IsServerEntry`…): run them after touching `ApiRoutes`, `LegacyApiPaths` or `RequestGuard`.
- Swagger UI's `SwaggerEndpoint("../../openapi/v1.json")` is relative to `/api/docs/index.html`; `/api/docs` and
  `/openapi/v1.json` are outside the versions.

## Client, tests and tools

- Client: `API_BASE = 'api/v1/'` in `core/api.service.ts` (re-used by `config.service.ts`, archive entries, epub,
  the change feed, the backup link). `Api`'s private `get`/`post`/… and its direct `http` calls prefix it, so routes
  there read `'tags'`, `` `files/jobs/${id}` ``; `withVersion` marks only `API_BASE` addresses. Paths stay relative
  (virtual directory, [frontend](frontend.md)). Only `api/docs` (link in System › About) is unversioned.
  `public/flash/player.js` accepts `/api/v1/files/flash/` links only (the server makes those).
- Server tests: `ApiPaths.V1` (`= ApiRoutes.Base`), imported in every test file by `<Using Static="true">` in
  `Sfe.Tests.csproj`: `$"{V1}/tags"` (a constant, so `[DataRow]` takes it). Route patterns with `{…}` in
  `AccessTests`' `[DataRow]`s stay literal `/api/v1/…`.
- `tests/e2e/` and `agents/tools/*` call `api/v1/…` (`menu-profile.spec.ts` filters on `/api/`, both forms).
- `references.mjs --only openapi,routes` compares a refactoring's document and route list; a change of the base shows
  as the prefix only.

## Where the version is

- The version is in the path: `/api/v1/…`. Not a header: `<img>`, `<video>` and download links can't send one.
- Each version has its own OpenAPI document (`/openapi/v1.json` for v1).
- The unversioned `/api/…` paths are another name of v1 until SFE 2.0 (`Deprecation` header, out of the OpenAPI
  document). Nothing new is added to them: a route born after v1 exists only under `/api/v1`.

## What may change inside v1

- **Breaking, never inside v1:** a route, parameter, property or enum word removed or renamed; a new required input;
  a status or a problem `type` word changed for the same case; a default changed; a right needed where none was.
- **Allowed in v1:** new routes, new optional inputs, new properties in answers, new problem `type` words for new
  cases. Clients must ignore properties they don't know (the user docs say so; the C# library keeps to it).
- A breaking change makes `/api/v2` **for that route group only**; v1 keeps answering as before until a major version
  of SFE removes it.

## Answers

- Errors are ProblemDetails; the `type` word is the contract, the text is for people. A problem without a word of its
  own carries ASP.NET Core's default (a link to its status in RFC 9110).
- Lists are paged with a cursor ([scale](scale.md)); a new list never returns everything.

## Problem types

Every word is a constant of `ProblemTypes` (`src/Sfe.Api/ProblemTypes.cs`, with `All`); the constants next to the
endpoints (`FileEndpoints.ExistsType`, `ConnectionProblems.*`, `ExploreEndpoints.*`, `RequestGuard.*`…) refer to it.
A new case adds a word there, here and in `docs/reference/api.md`; `ProblemTypesTests` checks that every
`type:` in `src/Sfe.Api` comes from the list and that this note lists every word. The OpenAPI document carries the
list (`x-sfe-problem-types`), so the baseline refuses a word removed.

| Word | Status | Case |
|---|---|---|
| `access-gains` | 409 | a move would let more people see or change a folder of a connection: confirm first |
| `archive-reading` | 409 | the archive's list is still being read: try again in a moment |
| `archive-unreadable` | 422 | the archive (or an entry) can't be read |
| `connection-needs-reconnecting` | 409 | the provider no longer accepts the connection's sign-in |
| `connection-not-indexed` | 409 | the library's index doesn't hold the connection's files yet (its sync started) |
| `connection-not-supported` | 409 | the provider lacks the function |
| `connection-rate-limited` | 429 | the provider asks to wait (`Retry-After`) |
| `connection-refused` | 403 | the provider doesn't let this account do it |
| `connection-unavailable` | 502 | the provider can't be reached or failed |
| `connection-unusable` | 409 | the connection is off, or its kind or level no longer allowed |
| `file-changed` | 412 | the text file changed since it was read (its version in `version`) |
| `file-exists` | 409 | something of that name is already there |
| `file-is-link` | 409 | the file is a link: not saved over |
| `folder-rules` | 409 | the folder access rules can't follow a folder there |
| `job-running` | 409 | a retry asked while the job still runs |
| `library-root-missing` | 404 | the library's folder doesn't exist on the server |
| `local-administrators-only` | 400 | sign-in refused: a Local server lets only administrators in |
| `local-only` | 403 | a Local server answers its own computer only |
| `migration-required` | 503 | the server waits for its update |
| `not-encodable` | 422 | characters the file's encoding can't write |
| `not-explorable` | 409 | the file can't be explored as an archive |
| `not-protected` | 409 | nothing in the archive has a password |
| `not-text` | 415 | the file is not text |
| `nothing-to-retry` | 409 | a retry of a job with nothing left to start again |
| `read-only` | 409 | the file is read-only: not saved over |
| `scan-running` | 409 | a scan runs there: try again once it ends |
| `session-only` | 403 | a route for signed-in sessions only (sign-in, password, setup, tokens…) called with an access token |
| `setup-required` | 503 | the server has no administrator yet |
| `tenant-changed` | 409 | the session is in another space now: reload |
| `too-large` | 413 | too large to show or save as text, or to read from an archive |
| `version-required` | 428 | a save over a file without `If-Match` |
| `wrong-password` | 403, 422 | the archive needs a password, or the one given doesn't open it |

## The compatibility net

Code: `tests/Sfe.Tests/OpenApiBaselineTests.cs`, `ApiBreaksTests.cs`, `Infrastructure/OpenApi/` (`ApiContract`,
`ApiSchemas`, `ApiBreaks`), `src/Sfe.Api/ApiDocument.cs`; baseline `tests/samples/openapi/v1.json`.

- The v1 document (`/openapi/v1.json`) is committed as `tests/samples/openapi/v1.json`. `OpenApiBaselineTests`
  reads both into facts (`ApiContract`: routes, parameters, bodies, statuses and their content types, rights,
  schemas, properties, enum words, problem types; texts and the version left out) and compares them.
- `TheApiKeepsTheV1ContractOfTheBaseline` fails on a break (`ApiBreaks`): a route, parameter, body, status, content
  type, schema, property, enum word or problem type word removed or renamed; a new required parameter, body or
  property of what clients send, or one made required; a type that accepts less in what clients send or gives more in
  what they receive (`null` added to an answer's property, too); a property of an answer that may now be missing; a
  default changed; a right needed where none was (each requirement must be met by the rights of one requirement of
  the baseline).
- `TheBaselineHoldsEverythingTheApiOffers` fails while the document differs from the baseline in any fact, added ones
  too: what is added (and passes the first test) is written into the baseline **in the same commit**, so it is kept
  from then on. Summaries and descriptions don't count: no update for a text.
- Write the baseline (from the repository root; PowerShell: `$env:SFE_OPENAPI_BASELINE='write'; dotnet test …`):

  ```sh
  SFE_OPENAPI_BASELINE=write dotnet test tests/Sfe.Tests --filter FullyQualifiedName~OpenApiBaselineTests.WritesTheBaselineWhenAsked
  ```

  After an addition only. After a break, never inside v1: the change goes to `/api/v2` for its route group, or waits
  for a major version of SFE.
- The rights are in the document because of the net: `ApiDocument` adds `x-sfe-rights` to each operation (one list
  per requirement, any one of its rights; none on an anonymous route) and `x-sfe-problem-types` to the document.
- Schemas are told apart by the way they travel (`ApiSchemas.FlowsOf`: reached from bodies and parameters, from
  answers, or both): a schema used both ways can't widen nor narrow a type.
- A schema renamed in C# is a break too (generated clients name their classes after it).
