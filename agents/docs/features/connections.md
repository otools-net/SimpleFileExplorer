# Connections (external storage)

Code: `src/Sfe.Core/Connections/` (`ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, `ConnectionRows`,
`ConnectionLevels`, `ProviderSecrets`, `Dropbox/`), `src/Sfe.Core/Data/Entities/Access/Connection*.cs`,
`src/Sfe.Api/Security/ConnectionCredentials.cs`, `src/Sfe.Api/Endpoints/ConnectionProviderEndpoints.cs`; tests
`ConnectionServiceTests`, `ConnectionAccessTests`, `ConnectionDataTests`, `ConnectionCredentialsTests`,
`DropboxProviderTests`, `ConnectionProviderRouteTests` with `tests/Sfe.Tests/Infrastructure/Connections/`
(`ConnectionWorld`, `FakeProvider`, `FakeProtector`, `RecordedDropbox`). Work still planned:
[connections-plan](../development/connections-plan.md).

## Model

- A `Connection` is a storage outside the libraries (Dropbox…) an account signed in to, browsed as a location of its
  own, its paths relative to its `RootPath` in the remote storage.
- Level: **account** (`OwnerId`, private, in every library its owner works in), **library** (`TenantId`), **server**
  (`AllTenants` or `ConnectionTenant` rows). A check constraint ties the level to owner / library.
- Who uses a library- or server-level one: `ConnectionGroup` rows (group, Read or Write). For a server-level one, each
  library's groups are its own rows; a library no longer offered loses them (`ConnectionSharing.SetTenantsAsync`).
- `Disabled` keeps everything but nobody uses it; `ReadOnly` caps every level at Read.

## Who may use one (`ConnectionAccess`)

The one place routes and jobs ask; `null` = the connection does not exist for that person (404). The route still asks
`connections.use` and the `files.*` right of what it does.

- Never: connections off, kind not in `Connections:Providers`, level not in `Connections:Levels`, disabled, guests and
  visitors.
- Account level: the owner, Write. **Never an administrator**, even the server's: they list and remove through the
  management lists (`GetAtLevelAsync`), never open it.
- Library level: the library of the request only; the best level of the person's groups there.
- Server level: offered to the library of the request; then groups the same way.
- Server administrators: Write on library and server levels reachable in that library.
- `ConnectionAccess.Decide` is the pure rule (tested without a database).

## Credentials

- Stored only protected (`ICredentialProtector`; the Api's `ConnectionCredentials`, Data Protection purpose
  `SFE.Connections`, the keys folder of [sessions-and-pipeline](../architecture/sessions-and-pipeline.md)); never in
  `ConnectionInfo`, answers or logs.
- Text that does not unprotect (a backup restored without its keys folder) answers null: `OpenAsync` marks the
  connection `NeedsReconnecting` and answers a conflict (`NeedsReconnectingProblem`). Signing in again
  (`ReauthorizeAsync`) keeps id, groups and rules and says when the provider's account changed (`AccountId`).

## Setting up a kind (Dropbox)

- App key: server setting `connections.dropbox.appKey` (`DropboxSettings` in `ConnectionSettings`); without it
  `SignInMethods` is empty, so Dropbox is not offered.
- App secret (optional: PKCE signs in without it), write-only: `PUT`/`DELETE api/connections/providers/{kind}/secret`
  writes `ProviderSecrets` (one `SecretFiles` file per kind in `connections/` beside the setup code, cached; not in
  backups). `Sfe:Connections:Dropbox:AppSecret` in the configuration wins. Only kinds implementing `IRegisteredApp`
  have a secret (404 for others). A secret never goes in `EditableSettings`: the settings are sent back whole.
- `GET api/connections/providers/{kind}`: offered, the return address to register
  (`ConnectionProviderEndpoints.ReturnAddress`: scheme, host, path base of the request, then
  `/api/connections/oauth/callback`), where the secret comes from (`none`, `page`, `configuration`).
  `POST …/test`: `IConnectionProvider.TestAsync`
  (Dropbox: `check/app` with key and secret, nothing sent without a secret). All need `server.settings`.
- `DropboxProvider`: authorize address by `DropboxOAuth2Helper.GetAuthorizeUri` (offline token, the four scopes,
  S256 challenge; the verifier is the step's `Pending`), redirect flow sends `redirect_uri`, code flow none; the
  grant is the refresh token, the e-mail and `account_id` (`users/get_current_account`). `Open` comes in phase 3.
- `DropboxErrors.CallAsync` wraps every SDK call. The SDK throws `InvalidOperationException` (source `Dropbox.Api`)
  when an answer has another shape (an empty 404, a proxy page): mapped to `Unavailable`.
- Tests: `RecordedDropbox` is the transport given to `OutboundPolicy` (answers by path, keeps the requests).

## Signing in to make one

- `POST api/connections/authorize` (`ConnectRequest`: kind, level, name, method, root, fields) → `SignInBegun`: the
  provider's address and a state, or the connection when nothing more is needed (password kinds). The draft is
  checked first (`ConnectionService.CheckAsync`), so nobody is sent to the provider for a connection that can't be
  made. Account level: the caller owns it; library level: the request's library.
- `PendingSignIns` keeps the provider's `Pending` (PKCE verifier) server side, by a random state: used once by any
  attempt (another account's try spends it), 10 minutes, at most 1000 waiting; a restart forgets them.
- `GET api/connections/oauth/callback` (needs the session, 401 signed out) changes state on a GET: the state is the
  CSRF defence. It redirects to `{PathBase}/connections?made={id}` or `?error=signed-out|refused|expired|failed`,
  never to an address from the query. `POST …/authorize/complete` ends the code flow.
- Rights: the routes need one of `connections.own`, `connections.library`, `connections.server`; the level's own
  right is checked inside (`ConnectionSignInEndpoints.RightFor`), and again at completion.

## Audit lines

- Security warnings: a connection made, signed in again to another account, offered to more libraries, a group added
  or raised to Write. Information: other changes, removal. Each names the connection by `ConnectionRows.Describe`
  (id, name, kind, level), never a token, a code or the provider's account.
- An app secret set: security warning (`ConnectionProviderEndpoints`); removed: information.

## Gotchas

- **No database cascade from `Connections` to `ConnectionTenants` / `ConnectionGroups`** (two cascade paths on SQL
  Server): whoever deletes a connection loads it with `WithUses()` (`Include` of both); a bare delete throws.
- Changing a library's group rows: keep the rows that stay and change their level in place; removing and adding a row
  with the same key in one context is refused by EF.
- Name clashes in the Api: `ConnectionInfo` (ASP.NET has one: alias it with `using ConnectionInfo =
  Sfe.Core.Connections.ConnectionInfo;`), and `SignInRequest` is the account sign-in's (ours is `ConnectRequest`).
- Tests reading the API's answers need `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` (levels, sign-in
  kinds and field types are strings).
- `ConnectionService` and `ConnectionAccess` read the settings through `SettingLayers.ServerLevel()` (server level
  only); a test container needs `IConfiguration` registered (`ConnectionWorld`).
