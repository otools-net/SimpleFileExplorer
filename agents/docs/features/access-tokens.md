# Access tokens

Code: `Core/Access/AccessTokens.cs`, `AccessTokenSecret`, `AccessTokenModels`, `AccessTokenRights`,
`Core/Data/Entities/Access/AccessToken.cs`, `Data/AccessMigrations/20261114172624_AccessTokens`,
`Core/Options/AccessTokenOptions`, `Core/Settings/AccessTokenSettings`; `Sfe.Api/Security/AccessTokenAuthentication`,
`BearerTokens`, `CurrentAccess`, `RequestGuard`, `SessionOnlyRoutes`, `SessionOnlyMetadata`, `AccessTokenAudit`,
`JobToken`, `FileJobAccess`, `ConnectionJobAccess`; `Endpoints/TokenEndpoints`, `TokenAdministration`,
`AccessTokensConfig`, `ApiDocument`; client `src/app/tokens/`, `core/access-token.models.ts`,
`styles/access-tokens.css`. User docs: `docs/reference/api.md#calling-with-a-token`,
`docs/administration/my-account.md#access-tokens`, `spaces-and-accounts.md#access-tokens`. Attack vectors and their
tests: [security](../development/security.md#access-tokens).

Personal access tokens let a program call any v1 route with `Authorization: Bearer <token>`, as its account.

## Decisions

- A token **acts as its account**: rights = the account's now ∩ the token's list (null list = all of the account's).
- A token may be limited to one library; else the library is named by `X-SFE-Tenant` (its id) among those the
  account reaches, else the session rule (first reached, else the default).
- A password change does **not** end tokens (confirmed by the owner 2026-11-14; the security stamp isn't compared);
  said in the user docs.
- A lockout after wrong passwords doesn't end them either (it would let a stranger stop an account's programs).
- A disabled or deleted account ends its tokens (checked at each request); "Revoke all" ends them too.
- Local mode: the session's checks apply (this computer, administrators only).
- Rights `account.tokens` (own tokens) and `access.tokens` (see and revoke those of the spaces one administers):
  tenant rights, Write, Administrators group, never guests, `since` 16.
- Room left for OAuth: the row has a nullable `ClientId` and a `Kind` (`Personal` only); nothing else is built.

## Model and format

- Table `AccessTokens` (access database): `Lookup` varchar(16) unique, `Hash` varbinary(32), `Prefix` (shown),
  `Rights` (ids separated by spaces, null = all), tenant, `Kind`, `ClientId`, created, expires, `LastUsed` /
  `LastUsedFrom`, `Revoked` / `RevokedById`.
- `RevokedById` has no foreign key (a second path from `Users`). Cascades from `Users` and from `Tenants`: a token
  limited to a deleted library goes with it, never becomes unlimited.
- Token `sfe_<16 hex lookup>_<43 base64url>` (`AccessTokenSecret`): 32 random bytes; only their SHA-256 is kept.
- The lookup finds the row; the hash is compared with `FixedTimeEquals`; an unknown lookup is compared with a zero
  hash (same work).
- The lookup is not the row id (an id would tell how many tokens exist).
- Revoking keeps the row (`Revoked`): lists show it, faded. Revoked and expired rows are never purged (yet).
- `CreateAsync` returns the secret once: expiry within `MaxLifetimeDays` (default the longest), a library the
  account reaches, rights known and held there (or in any library it reaches), `MaxPerAccount` counting tokens
  neither revoked nor expired.
- `FindAsync` caches rows until `AccessChanges.Version` moves (revoking moves it); unknown lookups are never cached.
- The last use is written at most every 5 minutes per token.

## Authentication

- `AccessTokenAuthentication` (scheme `AccessToken`) sits behind the default policy scheme `Sfe`: its selector takes
  it whenever `BearerTokens.IsBearer` (the header starts with `Bearer `), else the cookie.
- A bearer request never reads the cookie and needs no `X-SFE-Request` (no ambient credential to abuse).
- Its 401 carries `WWW-Authenticate: Bearer error="invalid_token"` and no `Set-Cookie`.
- The handler fails while `MigrationCoordinator.IsBlocking` (no table yet) and when tokens are off.
- It reads the account through `AccessService.GetSessionUserAsync` at each request (the cookie's
  `OnValidatePrincipal` doesn't run for it): disabled or deleted refused; a Local server takes administrators only.
- Claims: the cookie's (id, name, stamp, the token's library as `sfe:tenant`) plus `sfe:token`; the
  `PresentedAccessToken` is in the request's features (`BearerTokens.TokenOf`).
- A token in the query (`?access_token=`) is never read.
- `RequestGuard`: after the migration, setup and Local gates, a bearer whose token wasn't accepted gets 401 on any
  route, never a visitor's rights instead.
- `CurrentAccess`: a library the token isn't for or the account doesn't reach gives no library and no rights (403);
  `TenantMiddleware` answers 403 instead of 409 `tenant-changed` to a bearer.

## Session-only routes

- `.SessionOnly()` (`SessionOnlyMetadata`) on a group refuses a bearer with 403 `session-only`.
- Session-only: the groups `session` (sign-in, password, fast sign-in, recovery, space switch), `setup`,
  `installation`, `migration`, `me/tokens`, `access/tokens`. Sign-in directories have no route (configuration only).
- `SessionOnlyRoutes` lists them; `AccessTokenRouteTests.EveryRouteLettingSomeoneInOrHandlingTokensIsForSessionsOnly`
  checks the list both ways: a new route letting someone in must be added.
- In OpenAPI (`ApiDocument`) every operation asks for scheme `accessToken` (http bearer) or nothing (the cookie);
  session-only ones carry `x-sfe-session-only: true` and ask for no scheme, so Swagger UI calls them with the cookie.

## Jobs

- A job started with a token carries a `JobToken` (`CurrentAccess.JobToken()`).
- `FileJobAccess` (move, copy, compress, extract, transfers' library side) and `ConnectionJobAccess`
  (`ConnectionJobSubject.Token`) check each item with the token's rights over the account's.
- Once the token is revoked or expired (`AccessTokens.IsActiveAsync`, cached until `AccessChanges` moves), nothing
  more: `FileJobs` and `TransferJobs` start each item with `JobRun.BeginAsync`, which asks
  `IFileJobAccess.StoppedAsync` and reports the rest Forbidden with `JobToken.StoppedProblem`.
- A session's job is never stopped this way. Uploads check each part in its own request.

## Throttle and logs

- Wrong tokens go through `SignInThrottle` under a reserved name (`AccessTokenAuthentication.ThrottleName`) per
  `AddressKeys.Of`: `MaxFailedSignIns` wrong tokens lock the address (IPv6: its /64) out of every token for
  `LockoutMinutes`.
- The security warning names a wrong token by `sfe_<lookup>` only, with the address, never a secret character.
- `AccessTokenAudit` (after authentication) logs every bearer request but GET/HEAD/OPTIONS at information
  (`Sfe.Api.Security.AccessTokenAudit`): "POST /api/v1/tags answered 201 to admin through access token 5 “name”".
- `ProblemLog` names the token too (`BearerTokens.Describe`).
- `RequestLimits` partitions per account: a token counts as its account (no extra budget by making tokens).

## Routes

- `GET me/tokens` → `AccessTokenInfo[]`; `POST me/tokens` (`AccessTokenDraft`: `name`, `expires?`, `tenantId?`,
  `rights?`) → 201 `{ token, secret }`, 400 validation, 403 tokens off, 409 too many.
- `DELETE me/tokens/{id}` → 204, 404 for another's; `POST me/tokens/revoke-all` → `{ revoked }`.
- `GET access/tokens`, `DELETE access/tokens/{id}` (`TokenAdministration`): server administrators every token,
  others those of the accounts of the spaces where they hold `access.tokens`, never a server administrator's (404).
- `GET config` has `accessTokens` (`{ enabled, maxLifetimeDays }`), null for a session holding neither token right.

## Client

- `src/app/tokens/`: `MyTokensComponent` (My account › Access tokens, tab `tokens` of `AccountSection`),
  `AllTokensComponent` (Spaces & accounts › Access tokens, in `AccessSection`), `TokenRowComponent`
  (`li[sfeTokenRow]`, both lists), `TokenCreateComponent` (the sheet), `token-parts.ts`.
- The secret lives only in the sheet's signal, cleared when the sheet closes and on destroy.
- `tokensConfig` reads `config.accessTokens` (absent = off). Without a longest life the sheet offers 7/30/90 days,
  1 year and "The longest allowed" (null: the server's default); the server refuses a longer one (400 `expires`).
- Rights to pick come from `config.rights` (current library), titled and grouped by `api/access/rights` when the
  session may read it, else by id; the server checks them for the library chosen.
- Sub-pages are declared in `tokens/token-pages.ts`; the settings tab in `tokens/token-settings.ts`, spread into
  `TABS` (`admin-sections.ts` and `settings-schema.ts` sit at their readability baseline).
- While tokens are off, My account › Access tokens says so and offers no New token (list and Revoke stay).
- Specs: `tokens/my-tokens.spec.ts`, `tokens/all-tokens.spec.ts`; builder `accessToken()` in `testing/fakes.ts`.

## Settings

- `Security:AccessTokens:Enabled` (default true), `MaxLifetimeDays` (365; 0 no limit; at most 3650),
  `MaxPerAccount` (20; 1 to 1000): server level, lockable (`security.accessTokens.*`).
- `AccessTokens` reads them through `SettingLayers.ServerLevel()`: the root container's `SecurityOptions` holds the
  configuration alone.
- Off: none made, those made refused (kept; they work again once back on).

## Gotchas

- **Known gap:** external player and Flash links made with a token still carry the account, not the token: they
  keep the account's rights and outlive a revoked token until they expire.
- `EverySettingOfTheClientsSettingsDialogExists` reads only `settings-schema.ts`, so it doesn't check the three
  token keys of `token-settings.ts`. `SETTINGS_RIGHTS` has no `accessTokens` entry (the sub-page names
  `server.settings` itself).
- Tests: helpers in `tests/Sfe.Tests/Infrastructure/TokenClients` (`Bearer` client without cookies, `CreateAsync`
  through the service).
- SQLite adds only nullable columns to existing tables; the tokens are a new table, fine on both providers.
