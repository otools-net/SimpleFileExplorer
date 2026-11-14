# API versioning

Code: `src/Sfe.Api/ApiRoutes.cs`, `LegacyApiPaths.cs`, `LegacyPathLog.cs`, `Program.cs` (the route group),
`src/Sfe.Api/Endpoints/*Endpoints.cs`. Being put in place by [api-v1-plan](api-v1-plan.md) (the OpenAPI baseline and
`ProblemTypes` come with it);
route conventions are in [api-routes](api-routes.md).

- Every `api/` path the server tests or hands out goes through `ApiRoutes`: `IsApi`/`IsServerEntry`/`IsDocs` in the
  middleware, `Link` (relative to the app's address, for links) and `Location` (absolute, for `Created`/`Accepted`).
  `ApiRoutes.Docs` (Swagger UI) is outside the versions and never moves.
- The routes are mapped once, under `ApiRoutes.Base` (`/api/v1`). `LegacyApiPaths`, first in the pipeline, rewrites
  `/api/x` to `/api/v1/x` (not `/api/docs`, not `/api/v2/…`); `app.UseRouting()` is called right after it, because
  the implicit one of WebApplication picks the endpoint before any middleware runs. Mapping the routes twice would
  fail on duplicate `.WithName` and double the OpenAPI document.
- An old path's answer carries `Deprecation: @1794614400` (RFC 9745) and `Link: <…/api/v1/x>;
  rel="successor-version"`; `LegacyPathLog` writes one warning per caller (account, or address) and UTC day, with the
  path's first two segments only (link tokens stay out of the log), at most 10,000 callers a day.
- `ApiRoutes.OAuthCallback` (`/api/connections/oauth/callback`) is the one unversioned address kept for good: it is
  registered at the providers. It answers through the rewrite without `Deprecation`; `ReturnAddress` hands it out.
- The 404 fallback is mapped on all of `/api/…`, every version: an unknown API path never gets the client's
  `index.html`. Tested by `LegacyPathTests`.

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

- Errors are ProblemDetails; the `type` word is the contract, the text is for people. Today the words are constants
  next to their endpoints (`FileEndpoints.ExistsType`, `ExploreEndpoints.NotExplorableType`…).
- Lists are paged with a cursor ([scale](scale.md)); a new list never returns everything.
