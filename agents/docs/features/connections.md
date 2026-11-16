# Connections (external storage)

Code: `src/Sfe.Core/Connections/` (`ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, `ConnectionRows`,
`ConnectionLevels`, `ConnectionSignInFlow`, `PendingSignIns`, `ProviderSecrets`),
`src/Sfe.Core/Access/ConnectionRights.cs`, `src/Sfe.Core/Settings/ConnectionSettings.cs`,
`src/Sfe.Core/Data/Entities/Access/Connection*.cs`,
`src/Sfe.Api/Security/ConnectionCredentials.cs`, `src/Sfe.Api/Endpoints/ConnectionEndpoints.cs`,
`ConnectionSignInEndpoints.cs`, `ConnectionManagers.cs`, `ConnectionProviderEndpoints.cs`; client `app/connections/`,
`library/connection-pages.ts`, `core/connection.models.ts`. Tests: `ConnectionServiceTests`, `ConnectionAccessTests`,
`ConnectionDataTests`, `ConnectionCredentialsTests`, `ConnectionSettingsTests`, `ConnectionSignInFlowTests`,
`ConnectionSignInRouteTests`, `ConnectionRouteTests`, `ConnectionProviderRouteTests`, `connections.spec.ts`, helpers in
`tests/Sfe.Tests/Infrastructure/Connections/` (`ConnectionWorld`, `FakeProvider`, `FakeProtector`, `RecordedDropbox`).
User docs: [docs/administration/connections.md](../../../docs/administration/connections.md).

Related notes: [connection-files](connection-files.md) (browse, read, change, upload, properties),
[transfers](transfers.md) (between the library and a connection), [connection-folder-rules](connection-folder-rules.md),
[connection-providers](connection-providers.md) (adding a kind), [dropbox](dropbox.md), the C# facade
([client-library](client-library.md#connections)). Still planned: index and search
([connections-index-plan](../development/connections-index-plan.md)). Security vectors:
[security](../development/security.md#connections).

## Model

- A `Connection` is a storage outside the libraries (Dropbox…) an account signed in to, browsed as a **location** of
  its own beside the library (never mounted in the library's tree), its paths relative to its `RootPath` in the remote
  storage. Routes: `api/connections/{id}/…`, mirroring the library's file routes.
- Level: **account** (`OwnerId`, private, in every library its owner works in), **library** (`TenantId`), **server**
  (`AllTenants` or `ConnectionTenant` rows). A check constraint ties the level to owner / library. `CreatedById` has
  no key: a library's connection outlives who made it.
- Who uses a library- or server-level one: `ConnectionGroup` rows (group, `FolderLevel` Read or Write). For a
  server-level one, each library's groups are its own rows; a library no longer offered loses them
  (`ConnectionSharing.SetTenantsAsync`) and its folder rules.
- `Disabled` keeps everything but nobody uses it; `ReadOnly` caps every level at Read; `State` = `NeedsReconnecting`
  when the provider refuses the sign-in.
- Rights (`ConnectionRights`, spread into `RightCatalog.All`, `since: 15`, Administrators only, never guests):
  `connections.use`, `connections.own`, `connections.library`, `connections.server`. Inside a connection the
  library's `files.*` rights apply, narrowed by the connection's level and its folder rules.
- **Who runs the server decides** ([security](../development/security.md)): settings `connections.enabled` /
  `providers` / `levels` (server level, right `server.settings`, lockable). `Enabled` defaults to true: nothing is
  offered until a kind has a sign-in method (Dropbox: an app key). `AllowPrivateAddresses` is configuration only.
- `api/v1/config` carries `ClientConfig.Connections` (`levels`, `kinds` with their sign-in fields and capabilities):
  null when off or without a `connections.*` right.

## Who may use one (`ConnectionAccess`)

The one place routes and jobs ask; `null` = the connection does not exist for that person (404). The route still asks
`connections.use` and the `files.*` right of what it does; where inside it:
[connection-folder-rules](connection-folder-rules.md).

- Never: connections off, kind not in `Connections:Providers`, level not in `Connections:Levels`, disabled, guests and
  visitors.
- Account level: the owner, Write. **Never an administrator**, even the server's: they list and remove through the
  management lists (`GetAtLevelAsync`), never open it.
- Library level: the library of the request only; the best level of the person's groups there.
- Server level: offered to the library of the request; then groups the same way.
- Server administrators: Write on library and server levels reachable in that library.
- `ConnectionAccess.Decide` is the pure rule (tested without a database). Routes build a `ConnectionSubject` from
  `AccessState` (tenant, user id, server administrator, guest = no user or a guest account).

## Credentials

- Stored only protected (`ICredentialProtector`; the Api's `ConnectionCredentials`, Data Protection purpose
  `SFE.Connections`, the keys folder of [sessions-and-pipeline](../architecture/sessions-and-pipeline.md)); never in
  `ConnectionInfo`, answers, logs or URLs.
- Text that does not unprotect (a backup restored without its keys folder) answers null: `OpenAsync` marks the
  connection `NeedsReconnecting` and answers a conflict (`NeedsReconnectingProblem`). Signing in again
  (`ReauthorizeAsync`) keeps id, groups and rules and says when the provider's account changed (`AccountId`).
- A refused refresh is `CredentialsEnded`: whoever uses the storage marks the connection
  (`MarkNeedsReconnectingAsync`; the file routes and transfer jobs do).

## Signing in to make one

- `POST api/connections/authorize` (`ConnectRequest`: kind, level, name, method, root, fields) → `SignInBegun`: the
  provider's address and a state, or the connection when nothing more is needed (password kinds). The draft is
  checked first (`ConnectionService.CheckAsync`), so nobody is sent to the provider for a connection that can't be
  made. Account level: the caller owns it; library level: the request's library. The root folder is typed.
- `PendingSignIns` keeps the provider's `Pending` (PKCE verifier) server side, by a random 32-byte state: used once by
  any attempt (another account's try spends it), 10 minutes, at most 1000 waiting; a restart forgets them.
- `GET api/connections/oauth/callback` (needs the session, 401 signed out; not in `ApiRoutes.IsServerEntry`) changes
  state on a GET: the session cookie is `SameSite=Lax`, sent on the top-level navigation back, so the state is the
  CSRF defence. It redirects to `{PathBase}/connections?made={id}` or `?error=signed-out|refused|expired|failed`,
  never to an address from the query. `POST …/authorize/complete` ends the code flow.
- Rights: the routes need one of `connections.own`, `connections.library`, `connections.server`; then
  `ConnectionSignInEndpoints.MayManage` over the draft (account: its owner with `own`, never an administrator for
  someone else; library: the request's library with `library`; server: `server`), asked again at completion.
- Signing in again: `POST api/connections/{id}/reauthorize` (`SignInAgain`), same flow with
  `PendingSignIn.ConnectionId`; completion calls `ReauthorizeAsync` (same id, groups, rules) and the callback lands on
  `connections?reconnected={id}`, with `&otherAccount=true` when the provider's account changed. Not found for a
  connection the session may not manage.
- The sign-in steps answer a `next` step, so a later second step (SFTP's host key to accept) fits without new routes.

## Listing and managing (`ConnectionEndpoints`)

- `GET api/connections`: `usable` (`ConnectionAccess.UsableAsync` for the session) and `managed`: its own (with
  `connections.own`), its library's and the server's offered there (`connections.library`), the server's
  (`connections.server`), every account's for server administrators; each with `manage` and `remove` flags.
- `ConnectionManagers` is the one rule (also used by the sign-in routes): manage = make, sign in again, rename,
  switch; remove = manage, or a server administrator for an account's connection. Anything else answers 404.
- `PATCH` / `DELETE api/connections/{id}`; `PUT …/libraries` needs `connections.server`; `PUT …/groups` needs
  `connections.library` and acts on the request's library only (`ConnectionSharing` answers 404 when the connection
  isn't that library's or offered to it). A library's administrator gives a server connection to groups but can't
  rename or remove it.
- The owner's account removed: its connections go with it (database cascade on `OwnerId`).

## The client

- `ConnectionsSectionComponent`: tab `mine` (My account › Connections: one's own), `shared` (Administration ›
  Connections: the rest of `managed`), `setup` (`ProviderSetupComponent` for `dropbox`). What a row offers follows the
  server's `manage` / `remove` flags; groups and libraries come in the same `GET api/connections`. A row keeps two
  buttons, the rest in a panel one per line. The add dialog is a `sfe-sheet` (a sheet on phones).
- `ConnectDialog` builds its form from `config.connections.kinds[].signIn` (no code per provider). Redirect flow:
  `window.location.assign` to the address the server gave; code flow: the address in a new tab, then the code. The
  client bundle names no provider address (fully local): the browser only navigates to what the server sends.
- `connectionsLanding` (route `connections`): the callback's `?made=` / `?reconnected=` / `?error=` become a toast,
  then the connection's page (My account for one's own).
- Sidebar: `ConnectionLinksComponent` lists the usable ones under the quick links; they take drops onto their root.
- The place: `LibraryRoute.c` (`?c=7&path=…`) lists a connection's folder through `connectionPage`
  (`library/connection-pages.ts`), which sets `ConfigService.place`. `config` is then `atPlace(config, place)`: file
  rights are those the connection's `access` and capabilities give back; default tags, index, data server,
  external player, trickplay, covers and animated hover are gone (streaming and thumbnails stay:
  [connection-previews](connection-previews.md)), so every component already hiding what a session
  may not do hides them (no code per component). `urls` give the connection's addresses
  (`api/connections/{id}/files/raw/…`); player and view pages carry `c` too (`enterPlace`).
- Leaving the place: a search (`navigate` with `q` drops `c`), quick links (`libraryParams`, `config.libraryUrls`),
  the SFE link, `reveal`; the library page clears the place when destroyed (admin pages show library files). Adding
  quick links is hidden there (`QuickLinksService.addable`: links are library paths).
- `setPlaceFormatsOnly` (formats.ts): every format but EPUB, Flash and archives (they read files through library
  routes); text formats read only through `FILE_SOURCE` ([connection-previews](connection-previews.md)).
- Requests are `Api` methods (`connections`, `authorizeConnection`, …); specs mock them with `mockApi()`. `Api`
  answers a 409 with its problem body instead of throwing: `problemOf` tells it apart.

## Audit lines

- Security warnings: a connection made, signed in again to another account, offered to more libraries, a group added
  or raised to Write, an app secret set. Information: other changes, removal, a secret removed. Each names the
  connection by `ConnectionRows.Describe` (id, name, kind, level), never a token, a code or the provider's account.

## Open decisions (defaults taken)

Taken while building, without the owner's answer; each can change (those marked confirmed were answered by the
owner):

- Transfers: `TransferJobs` per library, sharing the library's `JobBoard` ids, routes and operations panel.
- Transfers: failed items kept 24 h in memory behind `POST api/files/jobs/{id}/retry` (as built: a new job, a broken
  file written again from its start, no resume point).
- Transfers: `Files:Uploads:MaxFileSizeMegabytes` holds for files going into a connection.
- Transfers: content hashes verified for copies too, not only moves.
- Transfers (confirmed 2026-11-14): a library source of a move goes to SFE's trash (chosen for safety, against the
  study's "deleted for good").
- Transfers: a library folder with folder rules may be moved out to a connection (the default said "with a warning";
  as built it goes to the trash like a delete, no warning of its own).
- Transfers (confirmed 2026-11-14): connection to connection allowed, the bytes going through the server.
- Transfers: an Information log line per item and one `BackgroundTaskKind.Transfer` run per job.
- Client: the new requests stay `Api` methods.
- Folder rules: the `ConnectionGroup` row stays the gate (a rule never opens a connection to a group without one).
- Folder rules (confirmed 2026-11-14): a rule may raise a group above its row's level (nearest rule wins, `ReadOnly`
  still caps).
- Folder rules: a library no longer offered a server connection loses its rules.
- Folder rules: a move's keep-access question covers the request's library; other libraries' rules follow with a
  security warning.
- Folder rules: rules of a deleted folder are kept (shown as not found, to re-point).
- Folder rules: a folder renamed outside SFE found by its provider id (`ItemId`), with a manual Follow (not built: the
  rule is re-pointed by hand; automatic with [connections-index-plan](../development/connections-index-plan.md)).

## Gotchas

- **No database cascade from `Connections` to `ConnectionTenants` / `ConnectionGroups`** (two cascade paths on SQL
  Server): whoever deletes a connection loads it with `WithUses()` (`Include` of both, EF `ClientCascade`); a bare
  delete throws (`ConnectionDataTests.AConnectionLoadedWithItsRowsIsRemovedWithThem`). `AccessService` needs no
  change for users, groups or libraries removed.
- Changing a library's group rows: keep the rows that stay and change their level in place; removing and adding a row
  with the same key in one context is refused by EF.
- Name clashes in the Api: `ConnectionInfo` (ASP.NET has one: alias it with `using ConnectionInfo =
  Otools.Net.Sfe.Core.Connections.ConnectionInfo;`), and `SignInRequest` is the account sign-in's (ours is
  `ConnectRequest`).
- Tests reading the API's answers need `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` (levels, sign-in
  kinds and field types are strings).
- `ConnectionService` and `ConnectionAccess` read the settings through `SettingLayers.ServerLevel()` (server level
  only); a test container needs `IConfiguration` registered (`ConnectionWorld`).
- `RightCatalog.All` spreads `ConnectionRights.All` while it initializes: `ConnectionRights` must never call back
  into `RightCatalog` (a null list otherwise).
- `RightCatalog.cs`, `SettingsService.cs`, `AccessService.cs` are over the readability limits: new connection code
  goes in new files, and the few lines that had to go in were recorded with `measure.mjs --write-baseline`.
- A test server knows the fake kind with `services.AddSingleton<IConnectionProvider>(new FakeProvider())`
  (`ConnectionSettingsTests.Factory`); `ConnectionWorld` gives two libraries, alice / bob / carol and a server
  administrator (`WithSettings`).
- Servers without internet: the connection's routes answer `connection-unavailable` (502) within the outbound
  timeouts ([connection-providers](connection-providers.md)); nothing else depends on the provider.
