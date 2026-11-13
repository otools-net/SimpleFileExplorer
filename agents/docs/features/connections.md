# Connections (external storage)

Code: `src/Sfe.Core/Connections/` (`ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, `ConnectionRows`,
`ConnectionLevels`), `src/Sfe.Core/Data/Entities/Access/Connection*.cs`,
`src/Sfe.Api/Security/ConnectionCredentials.cs`; tests `ConnectionServiceTests`, `ConnectionAccessTests`, `ConnectionDataTests`, `ConnectionCredentialsTests` with
`tests/Sfe.Tests/Infrastructure/Connections/` (`ConnectionWorld`, `FakeProvider`, `FakeProtector`). Work still
planned: [connections-plan](../development/connections-plan.md).

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

## Audit lines

- Security warnings: a connection made, signed in again to another account, offered to more libraries, a group added
  or raised to Write. Information: other changes, removal. Each names the connection by `ConnectionRows.Describe`
  (id, name, kind, level), never a token, a code or the provider's account.

## Gotchas

- **No database cascade from `Connections` to `ConnectionTenants` / `ConnectionGroups`** (two cascade paths on SQL
  Server): whoever deletes a connection loads it with `WithUses()` (`Include` of both); a bare delete throws.
- Changing a library's group rows: keep the rows that stay and change their level in place; removing and adding a row
  with the same key in one context is refused by EF.
- `ConnectionService` and `ConnectionAccess` read the settings through `SettingLayers.ServerLevel()` (server level
  only); a test container needs `IConfiguration` registered (`ConnectionWorld`).
