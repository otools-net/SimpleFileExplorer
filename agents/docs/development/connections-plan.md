# Plan: external connections (Dropbox first)

Files: new `src/Sfe.Core/Connections/` (and `Connections/Dropbox/`), `Data/AccessDbContext.cs`,
`Data/AccessMigrations/`, `Access/RightCatalog.cs`, `Access/FolderRuleService.cs`, `Settings/`; new
`src/Sfe.Api/Endpoints/Connection*Endpoints.cs`, `Security/`; client new `app/connections/`, `app/library/` (listing
page), `app/files/`, `app/admin/`, `core/`. Part 5 of the [epic](external-access-epic-plan.md); needs
[api-v1-plan](api-v1-plan.md) finished (its last phase needs [client-library-plan](client-library-plan.md)).
Index, search, tags and previews are [connections-index-plan](connections-index-plan.md). Delete this plan once its
last item is ticked.

## Starting point (2026-11-13)

- Core services are per library and read the disk directly (`FileOperations`, `FileJobs`, `FileContent`, `Uploads`,
  `LibraryBrowser`…); no storage interface exists and this plan does not add one under them.
- Server-level services (accounts, settings levels, `FolderRuleService`) are registered once (`AddSfeAccess`) and
  handed to each library ([tenants](../architecture/tenants.md)).
- Folder rules are keyed by library and path ([folder-access](../features/folder-access.md)).
- The server makes no outbound call today except fetching ffmpeg on request.

**Done when:** an account with the right connects a Dropbox account at its level, people allowed to use it browse it
beside the library, manage its files, and copy or move between it and the library, with groups and folder rules
deciding who sees and changes what; a second provider can be added by writing one folder of code.

## Decisions

- A connection is a **location** beside the library: its own tree, paths relative to the folder it is rooted at in
  the remote storage. API: `api/v1/connections/{id}/…` mirroring the library's file routes. Client:
  the listing page with a location (`?conn={id}&path=`).
- Levels: **account** (owner only, in any library they work in), **library** (members by groups: Read or Write, then
  folder rules), **server** (offered to chosen or all libraries; each library's administrators set groups and rules).
- Rights (Administrators only, `since`, never guests): `connections.use` (see and open those one may),
  `connections.own`, `connections.library`, `connections.server` (server scope). Inside a connection the existing
  `files.*` rights apply, narrowed by the connection's level for the person and its folder rules.
- **Who runs the server decides** ([security](security.md)): settings `Connections:Enabled`, `Connections:Providers`
  (allowed kinds), `Connections:Levels` (allowed levels), lockable from the environment. Off until a provider is
  configured.
- Credentials: protected with Data Protection (purpose `SFE.Connections`) in the access database; never in answers,
  logs or errors. A backup restored without the keys folder needs the connections authorized again.
- Delete uses the provider's own trash or versions (Dropbox keeps deleted files); SFE's trash stays for the library.
  The delete dialog says which applies.
- Fully local stays true: the client bundle names no provider address; the authorize address comes from the server
  at run time and the browser only **navigates** to it.

## Architecture

- `IConnectionProvider` (one per kind, registered in a `ConnectionProviders` catalog): kind id, name, icon name,
  `ProviderCapabilities` (the list in phase 0's results), how it signs in (`OAuthRedirect`, `OAuthCode`, later
  `Password`, `Key`)
  with the fields it needs described as data (label, type text/secret/address), so the client's form needs no code
  per provider; `OpenAsync(credentials)` gives an `IRemoteStorage`.
- `IRemoteStorage`: `ListAsync(path, cursor)` paged, `GetAsync(path)`, `OpenReadAsync(path, range)`,
  upload session (`Begin`, `Append`, `Finish` with a conflict mode), `CreateFolderAsync`, `MoveAsync`, `CopyAsync`,
  `DeleteAsync`, `ThumbnailAsync`, `ChangesAsync(cursor)`, `SpaceAsync`. Errors as one `RemoteStorageException` with
  a kind (not found, exists, refused, credentials ended, rate limited with a delay, too large, unavailable).
- `RemotePaths`: the same rules as `LibraryPaths` for a remote tree (normalised, no `..`, never above the
  connection's root folder); names refused by a provider reported per item.
- `OutboundPolicy`: every provider's HTTP or socket goes through it (allowed hosts per provider; for providers with
  an address typed by a user: no loopback, link-local or private ranges on an External server unless allowed by a
  setting; redirects re-checked). Dropbox has fixed hosts; the policy exists from the start for WebDAV, FTP and SCP.
- `ConnectionService` (server level): create, authorize, rename, disable, remove, who may use; `ConnectionAccess`
  gives a person's level at a path of a connection (owner, group level, folder rules) — the one place routes and
  jobs ask.
- `TransferJobs` (server level): copy and move where at least one side is a connection, between two
  `ITransferEndpoint`s: the library's wraps `FileOperations`, `FileContent` and `Uploads` (so the index, default
  tags, rules, change feed and write protections apply), a connection's wraps `IRemoteStorage`. Same conflict choices
  as `FileJobs` (`ConflictChoice`), access asked again per item, a move = copy, verify (size, hash when the provider
  has one), delete the source. Local-to-local stays in `FileJobs`. Runs listed in the operations panel and the task
  history.

## Progress

### Phase 0: spike and checks (results written here)

- [x] Dropbox: official SDK (`Dropbox.Api`: maintained? targets, dependencies) or plain HTTP over `HttpClient`
  (look for a package first); a console spike signing in with PKCE by both flows, listing, ranged download, chunked
  upload, the change cursor
- [x] `FolderRule` / `FolderPolicy` / `FolderRuleService`: can a nullable connection column key the rules of a
  connection (SQLite: nullable column added; unique indexes; the 250-rule cap; `BeginMoveAsync` on renames inside a
  connection)? Else a twin table with the same evaluator
- [x] The listing page: what `ListingStore`, `Api` and the item components assume about "the library" (list here what
  must take a location)
- [x] Write the interfaces against Dropbox **and on paper** against WebDAV, SFTP and Google Drive (ids instead of
  paths, no ranged reads, no change cursor): adjust capabilities before any code depends on them

#### Results (2026-11-13)

**Dropbox client: the official SDK, `Dropbox.Api` 7.4.0** (2026-09-23; three releases in 2026; .NET Standard 2.0;
one dependency, `Newtonsoft.Json` 13.0.4; 9.3 M downloads). No real account was at hand (owner's answer): the spike
ran the SDK against a fake `HttpMessageHandler` and read every request it made; the check with a real account is
the manual list of phase 2.

- Every call goes through the `HttpClient` given in `DropboxClientConfig` (`HttpClient`, `LongPollHttpClient`) and
  the `client` argument of `DropboxOAuth2Helper.ProcessCodeFlowAsync`: `OutboundPolicy` and the recorded answers of
  the tests are a handler of that client. Hosts seen: `api.dropbox.com` (token), `api.dropboxapi.com` (RPC),
  `content.dropboxapi.com` (download, upload), `*.dl.dropboxusercontent.com` (temporary links); the browser alone
  goes to `www.dropbox.com/oauth2/authorize`. `notify.dropboxapi.com` (long poll) is not used.
- PKCE: `GeneratePKCECodeVerifier` (128 characters), `GeneratePKCECodeChallenge` (S256), `GetAuthorizeUri(Code, key,
  redirect or null, state, tokenAccessType: Offline, scopeList, codeChallenge)`. Without a redirect address Dropbox
  shows the code to copy: the two flows differ only by that argument. The token request carries `code_verifier`,
  and `client_secret` too when a secret is given: **the app secret is optional** (PKCE alone works for both flows).
- A client made from the refresh token (`new DropboxClient(refresh, key[, secret], config)`) fetches an access token
  by itself before the first call. A refused refresh (`invalid_grant`) is an `AuthException`; a wrong code at
  sign-in an `OAuth2Exception`: both mean "needs reconnecting". The refresh token does not change at a refresh:
  nothing is written back to the database.
- Errors: `ApiException<TError>` with typed unions (`path/not_found`), `RateLimitException.RetryAfter` (seconds),
  `AuthException` (401), `RetryException` (5xx), `BadInputException` (400). `MaxRetriesOnError = 0`: SFE's own gate
  decides the waits.
- **No ranged download in the SDK** (`DownloadAsync(path, rev)` takes no range): a ranged read asks
  `GetTemporaryLinkAsync` (a link for 4 hours) and makes a `GET` with `Range` on it with the same policy-bound
  `HttpClient`. The link is never given to the browser (content stays streamed through SFE).
- Upload sessions: `UploadSessionStartAsync` / `AppendV2Async(cursor(sessionId, offset))` / `FinishAsync(cursor,
  CommitInfo(path, WriteMode.Add, autorename))`; the SDK adds the `content_hash` of each part (`AutoContentHash`).
- Listing: `ListFolderAsync(path, limit)` then `ListFolderContinueAsync(cursor)`; `""` is the root; entries carry
  `PathLower`, `PathDisplay`, and for files `Size`, `ServerModified`, `ContentHash`, `IsDownloadable`. The cursor of
  the last page is the change cursor (`ListFolderGetLatestCursorAsync` gives one without listing).

**Folder rules: a twin table** (`ConnectionFolderRules`) compiled by the same `FolderPolicy`, not a nullable column
in `FolderRules`:

- Nine queries of `FolderRuleService` and `RightsEvaluator` read `FolderRules` by `TenantId` alone: each would need
  `ConnectionId == null`, and one forgotten makes a connection's rule hide or open a library folder.
- A server-level connection has rules **per library** it is offered to: the key is (tenant, connection, path), and
  `Connections → FolderRules` beside `Tenants → FolderRules` is one more cascade path for SQL Server.
- Paths compare by the provider's rules (Dropbox: case-insensitive everywhere), the library's by the host's
  (`LibraryPaths.PathComparer`): one table would mix two comparers.
- The 250 cap protects the index queries of a library (SQL parameters); a connection has its own count.
- Reused as they are: `FolderLevel`, `FolderPrincipalRef`, `FolderPolicy` (given a comparer), the move logic
  (`Compare`, pins) once extracted from `FolderRuleService` (phase 6).

**The listing page** (client): what is tied to the library's one path space today:

- `LibraryRoute` (`path`, `q`, `sort`, `desc`) and `routeParams` / `readRoute`: gain `conn`; `browseParams`, links.
- `ListingStore` (a root singleton, injected by 82 files): `selection`, `revealed`, restore anchors and the change
  feed are keyed by `path` alone; `can(right, item)` reads the library's rights and `folderAccess`.
- `Api`: about 100 uses of `path` under `api/…` (browse, siblings, covers, `files/raw`, thumbnails, trickplay,
  file-tags, jobs, uploads, properties, archives): a location-aware set is needed for browse, raw, file management,
  uploads, zip and properties only in this plan; the rest is hidden by capabilities.
- `LibraryItem` has what a remote item has (`name`, `path`, `kind`, `size`, `modified`); `modified` is already
  nullable (Dropbox folders have none). `indexed`, `tags`, `excluded`, `defaultTags` stay absent.
- Hidden for a connection until [connections-index-plan](connections-index-plan.md): search box, tags, covers,
  scan panel, quick links, trickplay, thumbnails (phase 3 shows the provider's when it has them), siblings.
- The exact list of components is made when phase 3 starts; the rule is one `location` signal in `ListingStore`
  (`null` = the library) that the item URLs and `Api` calls read, never a second listing page.

**Interfaces checked on paper** (the "Architecture" section above follows these):

| Need | Dropbox | WebDAV | SFTP | Google Drive |
|---|---|---|---|---|
| Address | path | path | path | id; names not unique in a folder |
| Listing | pages + cursor, unordered | whole folder (`PROPFIND` depth 1) | whole folder | pages, by parent id |
| Ranged read | through a temporary link | `Range` (most servers) | seek | `Range` |
| Upload | sessions, parts up to 150 MB | one `PUT` | write at offset | resumable session |
| Server-side copy / move | both | both | move only (rename) | both |
| Changes | cursor | none (`getetag` per folder) | none | page token |
| Hash | `content_hash` (its own) | sometimes | none | md5 / sha256 |
| Thumbnails | yes | no | no | yes |
| Own trash | yes | no | no | yes |
| Names | case-insensitive | server's | server's | anything, duplicates |
| Folder dates | none | yes | yes | yes |
| Files without content | Paper docs (`is_downloadable`) | none | none | Docs, Sheets |
| Sign-in | OAuth (redirect or code) | address + password | address + password or key, **host key to confirm** | OAuth |

- `IRemoteStorage` stays **path-addressed**: SFE's routes, rules and transfers speak paths. A provider of ids
  (Google Drive) resolves paths itself (a bounded cache per connection) and shows duplicate names apart; `RemoteItem`
  carries the provider's `Id` (opaque) for that and for the index of plan 6.
- Listings come **unordered and in pages of the provider's choice**: sorting by name, date or size needs the whole
  folder. Phase 3 lists a folder once into a bounded per-connection snapshot (as `CachedListing` does for the disk)
  and pages from it; a folder past the cap lists in the provider's order with sorting off.
- `OpenReadAsync` answers what it could do (`RemoteContent`: stream, the range actually served, total length): a
  provider without ranges serves the whole file, and the route then answers 200 without `Accept-Ranges`.
- Uploads keep `Begin` / `Append` / `Finish`: a provider with one `PUT` (WebDAV) keeps the parts in a temporary
  file of the server until `Finish` (capability `UploadsInParts` false = the server's disk is used, so the free
  space of the cache folder is checked).
- `ProviderCapabilities` as built: `RangedReads`, `UploadsInParts`, `MaxUploadPart`, `ServerSideCopy`,
  `ServerSideMove` (two flags: SFTP moves but cannot copy), `Thumbnails`, `ChangeCursor`, `ContentHash` (a name:
  `dropbox`, `md5`, `sha256`; null = none), `OwnTrash`, `CaseInsensitiveNames`, `UniqueNames`, `FolderDates`,
  `FreeSpace`. `RemoteItem.Downloadable` is false for files without content.
- Sign-in kinds: `OAuthRedirect`, `OAuthCode`, `Password`, `Key`; SFTP's host key is a **second step** ("this
  server shows this key: accept?"): not built now, but the authorize routes answer a step (`next`) so it fits later.

### Phase 1: foundation

- [x] `Connections/` interfaces, `ProviderCapabilities`, `RemotePaths`, `OutboundPolicy`, an in-memory
  `FakeProvider` for tests (in the test project). As built: `IConnectionProvider` (sign-in in two calls,
  `BeginSignInAsync` / `CompleteSignInAsync`, so a password kind ends at the first and OAuth at the second),
  `IRemoteStorage`, one `RemoteStorageException`; `OutboundPolicy.CreateClient(rule)` is the only maker of a
  provider's `HttpClient` (tests pass a transport); `FakeStorageTests` is the contract a real provider's tests copy
- [x] Entities `Connection`, `ConnectionTenant` (server level: offered to), `ConnectionGroup` (library, group, Read or
  Write); migration in `AccessMigrations` and the SQLite path; removal of a user, group or library removes its rows
  (SQL Server: one cascade path, [database](database.md)). As built: `Connections.OwnerId` (account level only,
  cascade) apart from `CreatedById` (no key: a library's connection outlives who made it); the database removes a
  connection with its owner or library, and `ConnectionTenants` / `ConnectionGroups` rows with their library or
  group; from a connection to those rows there is **no** database cascade (two paths): whoever removes a connection
  loads it with `Include(Tenants)`, `Include(Groups)` and EF deletes them (`ClientCascade`), so `AccessService` is
  untouched; a check constraint ties the level to owner / library. Checked on LocalDB (migration applies, deletes
  of a user, a group, a library, a connection with rows refused) and by `ConnectionDataTests` on SQLite
- [x] Settings and rights (with `since`), `core/rights.ts`; `api/v1/config` tells the client what is on. As built:
  rights in `Access/ConnectionRights.cs` (spread into `RightCatalog.All`; `since: 15`), settings
  `connections.enabled` / `providers` / `levels` (`ConnectionSettings`: server level, right `server.settings`,
  `Enabled` defaults to true: nothing is offered until a kind has a sign-in method), `AllowPrivateAddresses` in the
  configuration only; `ClientConfig.Connections` (`levels`, `kinds` with their sign-in fields and capabilities),
  null when off or without a `connections.*` right; client: settings tab and Administration › Connections
- [x] `ConnectionService`, `ConnectionAccess`; credentials protected; audit log lines for every change. As built:
  `ConnectionService` (lists by level / library / owner, create from a grant, update, reauthorize telling another
  account, mark needs reconnecting, remove with its rows, `OpenAsync`), who uses it in `ConnectionSharing`
  (libraries of a server-level one, a library's groups), `ConnectionAccess.LevelAsync` / `UsableAsync` (rules in
  [connections](../features/connections.md)); `ConnectionCredentials` (Api) protects with purpose `SFE.Connections`;
  security warnings for a connection made, signed in to another account, or opened to more

### Phase 2: Dropbox sign-in and management

- [x] Settings `Connections:Dropbox:AppKey`, `AppSecret` (write-only: the API never returns it; stored with
  `SecretFiles` or protected, not in plain `settings.json`), the redirect address shown to the administrator to
  register, a "Test" action. As built (owner's choice: both on the page, the secret write-only): the app key is
  the server setting `connections.dropbox.appKey`; the secret goes through `PUT`/`DELETE
  api/connections/providers/{kind}/secret` into `ProviderSecrets` (a `SecretFiles` file per kind in
  `connections/` beside the setup code; not in backups), `Sfe:Connections:Dropbox:AppSecret` in the configuration
  wins (`IRegisteredApp`); `GET api/connections/providers/{kind}` tells offered / return address / where the secret
  comes from; `POST …/test` (`IConnectionProvider.TestAsync`: Dropbox's `check/app` with key and secret). Also
  built here: `DropboxProvider` sign-in (PKCE, both flows, account by `get_current_account`), `DropboxErrors`,
  tests on recorded answers (`RecordedDropbox`). The page's secret field, address and Test button come with the
  client item below
- [x] `POST connections/authorize` (state bound to account and level, PKCE verifier kept server side, 10 minutes),
  `GET connections/oauth/callback` (redirect flow; the session must be the state's account), `POST
  connections/authorize/complete` (code flow). As built: `ConnectionSignInFlow` (Core: draft checked by
  `ConnectionService.CheckAsync` before the person leaves, method among the kind's, a password kind made at once)
  and `PendingSignIns` (random 32-byte state, used once by any attempt, bound to the account, 10 minutes, at most
  1000); routes in `ConnectionSignInEndpoints` (any of `connections.own` / `library` / `server`, then the level's
  own right, asked again at completion); the callback redirects to `connections?made={id}` or `?error=signed-out |
  refused | expired | failed` under the app's path base (client page in item 4); signed out it answers 401
- [x] Refresh tokens; state "needs reconnecting" when Dropbox refuses them; reconnect keeps the connection's id,
  groups and rules; the root folder inside Dropbox chosen at creation. As built: the SDK refreshes the access token
  itself from the stored refresh token, which does not change (nothing written back); a refused refresh is
  `CredentialsEnded` (`DropboxErrors`), and whoever uses the storage marks the connection
  (`MarkNeedsReconnectingAsync`; the routes of phase 3 do it); `POST connections/{id}/reauthorize` signs in again
  through the same flow (`BeginReconnectAsync`, `PendingSignIn.ConnectionId`, `ReauthorizeAsync`), the callback
  landing on `connections?reconnected={id}[&otherAccount=true]`; who may make or sign in again is one rule over the
  draft (`ConnectionSignInEndpoints.MayManage`: the owner only at account level). The root folder is typed at
  creation (`ConnectRequest.RootPath`); choosing it by browsing comes with the listing of phase 3
- [ ] Client: My account › Connections, Administration › Connections (library and server levels, who may use),
  add dialog driven by the provider's description; phone sheets; user docs with the Dropbox app steps
  - Server part built first: `GET api/connections` (`ConnectionsView`: usable, and managed with `manage` /
    `remove` flags), `PATCH` / `DELETE api/connections/{id}`, `PUT …/libraries` (`connections.server`), `PUT
    …/groups` (`connections.library`, the request's library); one rule in `ConnectionManagers`

### Phase 3: browse and download

- [ ] `GET connections/{id}/browse` (paged, the listing's shape), `files/raw` (ranges, the sandbox rules of
  `LibraryEndpoints.ServeFile`), marked folder-scoped
- [ ] Client: the listing page takes a location; sidebar entry and a connections page; what a location can't do is
  hidden from the capabilities the server sends; download; zip of several items (streamed)
- [ ] Rate limits and waits: Dropbox 429 honoured, a per-connection gate, per-caller limits on listing and reads

### Phase 4: file management

- [ ] New folder, rename, move and copy inside a connection (server side at the provider when it can), delete
- [ ] Uploads: the resumable upload routes with a connection target (chunks passed on to the provider's session, no
  whole file on the server's disk)
- [ ] Batch rename, properties (what the provider knows), selection bar, context menus, F2 / Shift+N

### Phase 5: transfers with the library

- [ ] `ITransferEndpoint` for the library and for a connection; `TransferJobs` with conflicts, progress, cancel,
  resume of a large file's upload after a failure
- [ ] Clipboard, drops and Move/Copy dialogs accept a location on both sides; operations panel and task history
- [ ] Free space checked at the destination when it tells; a move verifies before deleting

### Phase 6: groups and folder rules

- [ ] Rules inside library- and server-level connections with the model chosen in phase 0; `ConnectionAccess` answers
  404 for what a person doesn't see; "throughout" checks for folder operations
- [ ] Management card reused on a connection's folders ([folder-access](../features/folder-access.md)); rules follow
  folders renamed or moved in the app ([file-operations](file-operations.md))

### Phase 7: the C# library and close

- [ ] `sfe.Connections` in the facade: list, and the `Files`, `Jobs`, uploads and downloads over a connection
  location; baseline of the v1 document updated
- [ ] Feature notes (`agents/docs/features/connections.md`, `connection-providers.md` = how to add one,
  `dropbox.md`, `transfers.md`), [security](security.md) vectors, `docs/` pages; this plan deleted

## Attack vectors to test

- Another account's private connection by id (browse, raw, jobs, uploads, rename): 404; an administrator reading
  its content: refused; a library-level connection from another library; a server-level one in a library it isn't
  offered to.
- OAuth: a callback with another account's state, a state reused, late, or forged; a code completed twice; the
  callback opened while signed out; an authorize started at a level the account may not create.
- Paths: `..`, encoded separators, names the provider folds (case, trailing dots), above the connection's root
  folder.
- A read-only connection or a Read group level on every writing route and on both sides of a transfer.
- A hidden folder of a connection through listing, raw, jobs, zip, properties, uploads into it.
- Secrets (tokens, app secret, PKCE verifier) in logs, answers, the job view, backups (protected only), URLs.
- Remote content: `.html` / `.svg` served from a connection (sandboxed, attachment), a script loaded by a page.
- Floods: listings and reads as one caller; a transfer of millions of items (bounded memory, batches).
- Later providers: an address pointing at loopback or the LAN from an External server, a redirect to one.
- Local mode: connections work for the administrators on this computer; nothing new answers other computers.
- A token (access token of the API) limited in rights or library using connections.

## Traps

- "A root singleton must never depend on a Core type" ([tenants](../architecture/tenants.md)): `TransferJobs` is
  server level and needs a library's `FileOperations`: take it per job through `TenantRegistry`, never by injection.
- Dropbox: paths are case-insensitive and case-preserving (`path_lower` to compare, `path_display` to show);
  `too_many_write_operations` on parallel writes in one namespace (serialise writes per connection); upload sessions
  cap a request at 150 MB; listings are eventually consistent; folders have no modification date; team spaces and
  shared folders have their own roots; short-lived access tokens need the refresh token (`token_access_type=offline`).
- A redirect address must match what is registered at Dropbox exactly; behind a proxy it comes from the forwarded
  headers ([sessions-and-pipeline](../architecture/sessions-and-pipeline.md)), under a virtual directory it includes
  it. When it can't match, the code flow is offered.
- The session cookie is `SameSite=Lax`: it is sent on the top-level navigation back from Dropbox (GET); the callback
  changes state on a GET, so the protected state value is its CSRF defence.
- `ApiTests.WebClientHasNoExternalResources` scans the bundle and the specs: no `dropbox.com` in client code or
  tests (dotless fake hosts).
- Tests never call Dropbox: `FakeProvider` for the suites, recorded answers for the Dropbox client's own tests; a
  manual check list with a real account before each release of this plan.
- Outbound calls on a server without internet (many installs): everything else must stay unaffected and the state
  says "not reachable", with timeouts that never hold a request.
- An account-level connection is the same in every library its owner opens; group levels and rules exist only for
  library and server levels.
- The owner's account disabled or deleted: its connections are disabled or removed, their running jobs stop.
