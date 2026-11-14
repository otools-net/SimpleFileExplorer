# API routes (minimal APIs)

Code: `src/Sfe.Api/Endpoints/*Endpoints.cs` (one class per area), `Program.cs`.

- What may change in a published route, and when a new version is needed: [api-versioning](api-versioning.md).
- Routes are mapped on the group `Program.cs` makes at `ApiRoutes.Base` (`/api/v1`): a `Map…Endpoints` names its
  path from there (`/tags`). No literal `"api/` in `src/Sfe.Api` outside `ApiRoutes`: a link the server hands out is
  `ApiRoutes.Link(…)`, a `Created`/`Accepted` location `ApiRoutes.Location(…)`.
- A new or changed route changes the OpenAPI document: write the baseline again in the same commit
  ([the compatibility net](api-versioning.md#the-compatibility-net)); `OpenApiBaselineTests` says when to.
- A problem's `type` is a word of `ProblemTypes` (a new case: a new word, listed in api-versioning.md and
  `docs/reference/api.md`); the rights of `.RequireRight` show in the document as `x-sfe-rights`.
- Every route gets `.WithName`, `.WithSummary`, `.WithTags` (and `.WithDescription` for details): routes are
  documented by OpenAPI (`/openapi/v1.json`, Swagger UI at `/api/docs`), not in `docs/`.
- Every route names its right(s): `.RequireRight(Rights.X)` or `.AllowAnonymous()` ([rights.md](../features/rights.md)).
- Anything non-GET under `/api` without `X-SFE-Request` is a 400 (curl, PowerShell, tests): add the header.
- A POST endpoint taking a JSON body param doesn't match a request without `Content-Type: application/json`, and
  `api.MapFallback` then answers 404 (not 415): take optional inputs of bodyless actions from the query
  (`POST api/tasks/{task}/start?path=`).
- A route handler whose only parameter is `HttpContext` binds as a `RequestDelegate` and its result is silently dropped
  (warning ASP0016): give it another parameter (`CancellationToken`) or cast it to `Delegate`.
- Routes get library services through `TenantRequestServices` ([tenants.md](../architecture/tenants.md)).
- A `Map…Endpoints` method only lists routes, one `Map<Group>` method per group or subject past 60 lines; long
  descriptions are fields named after the route (`ExploreArchiveDescription`), next to the method that maps it.
- A handler takes at most 5 parameters: the services its routes share go in one record bound with `[AsParameters]`
  (`ExploreServices`: services and `HttpContext`, none of it in the OpenAPI document), query and header values in a
  request record next to the contracts (`ArchiveListQuery`), each with its binding and its name
  (`[FromQuery(Name = "path")]`, `[FromHeader(Name = …)]`): the OpenAPI document shows that name. Route values
  (`{id:long}`) stay parameters of the handler: inside an `[AsParameters]` record, OpenAPI documents a `long` as
  `integer` or `string`. Handlers start with `var (…) = services;` so their bodies keep their names.
- Check a change to route maps or handler parameters with `agents/tools/readability/references.mjs --only
  openapi,routes`: the OpenAPI document identical, and `routes.txt` the same rights, folder scopes and policies
  (its `metadata` lines change with `[AsParameters]`).
