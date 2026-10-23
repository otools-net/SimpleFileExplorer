# Sessions and the request pipeline

Code: `src/Sfe.Api/Security/` (`SfeSession`, `CurrentAccess`, `RequestGuard`, `RequireRight`), `Program.cs`;
client `src/Sfe.Web/src/app/core/api.service.ts`.

- **Session:** a cookie (`sfe.session`: user id, security stamp, library id; `SfeSession`), because `<img>`/`<video>`
  send cookies and nothing else. `OnValidatePrincipal` rejects it when the stamp changed (password, disabled, kind,
  admin flag) through `AccessService.GetSessionUserAsync` (cached until `AccessChanges.Version` moves). Data Protection
  keys persist in `Sfe:Security:KeysDirectory`.
- **Cookie names per port** (`InstanceCookies`, `InstanceCookieManager`): browsers keep cookies per host, not per port,
  so two instances on `localhost:5000` and `:5001` shared `sfe.session`, each sign-in signing the other out (its keys
  can't read the other's). The port of the request's Host is added: `sfe.session-5000`, `sfe.guest-5000`; no port (80,
  443, or a proxy passing none): the names as before. A session under the shared name is still read when there is
  none under the new one; once `ValidateAsync` accepts it, it is renewed under the new name and the shared one deleted
  (one that doesn't decrypt is another instance's: left alone). A virtual directory already separates instances (the
  cookie's path). Browser storage (`localStorage`) is per origin, port included: not shared. Checked in Edge by
  `agents/tools/visual-check/two-instances.mjs`.
- **Pipeline:** `UseForwardedHeaders` (loopback proxies, plus `Sfe:Security:TrustedProxies`) → HSTS when
  `RequireHttps` → `SecurityHeaders` (CSP, nosniff, `X-Frame-Options`, `Referrer-Policy` on every answer; a route's
  own CSP wins, e.g. library files' sandbox) → authentication → `RequestGuard` (503 `migration-required` while an update is due, except
  `api/migration`; 503 `setup-required` until an administrator exists, except `api/setup` and `api/session`; 400 for
  non-GET `/api` without `X-SFE-Request`; `/api/docs` needs `system.apiDocs`) → authorization (`RequireRight` policies
  `right:a,b`, `RightHandler` reads `CurrentAccess`) → rate limiter (`sign-in` per address; `RequestLimits` `media`,
  `search`, `upload` per account or address, so unauthorized requests are refused before they count) → `TenantMiddleware`.
- **Sign-in throttle:** `SignInThrottle` (memory) counts failures per name + address (`MaxFailedSignIns`); the
  database locks the account itself only after `AccountLockoutFailures` from everywhere (`LocalUserDirectory`).
- **CSP:** the client must keep no inline script or `on…=` handler in `index.html` (`TheWebClientHasNoInlineScripts`);
  Swagger UI (`api/docs`) gets `DocsPolicy` with inline scripts.
- **Client:** `Api` sends `X-SFE-Request` on non-GET and `X-SFE-Tenant` once `ConfigService.load` knew the library; a
  401 opens `login?return=`, a 503 `setup-required` opens `setup`, a 409 `tenant-changed` reloads (another tab
  switched); `redirected(error)` tells pages not to show "not reachable" then. Session/setup calls never redirect
  (their pages handle the answers).
- Related: [rights.md](../features/rights.md), [tenants.md](tenants.md), [update-migration.md](../features/update-migration.md).
