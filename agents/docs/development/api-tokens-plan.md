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

- [ ] Token scheme (`AuthenticationHandler`) and a policy scheme choosing it when `Authorization: Bearer` is sent;
  the principal carries the same claims as the cookie's plus the token id
- [ ] `CurrentAccess`: rights intersected with the token's, library from the token or `X-SFE-Tenant`
- [ ] `RequestGuard`: no `X-SFE-Request` needed with a bearer; Local mode and the setup and migration gates unchanged
- [ ] `.SessionOnly()` route metadata (tokens, password, sign-in directories, setup, installation, fast sign-in),
  refused 403 `session-only` with a token; a test like `EveryRouteNamesARight` asks every route to say which it is
- [ ] Failed tokens: throttle per address (`AddressKeys.Of`), `LogSecurityWarning` with name and address, never the
  token

### Phase 3: routes and rights

- [ ] Rights in `RightCatalog` with `since`, the same ids in `core/rights.ts`
- [ ] `api/v1/me/tokens` (list, create, revoke, revoke all), `api/v1/access/tokens` (list, revoke), all session-only
- [ ] The OpenAPI document declares the bearer scheme; Swagger UI's "Authorize" works
- [ ] The audit log names the token with the account on every change it makes ([logs](../features/logs.md))

### Phase 4: client

- [ ] My account › Access tokens: list, create (name, expiry, library, rights picked among one's own), the secret shown
  once with Copy, revoke; phone layout as a sheet ([frontend](frontend.md))
- [ ] Administration › People › Access tokens: every token with its account, last use, revoke
  ([admin-page](../features/admin-page.md), admin search entries)
- [ ] The `security.accessTokens.*` settings on the settings page (`settings-schema.ts`, a tab with its sub-page and
  `SETTINGS_RIGHTS` entry, `server.settings`)

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
