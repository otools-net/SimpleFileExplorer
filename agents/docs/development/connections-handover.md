# Handover: external connections (Dropbox first)

Files: [connections-plan](connections-plan.md) (the checklist: its `## Progress` is the source of truth),
[external-access-epic-plan](external-access-epic-plan.md) (status board), `src/Sfe.Core/Connections/`,
`src/Sfe.Core/Access/ConnectionRights.cs`, `src/Sfe.Core/Settings/ConnectionSettings.cs`,
`src/Sfe.Core/Data/Entities/Access/Connection*.cs`, `tests/Sfe.Tests/Infrastructure/Connections/`. Written
2026-11-13 at the end of the session that started the plan; delete it with the plan.

## Where it stands

The owner asked to start with plan 5 (connections) and do plans 1 to 4 later. Asked for this session: phases 0 to 2,
one commit per plan item, no real Dropbox account at hand. Done: phase 0 and phase 1 (the last item in a third
session, see the epic's session log).
**Nothing of Dropbox itself is built yet**: no package reference, no route, no page but the settings tab.

| Commit | What |
|---|---|
| `ebdb377` | api-v1 phase 1 (`ApiRoutes`), left uncommitted by the session before; the epic's plans |
| `043c50c` | phase 0: results written in the plan (SDK, rule table, listing page, capabilities) |
| `99d14d3` | readability baseline: `AccessEndpoints.cs` 442 to 448 lines (from `ebdb377`) |
| `ff601f1` | phase 1 item 1: interfaces, `RemotePaths`, `OutboundPolicy`, `FakeProvider` + contract tests |
| `651f589` | phase 1 item 2: tables, migration `20261113…_Connections`, upgrade and cascade tests |
| `cb5aebd` | phase 1 item 3: rights, settings, `ClientConfig.Connections`, settings tab, docs, changelog |
| last | phase 1 item 4: `ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, `ConnectionCredentials`, feature note |

Checks run for the last commit: the new suites, `npm test` (1179 passed), the full `dotnet test`, `measure.mjs
--check`, `check-links.mjs`. The migration was applied on LocalDB and its deletes checked there by hand.

## Resume here

1. Phase 2 (four items): Dropbox settings (**ask the owner about the app secret first**, see Decisions), the
   authorize routes, refresh and reconnect, the client pages.
2. Then phases 3 to 7 as the plan lists them.

Per commit, as the epic asks: tick the item in the plan with an "As built" sentence, update the status board and
session log of the epic plan, a line under `## Unreleased` when something is visible, `dotnet csharpier format`,
`node agents/tools/readability/measure.mjs --check`, the tests.

## What exists (read these before writing more)

- `IConnectionProvider`: kind, name, icon, `ProviderCapabilities`, `SignInMethods(options)` (empty = not configured,
  so not offered), sign-in in two calls (`BeginSignInAsync` gives a grant, or an address plus a `Pending` secret
  the **server** keeps; `CompleteSignInAsync` takes it back with the person's fields), `Open(credentials, rootPath,
  options)`. Providers get `ConnectionOptions` as an argument, never from DI.
- `IRemoteStorage`: path-addressed (relative to the connection's root folder), one `RemoteStorageException` with a
  `RemoteErrorKind`. `FakeStorageTests` is the behaviour a real provider must show.
- `RemotePaths.Normalize` (null = refused) for everything a route receives; `IsValidName` for new names;
  `ComparisonOf(capabilities)` for case.
- `OutboundPolicy.CreateClient(new OutboundRule(hosts))`: the only maker of a provider's `HttpClient` (https, hosts
  of the rule, no redirect followed, 10 s to connect, 60 s to start answering). Tests replace the transport:
  `new OutboundPolicy(() => handler)`.
- `ConnectionProviders` (registered in `AddSfeAccess`, built from every `IConnectionProvider` registered):
  `Find(kind)`, `Offered(options)`, `IsAllowed(kind, options)`. No provider is registered in the app yet.
- Entities `Connection` (level, `OwnerId` or `TenantId`, `AllTenants`, `RootPath`, `Credentials`, `Account`,
  `AccountId`, `State`, `Disabled`, `ReadOnly`), `ConnectionTenant`, `ConnectionGroup` (`FolderLevel` Read / Write).
- Rights `connections.use` / `own` / `library` (space, Administrators, `since: 15`) and `connections.server`.
  Settings `connections.enabled` / `providers` / `levels` (server level, right `server.settings`).
- `ICredentialProtector`, implemented by `Sfe.Api/Security/ConnectionCredentials` (registered in `AddSfeSecurity`).
- `ConnectionService` (lists, create from a grant, update, reauthorize, mark needs reconnecting, remove, `OpenAsync`),
  `ConnectionSharing` (libraries, groups), `ConnectionAccess` (`LevelAsync`, `UsableAsync`, pure `Decide`): rules in
  [connections](../features/connections.md). Routes build a `ConnectionSubject` from `AccessState` (tenant, user id,
  server administrator, guest = no user or a guest account).
- Test helper `ConnectionWorld` (two libraries, alice / bob / carol / a server administrator, `WithSettings`).
- Tests: `FakeProvider` (accounts by name; code `good:<name>` or password `secret` signs in), `FakeAccount`
  (`WithFile`, `Failing`, `Calls`, `Trash`), `FakeStorage`. A test server knows the fake kind with
  `services.AddSingleton<IConnectionProvider>(new FakeProvider())` (`ConnectionSettingsTests.Factory`).

## Decisions taken in this session (beyond the plan's)

- **Dropbox client: the official `Dropbox.Api` 7.4.0** (details and the request shapes seen: plan, phase 0).
- **The app secret is optional**: PKCE works for both flows without it. Proposed, not yet confirmed by the owner:
  only `connections.dropbox.appKey` is a setting of the page (an app key is public, it is in the authorize
  address); a secret, when wanted, is `Sfe:Connections:Dropbox:AppSecret` in the configuration only. This replaces
  the plan's "write-only AppSecret stored with `SecretFiles`": **ask the owner** before building phase 2 item 1.
- `Connections:Enabled` defaults to **true**; nothing can be connected until a kind has a sign-in method.
- Folder rules of connections: a twin table (phase 6), not a column of `FolderRules`.
- Ranged reads at Dropbox go through `GetTemporaryLinkAsync` + a `Range` request (the SDK's download has none):
  the rule's hosts must include `*.dl.dropboxusercontent.com`.
- Database: no cascade from `Connections` to `ConnectionTenants` / `ConnectionGroups` (two cascade paths on SQL
  Server). **Whoever deletes a connection loads it with `Include(c => c.Tenants).Include(c => c.Groups)`**; a bare
  delete throws (`ConnectionDataTests.AConnectionLoadedWithItsRowsIsRemovedWithThem`). `AccessService` needs no
  change for users, groups or libraries removed.

## Design worked out for what comes next (not built)

Phase 1 item 4 is built (see What exists).

**Phase 2**

- `src/Sfe.Core/Connections/Dropbox/DropboxProvider.cs` (+ `DropboxStorage`, error mapping): add the package to
  `Sfe.Core.csproj`; register `IConnectionProvider` in `AddSfeAccess`. Scopes: `account_info.read`,
  `files.metadata.read`, `files.content.read`, `files.content.write`. `MaxRetriesOnError = 0`.
- Error mapping seen in the spike: `ApiException<T>` path not found = `NotFound`; `RateLimitException` =
  `RateLimited` with `RetryAfter` seconds; `AuthException` and `OAuth2Exception` = `CredentialsEnded`;
  `RetryException` and `HttpRequestException` = `Unavailable`; `BadInputException` = `Refused`.
- Routes (new file `ConnectionEndpoints.cs`, mapped under `ApiRoutes.Base`, each with its right):
  `POST connections/authorize` (kind, level, name, method, fields; the state and the provider's `Pending` kept
  server side for 10 minutes, bound to the account and the level; answers `{ address, state, fields }`),
  `GET connections/oauth/callback` (session must be the state's account; a state is used once; answers a redirect
  into the client), `POST connections/authorize/complete` (code flow), then list / rename / groups / remove.
  The return address comes from the request (forwarded headers, virtual directory) and is shown to the
  administrator to register at Dropbox; add `connections/oauth/callback` nowhere in `ApiRoutes.IsServerEntry`
  (it needs the session).
- Tests never call Dropbox: recorded answers through `new OutboundPolicy(() => handler)`; the JSON bodies used in
  the spike are in the plan's phase 0 text (list, token, errors). The client must name no provider address
  (`ApiTests.WebClientHasNoExternalResources`): the authorize address comes from the server and the browser only
  navigates to it.
- A manual check list with a real account is still owed before a release (plan, "Traps"): write it as
  `agents/tools/dropbox-check/README.md` when the provider exists.

## Traps met

- **The readability ratchet and files already over a limit.** `RightCatalog.cs`, `SettingsService.cs` and
  `AccessService.cs` are over 400 lines and in `baseline.json`: any line added fails `measure.mjs --check`. What
  was done: new code in new files (`ConnectionRights`, `ConnectionSettings`), the few lines that had to go in
  (`RightCatalog` +1, `SettingsService` +3, and the client's data files `settings-schema.ts` +38,
  `admin-sections.ts` +8) recorded with `--write-baseline` in the same commit. Keep doing that,
  and say so in the commit; do not grow those files with logic.
- `RightCatalog.All` spreads `ConnectionRights.All` while it initializes: `ConnectionRights` must never call back
  into `RightCatalog` (a null list otherwise).
- After a client change the next `dotnet build` / `dotnet test` fails once ("No file exists for the asset …
  chunk-….js"): run it again ([build](../environment/build.md)).
- Git Bash heredocs break on a `'` and swallow `\\` and `\n` ([line-endings](../environment/line-endings.md)):
  write files with the Write tool, edit with Edit.
- `dotnet ef migrations add` writes CRLF: `sed -i 's/\r$//'` on the two new files and the snapshot.
- The test helpers are in namespace `Sfe.Tests.Infrastructure`.
- An upgrade test must set an installed version **older** than `Directory.Build.props` (`1.85.0` now), else "No
  update is due".
- No `sqlcmd` on this machine: SQL on LocalDB runs from PowerShell with `System.Data.SqlClient`; create a scratch
  database with `dotnet ef database update --context AccessDbContext --project src/Sfe.Core --connection "…"`,
  drop it afterwards.

## Left open

- User docs with screenshots for Administration › Connections: only `docs/configuration.md` and
  `docs/reference/rights.md` have text; a page under `docs/administration/` with its screenshot comes with the
  phase 2 pages (the tab alone shows three fields).
- `docs/upgrading.md`: nothing written (three empty tables, three rights for Administrators); add a line when the
  feature is released if the owner wants it noticed.
- The AGENTS.md index says the connections plan is "plan 5"; its own header says "Part 5": consistent, but the
  plan's first line still says it needs api-v1 finished. It does not in practice: routes map through `ApiRoutes`.
