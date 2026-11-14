# Plan: the API under `/api/v1`

Files: `src/Sfe.Api/Program.cs`, `Security/RequestGuard.cs`, `Security/SecurityHeaders.cs`,
`Tenancy/TenantMiddleware.cs`, `Endpoints/*Endpoints.cs` (links the server builds), `Security/FileLinks.cs`,
`FlashLinks.cs`, `FastSignIn.cs`; client `src/Sfe.Web/src/app/core/api.service.ts` and 18 other files; tests
`tests/Sfe.Tests/` (about 940 paths in 33 files), `tests/e2e/`; tools `agents/tools/`. Part 1 of the
[epic](external-access-epic-plan.md). Delete this plan once its last item is ticked.

## Starting point (2026-11-13, `edge` at `bbf446f`)

- `app.MapGroup("/api")` with 30 `Map…Endpoints` calls; one OpenAPI document `/openapi/v1.json` (its `info.version`
  is the app's version), Swagger UI at `/api/docs`.
- Path prefixes are tested by hand in `RequestGuard` (`/api/setup`, `/api/session`, `/api/installation`,
  `/api/migration`, `/api/docs`), `TenantMiddleware` and `SecurityHeaders`.
- The server builds paths in about 20 places (`api/files/shared/{token}`, `api/files/flash/…`, `api/files/archive/…`,
  `api/files/jobs/{id}`, `Created`/`Accepted` locations, `FastSignIn.Route`).
- The client names `api/…` 183 times in 19 files (151 in `api.service.ts`).

**Done when:** every route answers under `/api/v1`, the client and the tools use it, every old path still answers the
same, a test refuses a change that breaks v1, and the rules are written for agents and users.

## Progress

### Phase 0: rules

- [x] New note `agents/docs/development/api-versioning.md` (indexed in AGENTS.md): the rules of the section "Version
  rules" below
- [x] [api-routes](api-routes.md) links to it

### Phase 1: routes under `/api/v1`

Order (decided 2026-11-13): phase 1 keeps `ApiRoutes.Base = "/api"` (a refactoring, nothing answers differently);
`Base` becomes `"/api/v1"` in the same commit as the `LegacyApiPaths` middleware of phase 2, so no commit leaves the
client and the ~940 test paths on routes that don't answer.

- [x] `ApiRoutes` (one constant for the base, helpers for the links the server builds); `app.MapGroup` uses it
- [x] `RequestGuard`, `TenantMiddleware`, `SecurityHeaders` test their prefixes through `ApiRoutes`
- [x] Every built link and `Created`/`Accepted` location uses it (grep `"api/` and `"/api/` in `src/Sfe.Api`: none
  left outside `ApiRoutes`)

### Phase 2: old paths as aliases

- [x] `LegacyApiPaths` middleware (before `RequestGuard`): `/api/x` becomes `/api/v1/x`, with `Deprecation` and a
  `Link` to the new path; `/api/v1` and `/api/docs` untouched. As built (2026-11-14, with `ApiRoutes.Base =
  "/api/v1"`): first in the pipeline, then an explicit `app.UseRouting()` where WebApplication put it implicitly (it
  matched the endpoint before any middleware, so a rewrite after it changed nothing); `Deprecation: @1794614400` (RFC
  9745, 2026-11-14) and `Link: <…/api/v1/x>; rel="successor-version"`, no `Sunset` (2.0 has no date); `/api/v2/…`
  is never an alias; the OAuth return address `ApiRoutes.OAuthCallback` stays unversioned (registered at Dropbox)
  and answers without the deprecation; the 404 fallback now covers all of `/api/…` (else `/api/v2/x` got
  `index.html`).
- [x] One log line per caller and day for an old path (not per request). As built: `LegacyPathLog`, a warning per
  account (or address without one) and UTC day, written when the answer starts (the caller is known then), with the
  first two segments of the path only (tokens of links stay out) and the user agent; at most 10,000 callers a day,
  then one line saying so.
- [x] `LegacyPathTests`: every route of the endpoint data source answers at its old path with the same status as at
  the new one (anonymous, and signed in). As built: anonymous every method, signed in the GETs only (a route that
  changes something answers its second call differently whatever its path); parameters filled from the route
  pattern's constraints; a redirect followed (fast sign-in) excuses the missing header; also the unknown paths, the
  OpenAPI document (v1 paths only), the OAuth return address, a player link with an old path and a range, the log.

### Phase 3: client, tests and tools on v1

- [ ] Client: one base constant in `core/`, every path through it (`npm test`, e2e quick set)
- [ ] Server tests on v1 (the aliases are covered by `LegacyPathTests` only)
- [ ] `tests/e2e/`, `agents/tools/*` (docs-screenshots, visual-check, battery-probe, readability references)

### Phase 4: compatibility net

- [ ] The v1 document committed as the baseline (`tests/samples/openapi/v1.json`, updated by a documented command)
- [ ] A test fails when the current document breaks the baseline (see "Version rules"); adding is allowed and only
  asks for the baseline to be updated in the same commit
- [ ] Problem `type` words listed in one place (`ProblemTypes`), the same list in `api-versioning.md` and the user docs

### Phase 5: user docs and release

- [ ] `docs/reference/api.md` (new): base address, versions, what stays compatible, old paths and when they end,
  `X-SFE-Request`, where the OpenAPI document and Swagger UI are; `docs/upgrading.md`; `CHANGELOG.md`
- [ ] Facts that stay true moved to `api-versioning.md` and [api-routes](api-routes.md); this plan deleted

## Version rules (content of `api-versioning.md`)

- The version is in the path (`/api/v1`): `<img>` and `<video>` can't send headers.
- **Breaking** (never inside v1): a route, parameter, property or enum word removed or renamed; a new required
  input; a status or a `type` word changed for the same case; a default changed; a right needed where none was.
- **Allowed** in v1: new routes, new optional inputs, new properties in answers, new `type` words for new cases.
  Clients must ignore properties they don't know (written in the user docs and kept by the C# library).
- A breaking change makes `/api/v2` **for that route group only**; v1 keeps answering as before until a major version
  of SFE removes it; each version has its own OpenAPI document.
- Errors are ProblemDetails; the `type` word is the contract, the text is for people.
- Lists are paged with a cursor ([scale](scale.md)); a new list never returns everything.
- The unversioned `/api/…` paths are another name of v1 until SFE 2.0, and nothing new is added to them: a route born
  after this plan exists only under `/api/v1`.

## Traps

- Mapping the routes twice (`/api` and `/api/v1`) fails at start on duplicate `.WithName`, and doubles the OpenAPI
  document: hence the rewrite.
- The rewrite must come before `RequestGuard` and before routing picks the endpoint (`UseRouting` is implicit: set
  `context.Request.Path` in a middleware placed before `UseAuthentication`, and check `MapFallback` still answers 404
  for unknown `api/` paths, not `index.html`).
- Links made before the upgrade (external player tokens up to 7 days, flash links, one-time archive links) carry old
  paths: they open through the aliases; a test makes a token link with an old path by hand.
- A browser tab holding the old client keeps calling old paths: served by the aliases until it reloads.
- `references.mjs --only openapi,routes`: the document and `routes.txt` differ by the prefix only; review the diff
  once, then write new references.
- The virtual directory: the client's paths stay relative ([frontend](frontend.md)).
- `ServerModeTests`, `LocalStartTests` and the setup/migration gates depend on the guard's prefixes: run them after
  phase 1 and again after phase 2 (old and new path for each gate).
- Swagger UI's `SwaggerEndpoint("../../openapi/v1.json")` is relative to `/api/docs/index.html`: unchanged.

## Checks per commit

`dotnet test`, `npm test` when the client changed, the e2e quick set in phase 3
([testing-e2e](testing-e2e.md)), `AccessTests.EveryRouteNamesARight`, `ApiTests.WebClientHasNoExternalResources`.
