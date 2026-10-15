# Libraries (tenants)

Code: `src/Sfe.Api/Tenancy/` (`TenantRegistry`, `TenantHost`, `TenantMiddleware`, `TenantRequestServices`),
`src/Sfe.Core/ServiceCollectionExtensions.cs` (`AddSfeCore`).

## Rules

- Core services are per library (`AddSfeCore` per `TenantHost`); routes get them through `TenantRequestServices`.
- Services of the whole server (access, settings levels, `FfmpegLimits`) are registered in the application and handed
  to each library's container (`TenantHost.StartAsync`).
- A root singleton must never depend on a Core type (the root's registrations of Core types throw: they only tell
  minimal APIs which parameters are services).
- A DI factory registration (`AddScoped(type, sp => …)`) owns what it returns: the scope disposes it. Never forward
  another container's singletons that way (that's why routes get library services through `TenantRequestServices`).

## How it works

- `TenantRegistry` (hosted service) starts a `TenantHost` per enabled library: a `ServiceCollection` with
  `AddSfeCore(config + overrides, contentRoot, tenantId)`, the application's log outputs,
  `SettingsStore`/`SettingLayers`/`FfmpegLimits` instances, and the workers; it initializes the library's index.
- The first library uses the configuration as it is; others get `Sfe:Root` = their root, their connection string
  (`TenantHost.ConnectionString`: template, else `tenants/{slug}.db` beside the SQLite file or `{db}_{slug}` on SQL
  Server) and `{CacheDirectory}/tenants/{slug}`.
- `TenantMiddleware` swaps `HttpContext.RequestServices` for `TenantRequestServices` (Core types from the library, the
  rest from the request) for `/api` and `/openapi`, except setup/session/docs.
- Which library a request uses: see [rights.md](../features/rights.md) (`CurrentAccess`).
