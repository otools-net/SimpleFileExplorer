# Plan: personal access tokens

Files: `src/Sfe.Core/Access/` (new `AccessTokens`), `Data/AccessDbContext.cs`, `Data/AccessMigrations/`,
`AccessDatabaseInitializer`, `Access/RightCatalog.cs`; `src/Sfe.Api/Security/` (new token scheme, `CurrentAccess`,
`RequestGuard`, `SfeSession`, `SecurityServiceCollectionExtensions`), new `Endpoints/TokenEndpoints.cs`; client
`core/rights.ts`, `app/account/`, `app/access/`. Part 2 of the [epic](external-access-epic-plan.md); needs
plan 1 (the API under `/api/v1`, done: [api-versioning](api-versioning.md)). Delete this plan once its last item is ticked.

## Starting point (2026-11-13)

- One authentication scheme: the cookie (`SfeSession`, validated against the account's security stamp).
- `RequestGuard` refuses non-GET `/api` requests without `X-SFE-Request` (the CSRF defence of the cookie).
- `CurrentAccess` works out account, library and rights once per request; the library comes from the cookie.
- Signed links (`FileLinks`…) use Data Protection; nothing is stored per link.

**Done when:** an account with the right makes a token on its account page, a program calls any v1 route with
`Authorization: Bearer <token>` and gets what the account may do (or less, as the token says), administrators see and
revoke tokens, and the attack vectors below are tested.

## Decisions

- A token **acts as its account**: rights = the account's now ∩ the token's list (empty list = all of the account's).
- A token may be limited to one library; without that, the library is named by `X-SFE-Tenant` among those the account
  reaches, else the account's first.
- Stored: SHA-256 of 32 random bytes; shown once at creation as `sfe_<id>_<secret>` (the id finds the row, the hash
  is compared in constant time); the list shows name, first characters, dates.
- A bearer request ignores cookies and doesn't need `X-SFE-Request` (no ambient credential to abuse).
- A disabled or deleted account ends its tokens; a password change does not (as other services do; said in the
  docs), "Revoke all" does.
- Local mode: the session's checks apply (this computer, administrators only).
- New rights, Administrators only, never guests: `account.tokens` (own tokens), `access.tokens` (see and revoke
  anyone's in the libraries one administers; server administrators all).
- Room left for OAuth later: the row has a nullable client id and a kind; nothing else is built.

## Progress

### Phase 1: model and service

- [x] `AccessToken` entity (user, name, hash, shown prefix, tenant, rights, kind, client id, created, expires,
  last used at and from, revoked at and by); SQL Server migration in `AccessMigrations` and the SQLite path
- [x] `AccessTokens` service: create (returns the secret once), list, revoke, revoke all, find by presented token
  (cached until `AccessChanges.Version` moves), last use written at most every 5 minutes per token
- [x] Settings `Security:AccessTokens:Enabled`, `MaxLifetimeDays`, `MaxPerAccount`
  ([settings-new-key](../features/settings-new-key.md)); server level, lockable

As built (2026-11-14):

- Table `AccessTokens` (migration `20261114172624_AccessTokens`, applied on LocalDB; SQLite from the model):
  `Lookup` varchar(16) unique (the row is found by it), `Hash` varbinary(32), `Prefix` (shown), `Rights` (ids
  separated by spaces, null = all of the account's), `Kind` (`Personal`), `ClientId`, `LastUsed`/`LastUsedFrom`,
  `Revoked`/`RevokedById` (no foreign key: a second path from `Users`). Cascades from `Users` and from `Tenants` (a
  token limited to a deleted library goes with it, never becomes unlimited). `AccessTokenDataTests`.
- Token `sfe_<16 hex lookup>_<43 base64url>` (`AccessTokenSecret`): 32 random bytes, SHA-256 stored, compared with
  `FixedTimeEquals` (an unknown lookup is compared with a zero hash: same work). Not the row id (it would tell how
  many tokens exist). Revoking keeps the row (`Revoked`): lists show it.
- `AccessTokens` (`Core/Access/`): `CreateAsync(userId, AccessTokenDraft)` (name, expiry within `MaxLifetimeDays`,
  default the longest; a library the account reaches; rights known and held there, or in any library it reaches;
  `MaxPerAccount` counts tokens neither revoked nor expired), `ListAsync(userId)`, `ListAllAsync(tenantIds?)`
  (accounts belonging to those libraries; null all), `RevokeAsync(id, ownerId?, revokedById)`, `RevokeAllAsync`,
  `FindAsync(presented, address)`. Settings read through `SettingLayers.ServerLevel()` (the root container's
  `SecurityOptions` is the configuration's alone). Revoking moves `AccessChanges` (rows cached before are read
  again); unknown lookups are never cached. `AccessTokenServiceTests`, `AccessTokenSettingsTests`.
- Not yet: the account's state (disabled) and its current rights are checked at use by phase 2; the settings have no
  field on the settings page yet (phase 4, with the admin page: `settings-schema.ts` and `admin-sections.ts` are at
  their readability baseline). Revoked and expired rows are never purged (a later item if lists grow).

### Phase 2: authentication

- [x] Token scheme (`AuthenticationHandler`) and a policy scheme choosing it when `Authorization: Bearer` is sent;
  the principal carries the same claims as the cookie's plus the token id
- [x] `CurrentAccess`: rights intersected with the token's, library from the token or `X-SFE-Tenant`
- [x] `RequestGuard`: no `X-SFE-Request` needed with a bearer; Local mode and the setup and migration gates unchanged
- [x] `.SessionOnly()` route metadata (tokens, password, sign-in directories, setup, installation, fast sign-in),
  refused 403 `session-only` with a token; a test like `EveryRouteNamesARight` asks every route to say which it is
- [x] Failed tokens: throttle per address (`AddressKeys.Of`), `LogSecurityWarning` with name and address, never the
  token

As built (2026-11-14):

- `Security/AccessTokenAuthentication` (scheme `AccessToken`) behind the default policy scheme `Sfe`, whose selector
  takes it whenever `BearerTokens.IsBearer` (the header starts with `Bearer `), else the cookie: a bearer request never
  reads the cookie, and its 401 (`WWW-Authenticate: Bearer error="invalid_token"`) touches no cookie. The handler
  fails while `MigrationCoordinator.IsBlocking` (no table yet) and when tokens are off; reads the account through
  `GetSessionUserAsync` at each request (disabled or deleted: refused; a Local server: administrators only); puts the
  `PresentedAccessToken` in the request's features (`BearerTokens.TokenOf`). Claims: the cookie's (id, name, stamp,
  the token's library as `sfe:tenant`) plus `sfe:token`.
- A password change does **not** end tokens (confirmed by the owner 2026-11-14; the stamp isn't compared), nor does a
  lockout after wrong passwords (it would let a stranger stop an account's programs); a disabled or deleted account
  does.
- `CurrentAccess`: with a token, the library is the token's, else `X-SFE-Tenant` among those the account reaches,
  else the session rule (first reached, else the default); a library the token isn't for or the account doesn't
  reach gives no library and no rights (403; `TenantMiddleware` answers 403 instead of 409 `tenant-changed` to a
  bearer). Rights = the account's now ∩ the token's list.
- `RequestGuard`: `X-SFE-Request` not asked of a bearer; after the migration, setup and Local gates, a bearer whose
  token wasn't accepted gets 401 on any route (never a visitor's rights instead), and one on a route carrying
  `SessionOnlyMetadata` (`.SessionOnly()`, `SessionOnlyRoutes`) 403 `session-only` (new problem word). Session-only:
  the groups `session` (sign-in, password, fast sign-in, recovery, space switch), `setup`, `installation`,
  `migration`, `me/tokens`, `access/tokens`; sign-in directories have no route (configuration only).
  `AccessTokenRouteTests.EveryRouteLettingSomeoneInOrHandlingTokensIsForSessionsOnly` checks the list both ways.
- Throttle: `SignInThrottle` under a reserved name (`AccessTokenAuthentication.ThrottleName`) per `AddressKeys.Of`:
  `MaxFailedSignIns` wrong tokens lock the address (IPv6: its /64) out of every token for `LockoutMinutes`. The log
  names a wrong token by `sfe_<lookup>` only (no secret character), with the address.
- Jobs started with a token (`JobToken`, from `CurrentAccess.JobToken()`): `FileJobAccess` (move, copy, compress,
  extract, transfers' library side) and `ConnectionJobAccess` (`ConnectionJobSubject.Token`) check each item with the
  token's rights over the account's, and nothing once the token is revoked or expired (`AccessTokens.IsActiveAsync`,
  cached until `AccessChanges` moves); `FileJobs` and `TransferJobs` start each item with `JobRun.BeginAsync`, which
  asks `IFileJobAccess.StoppedAsync` and reports the rest Forbidden with `JobToken.StoppedProblem`
  (`AccessTokenJobTests`). Uploads check each part
  in its own request. External player and flash links made with a token still carry the account, not the token.

### Phase 3: routes and rights

- [x] Rights in `RightCatalog` with `since`, the same ids in `core/rights.ts`
- [x] `api/v1/me/tokens` (list, create, revoke, revoke all), `api/v1/access/tokens` (list, revoke), all session-only
- [x] The OpenAPI document declares the bearer scheme; Swagger UI's "Authorize" works
- [x] The audit log names the token with the account on every change it makes ([logs](../features/logs.md))

As built (2026-11-14):

- Rights `account.tokens` and `access.tokens` (`AccessTokenRights`, a part of `RightCatalog.All` like
  `ConnectionRights`): tenant rights, Write, Administrators, never guests, `since` 16.
- `TokenEndpoints`: `GET me/tokens` → `AccessTokenInfo[]`; `POST me/tokens` (body `AccessTokenDraft`: `name`,
  `expires?`, `tenantId?`, `rights?`) → 201 `{ token, secret }`, 400 validation, 403 tokens off, 409 too many;
  `DELETE me/tokens/{id}` → 204 / 404 (another's); `POST me/tokens/revoke-all` → `{ revoked }`; `GET access/tokens`
  and `DELETE access/tokens/{id}` (`TokenAdministration`): server administrators every token, others those of the
  accounts of the spaces where they hold `access.tokens`, never a server administrator's (hidden, 404).
- `ApiDocument`: security scheme `accessToken` (http bearer) asked by every operation, with an empty requirement
  beside it (the cookie); session-only operations carry `x-sfe-session-only: true` and ask for no scheme, so that
  Swagger UI calls them with the cookie alone. Baseline written.
- `AccessTokenAudit` (after authentication): every bearer request but GET/HEAD/OPTIONS is logged (information,
  `Sfe.Api.Security.AccessTokenAudit`) as "POST /api/v1/tags answered 201 to admin through access token 5 “name”";
  `ProblemLog` names the token too (`BearerTokens.Describe`).
- `GET config` has `accessTokens` (`{ enabled, maxLifetimeDays }`; null for a session holding neither token right).
- Tests: `AccessTokenAuthenticationTests` (attack vectors), `AccessTokenRouteTests` (routes, rights, session-only
  list, OpenAPI, config); helpers `Infrastructure/TokenClients`.

### Phase 4: client

- [x] My account › Access tokens: list, create (name, expiry, library, rights picked among one's own), the secret shown
  once with Copy, revoke; phone layout as a sheet ([frontend](frontend.md))
- [x] Administration › People › Access tokens: every token with its account, last use, revoke
  ([admin-page](../features/admin-page.md), admin search entries)
- [x] The `security.accessTokens.*` settings on the settings page (`settings-schema.ts`, a tab with its sub-page and
  `SETTINGS_RIGHTS` entry, `server.settings`)

As built (2026-11-14):

- Client code in `src/app/tokens/`: `MyTokensComponent` (My account › Access tokens, shown by `AccountSection` for tab
  `tokens`), `AllTokensComponent` (Spaces & accounts › Access tokens, shown by `AccessSection`), `TokenRowComponent`
  (`li[sfeTokenRow]`, one row of both lists), `TokenCreateComponent` (the sheet: form, then the token once with Copy;
  the token lives only in its signal, cleared when the sheet closes and on destroy), `token-parts.ts` (state, dates,
  lives offered, right areas, `tokensConfig`). Models `core/access-token.models.ts`; styles `styles/access-tokens.css`.
- `api/config` sends `accessTokens` as a boolean (`ConfigContracts.cs`); `tokensConfig` also takes
  `{ enabled, maxLifetimeDays }`, absent = off. Without the longest life the sheet offers 7/30/90 days, 1 year and
  "The longest allowed" (null: the server's default); the server refuses a longer one (400 under `expires`).
- Rights to pick come from `config.rights` (the current library), titled and grouped by the catalog
  (`api/access/rights`) when the session may read it, else by id; the server checks them for the library chosen.
- Sub-pages are declared in `tokens/token-pages.ts` (`admin-sections.ts` is at its readability baseline): My account ›
  Access tokens (`account.tokens`), Spaces & accounts › Access tokens (`access.tokens`) and › Token settings
  (`server.settings`, tab `accessTokens`). That tab lives in `tokens/token-settings.ts`, spread into `TABS`
  (`settings-schema.ts` at its baseline): the backend test `EverySettingOfTheClientsSettingsDialogExists` reads only
  settings-schema.ts, so it doesn't check these three keys. `SETTINGS_RIGHTS` has no `accessTokens` entry (the
  sub-page names `server.settings` itself).
- While tokens are off, My account › Access tokens says so and offers no New token (the list and Revoke stay).
- Specs: `tokens/my-tokens.spec.ts`, `tokens/all-tokens.spec.ts`; builder `accessToken()` in `testing/fakes.ts`.
- User docs: `docs/administration/my-account.md#access-tokens`, `spaces-and-accounts.md#access-tokens`, the README
  table, a link from `user-guide/signing-in.md`; screenshots come with the release check (phase 5).

### Phase 5: docs and release

- [ ] `docs/user-guide/` (account page), `docs/administration/`, `docs/reference/api.md` (calling with a token, curl
  and PowerShell examples), rights reference, `docs/configuration.md`, screenshots
- [ ] [security](security.md): the vectors below added; new feature note `agents/docs/features/access-tokens.md`
- [ ] This plan deleted

## Attack vectors to test

- A token of a disabled or deleted account, an expired one, a revoked one, one whose rights the account lost since.
- A token limited to a library with another library's `X-SFE-Tenant`; a token of an account removed from the library.
- A token on a session-only route; a token making tokens; a token changing its account's password.
- Guessing: many wrong tokens from one address, from one IPv6 /64, a right id with a wrong secret (same answer and
  timing as an unknown id).
- Local mode: a token from another computer, a non-administrator's token.
- A request with both a cookie and a bearer (the bearer decides); a bearer on `<img>`-style GET routes.
- Secrets: the token in logs, problem answers, the audit log, backups (only the hash), URLs (never accepted in the
  query).
- A space administrator revoking a server administrator's token (refused, as for accounts).

## Traps

- `OnValidatePrincipal` of the cookie doesn't run for the token scheme: the handler checks the stamp-equivalent
  (disabled, kind, administrator flag) itself through `AccessService.GetSessionUserAsync`.
- A 401 from a bearer request must not carry `Set-Cookie` or open the sign-in page logic; the client is unaffected.
- `RequestLimits` partitions per account: a token counts as its account (no extra budget by making tokens).
- File jobs started with a token carry the `AccessSubject` **and the token's limits** (`FileJobAccess` re-checks per
  item): a revoked token stops its running jobs' next item.
- SQLite adds only nullable columns to existing tables; this plan adds a new table (fine on both).
