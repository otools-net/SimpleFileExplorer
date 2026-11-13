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
  `ProviderCapabilities` (ranged reads, server-side copy and move, thumbnails, change cursor, content hash, own
  trash, case-insensitive names, free space), how it signs in (`OAuthRedirect`, `OAuthCode`, later `Password`, `Key`)
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

- [ ] Dropbox: official SDK (`Dropbox.Api`: maintained? targets, dependencies) or plain HTTP over `HttpClient`
  (look for a package first); a console spike signing in with PKCE by both flows, listing, ranged download, chunked
  upload, the change cursor
- [ ] `FolderRule` / `FolderPolicy` / `FolderRuleService`: can a nullable connection column key the rules of a
  connection (SQLite: nullable column added; unique indexes; the 250-rule cap; `BeginMoveAsync` on renames inside a
  connection)? Else a twin table with the same evaluator
- [ ] The listing page: what `ListingStore`, `Api` and the item components assume about "the library" (list here what
  must take a location)
- [ ] Write the interfaces against Dropbox **and on paper** against WebDAV, SFTP and Google Drive (ids instead of
  paths, no ranged reads, no change cursor): adjust capabilities before any code depends on them

### Phase 1: foundation

- [ ] `Connections/` interfaces, `ProviderCapabilities`, `RemotePaths`, `OutboundPolicy`, an in-memory
  `FakeProvider` for tests (in the test project)
- [ ] Entities `Connection`, `ConnectionTenant` (server level: offered to), `ConnectionGroup` (library, group, Read or
  Write); migration in `AccessMigrations` and the SQLite path; removal of a user, group or library removes its rows
  (SQL Server: one cascade path, [database](database.md))
- [ ] Settings and rights (with `since`), `core/rights.ts`; `api/v1/config` tells the client what is on
- [ ] `ConnectionService`, `ConnectionAccess`; credentials protected; audit log lines for every change

### Phase 2: Dropbox sign-in and management

- [ ] Settings `Connections:Dropbox:AppKey`, `AppSecret` (write-only: the API never returns it; stored with
  `SecretFiles` or protected, not in plain `settings.json`), the redirect address shown to the administrator to
  register, a "Test" action
- [ ] `POST connections/authorize` (state bound to account and level, PKCE verifier kept server side, 10 minutes),
  `GET connections/oauth/callback` (redirect flow; the session must be the state's account), `POST
  connections/authorize/complete` (code flow)
- [ ] Refresh tokens; state "needs reconnecting" when Dropbox refuses them; reconnect keeps the connection's id,
  groups and rules; the root folder inside Dropbox chosen at creation
- [ ] Client: My account › Connections, Administration › Connections (library and server levels, who may use),
  add dialog driven by the provider's description; phone sheets; user docs with the Dropbox app steps

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
