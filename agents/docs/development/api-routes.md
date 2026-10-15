# API routes (minimal APIs)

Code: `src/Sfe.Api/Endpoints/*Endpoints.cs` (one class per area), `Program.cs`.

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
