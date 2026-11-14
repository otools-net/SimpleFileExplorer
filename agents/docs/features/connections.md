# Connections (external storage)

Code: `src/Sfe.Core/Connections/` (`ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, `ConnectionRows`,
`ConnectionLevels`, `ProviderSecrets`, `Dropbox/`), `src/Sfe.Core/Data/Entities/Access/Connection*.cs`,
`src/Sfe.Api/Security/ConnectionCredentials.cs`, `src/Sfe.Api/Endpoints/ConnectionProviderEndpoints.cs`,
`ConnectionFileEndpoints.cs`, `RemoteListings`; tests `ConnectionFileRouteTests`, `RemoteListingsTests`, `DropboxStorageTests`,
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
- Rights: the routes need one of `connections.own`, `connections.library`, `connections.server`; then
  `ConnectionSignInEndpoints.MayManage` over the draft (account: its owner with `own`, never an administrator for
  someone else; library: the request's library with `library`; server: `server`), asked again at completion.
- Signing in again: `POST api/connections/{id}/reauthorize` (`SignInAgain`), same flow with
  `PendingSignIn.ConnectionId`; completion calls `ReauthorizeAsync` (same id, groups, rules) and the callback lands on
  `connections?reconnected={id}`, with `&otherAccount=true` when the provider's account changed. Not found for a
  connection the session may not manage.
- Refresh: the Dropbox SDK gets access tokens from the stored refresh token by itself; it never changes, so nothing
  is written back. A refused one is `CredentialsEnded`: the code using the storage marks the connection
  (`MarkNeedsReconnectingAsync`).

## Listing and managing (`ConnectionEndpoints`)

- `GET api/connections`: `usable` (`ConnectionAccess.UsableAsync` for the session) and `managed`: its own (with
  `connections.own`), its library's and the server's offered there (`connections.library`), the server's
  (`connections.server`), every account's for server administrators; each with `manage` and `remove` flags.
- `ConnectionManagers` is the one rule (also used by the sign-in routes): manage = make, sign in again, rename,
  switch; remove = manage, or a server administrator for an account's connection. Anything else answers 404.
- `PUT …/libraries` needs `connections.server`; `PUT …/groups` needs `connections.library` and acts on the request's
  library only (`ConnectionSharing` answers 404 when the connection isn't that library's or offered to it). A
  library's administrator gives a server connection to groups but can't rename or remove it.

## Browsing and reading (`ConnectionFileEndpoints`)

- `GET api/connections/{id}/browse`, `files/raw/{**path}`, `thumbnails/{**path}`: every one opens through
  `ConnectionFileServices.OpenAsync` (usable by the session or 404, then the route's `files.*` / `library.browse`
  right, then `ConnectionService.OpenAsync`); the storage is disposed with the response (`RegisterForDisposeAsync`),
  since a raw read streams from the provider's `HttpClient` after the handler returns.
- Paths go through `RemotePaths.Normalize` (null = 404). An encoded `%2F` stays in a catch-all route value: it is a
  name with `%2F` inside the root, never a separator (as in the library's routes).
- `RemoteListings`: offset 0 reads the whole folder (up to 20,000 items) and sorts it as `LibraryBrowser` does;
  later pages reuse the read for 5 minutes; larger folders come unsorted through a forward window (`hasMore`).
  `Forget(id)` drops a connection's reads: call it when SFE changes its files (phase 4).
- `files/raw` serves one range (`RemoteRanges`), under the library's sandbox (`RemoteFileResult`: `FilePolicy`, types
  a page could run downloaded); a provider without `RangedReads` serves the whole file with no `Accept-Ranges`.
- `ConnectionFileServices.RunAsync` runs every provider call in a turn of `ConnectionGates` (4 at once per
  connection); after a `RateLimited` answer the connection is left alone until its `Retry-After`: the routes answer
  429 with the time left without calling the provider. Per caller: policy `RequestLimits.Connections`. A raw read
  gives its turn back when its answer starts (the bytes stream after).
- A provider's error becomes `ConnectionProblems`: `CredentialsEnded` marks the connection `NeedsReconnecting` and
  answers 409 `connection-needs-reconnecting`; `RateLimited` 429 with `Retry-After`; `Unavailable` 502.
- Dropbox: `DropboxStorage`; an endpoint's error (`ApiException<T>`) is read from its `error_summary`
  (`not_found`, `not_folder`, `conflict`…); a ranged read asks a temporary link (never sent to the client).

## Changing a connection's files (`ConnectionWriteEndpoints`)

- `POST api/connections/{id}/files/folders`, `rename`, `delete`, `transfer`: `OpenAsync(id, right, write: true)`
  (403 for a connection the session only reads), then the provider in one gate turn; `RemoteListings.Forget` after.
  Delete and transfer answer a result per item; an error stopping the provider (sign-in ended, wait, unreachable)
  answers the usual problem and doesn't list what was done before it.
- No SFE trash: a delete goes to the provider's own (`OwnTrash`); no Undo.
- Client: `PlaceFiles` picks the library's or the connection's route; `PlaceTransfers` remembers the place of a cut or
  copy and refuses a paste or drop in another place (phase 5 copies between them). The folder picker (Move to / Copy
  to) lists the library only: hidden on a connection.

## The listing on a connection (`?c=`)

- `LibraryRoute.c` (`?c=7&path=…`) lists a connection's folder through `connectionPage` (`library/connection-pages.ts`),
  which sets `ConfigService.place`. `config` is then `atPlace(config, place)`: file rights other than view and
  download, tags, index, data server, streaming, external player, trickplay, covers and animated hover are gone, so
  every component already hiding what a session may not do hides them. `urls` give the connection's addresses
  (`api/connections/{id}/files/raw/…`); player and view pages carry `c` too (`enterPlace`).
- Leaving: a search (`navigate` with `q` drops `c`), quick links (`libraryParams`, `config.libraryUrls`), the SFE link,
  `reveal`; the library page clears the place when destroyed (admin pages show library files).
- `setRawFormatsOnly` (formats.ts): only image, video, audio and PDF open (no text, archive, EPUB or Flash reader on
  the server for a connection); the viewer's "open in a tab" gives the file itself.
- Not there yet, hidden: Properties (`ItemMenu.canSeeProperties`, `FolderMenu`), adding quick links
  (`QuickLinksService.addable`: links are library paths).
- Zips: `FileDownloads` asks `createArchive(paths, place.id)`; `ConnectionArchiveEndpoints` keeps the library's two
  steps (`ArchiveLinks` with scope `connection:{id}`: the library's route never takes such a link, nor uses it up).

## Client (`app/connections/`)

- `ConnectionsSectionComponent`: tab `mine` (My account › Connections: one's own), `shared` (Administration ›
  Connections: the rest of `managed`), `setup` (`ProviderSetupComponent` for `dropbox`). What a row offers follows the
  server's `manage` / `remove` flags; groups and libraries come in the same `GET api/connections`.
- `ConnectDialog` builds its form from `config.connections.kinds[].signIn` (no code per provider). Redirect flow:
  `window.location.assign` to the address the server gave; code flow: the address in a new tab, then the code.
- `connectionsLanding` (route `connections`): the callback's `?made=` / `?reconnected=` / `?error=` become a toast,
  then the connection's page (My account for one's own).
- Requests are `Api` methods (`connections`, `authorizeConnection`, …): the one place the client makes requests
  ([frontend](../development/frontend.md)); specs mock them with `mockApi()`.
- `Api` answers a 409 with its problem body instead of throwing: `problemOf` tells it apart.

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
