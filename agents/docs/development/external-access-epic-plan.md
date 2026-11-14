# Plan (epic): versioned API, access tokens, C# client library, external connections

Files: the plans below (plans 1, api-v1, 2, api-tokens, and 5, connections, done and deleted); code named in each.
This file is the **overview**: what was decided, in what order the plans run, what none of them does.
Delete it with the last plan (see [AGENTS.md](../../../AGENTS.md), "Keeping these notes current"), moving the
decisions that stay true into the feature notes.

## Why

- SFE serves only its own Angular client: about 30 route groups under an unversioned `/api`, a cookie session plus
  `X-SFE-Request` as the only sign-in, no tokens, no CORS.
- File code reads the local disk directly (about 350 `System.IO` calls in 68 files, no storage abstraction).
- Wanted: connections to other storage (Dropbox first; WebDAV, FTP, SCP, Google Drive later) linked to an account, a
  library or the server; an API third-party apps can rely on, used by our client too; a C# library hiding that API.

## The plans, in order

Each plan is worked alone, with its own releases; a later one builds on the earlier ones.

| # | Plan | Delivers | Needs |
|---|---|---|---|
| 1 | api-v1 (done 2026-11-14; notes: [api-versioning](api-versioning.md)) | every route under `/api/v1`, old paths as aliases, compatibility net | |
| 2 | api-tokens (done 2026-11-14; notes: [access-tokens](../features/access-tokens.md)) | personal access tokens (`Authorization: Bearer`) | 1 |
| 3 | namespace-rename (done: [namespaces](namespaces.md)) | C# namespaces `Sfe.*` become `Otools.Net.Sfe.*` (refactoring) | |
| 4 | [client-library-plan](client-library-plan.md) | `Otools.Net.Sfe.Client` package over the v1 API | 1, 2, 3 |
| 5 | connections (done 2026-11-14; notes: [connections](../features/connections.md)) | providers, Dropbox, browse, file management, transfers, rules | |
| 6 | [connections-index-plan](connections-index-plan.md) | index, search, tags and previews of connection files | 5 |

**A session takes the first unticked item of the first unfinished plan**, reads that plan's phase section, and ticks
the item in the commit that does it (as [readability-refactoring-plan](readability-refactoring-plan.md) does).

## Status board (done / next)

Updated in every commit of the epic, so a new session knows where to resume without reading history. The detail is
in each plan's `## Progress` checklist; this board is the summary.

| # | Plan | State | Done | Next |
|---|---|---|---|---|
| 1 | api-v1 | **done** 2026-11-14; plan deleted, what stays true in [api-versioning](api-versioning.md) and [api-routes](api-routes.md), user page `docs/reference/api.md` | phases 0 to 5 (rules; `ApiRoutes`; base `/api/v1`, `LegacyApiPaths`, `LegacyPathLog`, `LegacyPathTests`; client `API_BASE`, tests `V1`, e2e and tools; OpenAPI baseline, `OpenApiBaselineTests`, `x-sfe-rights`, `ProblemTypes`; user docs) | |
| 2 | api-tokens | **done** 2026-11-14; plan deleted, what stays true in [access-tokens](../features/access-tokens.md), user pages `docs/reference/api.md#calling-with-a-token`, `my-account.md`, `spaces-and-accounts.md` | phase 1 (table `AccessTokens`, `AccessTokens` service, `security.accessTokens.*` settings); phase 2 (`AccessTokenAuthentication` scheme + selecting policy scheme, `CurrentAccess` limits, `RequestGuard`, `.SessionOnly()`, throttle); phase 3 (rights `account.tokens`/`access.tokens`, `me/tokens` and `access/tokens` routes, bearer scheme in OpenAPI, `AccessTokenAudit`, `config.accessTokens`); jobs started with a token checked per item with its rights, stopped when it is revoked (`JobToken`); phase 4 (account and access pages, settings field); phase 5 (user docs, vectors in [security](security.md#access-tokens)) | screenshots of the token pages (`agents/tools/docs-screenshots`: needs new scenes in `shots.mjs`) |
| 3 | namespace-rename | **done** 2026-11-14; plan deleted, what stays true in [namespaces](namespaces.md) | phases 0 to 3 (every namespace `Otools.Net.Sfe.*`, `agents/tools/namespace-rename/`, `LegacyLogCategories` keeping old `Sfe.` log levels; notes rewritten; OpenAPI and routes identical) | |
| 4 | client-library | **in progress** | phases 0 and 1 (Kiota chosen, generated output committed under `src/Sfe.Client/Generated/`, `agents/tools/client-generation/`, `tests/Sfe.Client.Tests` on the in-process server with a token) | phase 2 of [client-library-plan](client-library-plan.md) (`SfeClient`, errors, retries) |
| 5 | connections | **done** 2026-11-14 (started before plans 1 to 4 at the owner's request; routes mapped through `ApiRoutes`, so they follow the base); plan deleted, what stays true in [connections](../features/connections.md) and its sibling notes | phases 0 to 6 (providers, `OutboundPolicy`, tables, rights, settings; Dropbox sign-in and management; browse, read, zips, gates; file management, uploads, properties; transfers with the library, retry, history; folder rules, enforcement, following moves); phase 7: notes split, security vectors in [security](security.md#connections) | the facade item (`sfe.Connections`) moved to plan 4's phase 5; a person runs `agents/tools/dropbox-check` before a release |
| 6 | connections-index | not started | | |

**Session log** (newest first; one line per session: date, what was done, anything left half-way):

- 2026-11-14 (ninth session, continued): api-tokens phase 5 (docs, `access-tokens` note, plan deleted);
  namespace-rename phases 1 to 3 (`Otools.Net.Sfe.*`, old log-level keys kept, routes and OpenAPI identical; plan
  deleted, note `namespaces`). Left: token-page screenshots (new scenes in `shots.mjs`). Next: client-library.
- 2026-11-14 (tenth session): api-tokens phase 5: user docs (calling with a token, curl and PowerShell examples,
  settings, pages), vectors in `security.md`, feature note `access-tokens.md`; plan deleted. Left: screenshots of the
  token pages (no scene yet). Next: namespace-rename.
- 2026-11-14 (ninth session): api-tokens phases 2 and 3 (authentication with a bearer, session-only routes,
  throttle, token routes and rights, OpenAPI bearer scheme, audit lines; `AccessTokenAuthenticationTests`,
  `AccessTokenRouteTests`, baseline written); jobs started with a token checked per item (`AccessTokenJobTests`);
  phase 4 (client pages). The owner confirmed the open defaults asked (tokens survive a password change; move source
  to SFE's trash; rules may raise; connection to connection allowed). Next: api-tokens phase 5 (docs).
- 2026-11-14 (eighth session): api-tokens phase 1: table `AccessTokens` (migration applied on LocalDB), service
  `AccessTokens`, settings `security.accessTokens.*` (no settings-page field yet: phase 4). Next: api-tokens phase 2.
- 2026-11-14 (seventh session, continued): api-v1 phase 5: `docs/reference/api.md` (base address, compatibility,
  old addresses until 2.0, problem types, sign-in today), facts moved into `api-versioning.md` and `api-routes.md`,
  plan 1 deleted. Next: api-tokens (plan 2).
- 2026-11-14 (seventh session): api-v1 phase 3 (client, tests and tools on v1); phase 4: the OpenAPI baseline and
  the test refusing breaking changes (`x-sfe-rights`, `x-sfe-problem-types` added to the document), `ProblemTypes`.
  Next: api-v1 phase 5.
- 2026-11-14 (sixth session): api-v1 phase 2: routes under `/api/v1`, old paths rewritten by `LegacyApiPaths`
  (explicit `UseRouting` after it; `Deprecation` + `Link`; OAuth return address kept unversioned), one log line per
  caller and day, `LegacyPathTests`. Next: api-v1 phase 3.
- 2026-11-14 (fifth session): connections phase 7 closed plan 5: feature notes split (`connections`,
  `connection-files`, `connection-providers`, `dropbox`, `transfers`, `connection-folder-rules`), open decisions
  (defaults taken) listed in `connections.md`, attack vectors in `security.md`, `sfe.Connections` moved to plan 4,
  user docs checked; plan and handover deleted. Next: api-v1 phase 2.

- 2026-11-14 (fourth session): connections phase 3 (`DropboxStorage`, browse, raw and zip routes; the listing on a
  connection; rate limits and the provider's waits); phase 4 item 1 (create, rename, move, copy, delete in a
  connection) and item 2 (uploads into a connection), item 3 (properties): phase 4 done; phase 5 C0 refactoring
  (`FileJobs` split), contracts, both transfer sides, `TransferJobs` and the transfer route, client across places;
  each built by subagents in parallel on one contract (one stop on the account's 5-hour limit, resumed); phase 5
  item 3 (retry, task history, size limit): phase 5 done; phase 6 C0 (FolderPolicy refactoring), C1 (rules
  table, LocalDB checked), C2/C3 (enforcement, closing two access gaps), C5 (management routes and card), C4 (rules follow moves):
  phase 6 done.
- 2026-11-13 (third session): connections phase 1 finished (services, access rules, Data Protection of credentials,
  feature note `connections.md`); phase 2 (owner: app secret on the page, write-only).
- 2026-11-13 (second session): api-v1 phase 1 committed; connections started out of order at the owner request
  (plans 1 to 4 later); connections phase 0 done without a real Dropbox account (SDK run against a fake handler).
- 2026-11-13: epic started; status board added; api-v1 phase 0 done (`api-versioning.md`, linked from `api-routes.md`
  and AGENTS.md); namespace-rename plan added as plan 3 at the owner's request; api-v1 phase 1 done (`ApiRoutes`,
  OpenAPI and `routes.txt` identical).

## Decisions (taken with the owner, 2026-11-13)

| Subject | Decision |
|---|---|
| Versioning | everything under `/api/v1` (URL path). Old `/api/...` paths stay as aliases until SFE 2.0, with a `Deprecation` header, out of the OpenAPI document |
| Third-party sign-in | personal access tokens, acting as the account, never with more rights |
| Local mode | same limits as the web client: tokens only from this computer and for administrators; connections work (outbound calls) |
| C# library | `src/Sfe.Client`: generated low-level client under a hand-written facade; a `.nupkg` among the release artifacts, **not** published to nuget.org; the facade covers files and connections (with search, tags, properties, trash, zip), the rest through the generated client |
| Connection model | separate locations beside the library, each with its own tree and path space; the library's disk code is not rewritten |
| Functions on connections | browse and file management, previews in the viewer, index/search/tags, folder rules |
| Remote media | index from the provider's metadata and change cursor; the provider's thumbnails; content streamed through SFE; a bounded local cache for what ffmpeg must read |
| Tags | the library's tags; a connection's index rows live in the library's database, in their own tables |
| Search | the location being browsed, plus an "Everywhere" scope with results grouped by location, never one merged list |
| Levels | account: private to its owner (administrators see it exists and can remove it, never its content). Library: that library's members, by groups and folder rules. Server: offered to every library, each library's administrators set groups and rules |
| Dropbox app | the server administrator registers their own Dropbox app (key and secret in the settings); redirect flow when the server's address is registered there, else the copy-the-code flow (PKCE) |
| New rights | Administrators only by default (`since`), never guests |
| Namespace (2026-11-13) | C# namespaces become `Otools.Net.Sfe.*`, before the client library is released (the `Sfe.Client` names in plan 4 mean namespace `Otools.Net.Sfe.Client`). Namespaces only: projects, folders, assemblies and the executable keep their `Sfe.*` names |

## Rules that apply to every plan

- Every commit of the epic updates the status board above (and the plan's checklist) before it is committed.

- Production: every change to the databases, settings or stored files ships with its tested upgrade path
  ([production-upgrades](production-upgrades.md)), on both providers ([database](database.md)).
- Every route names its right; new functions get one, with `since` ([rights](../features/rights.md)).
- Tests with every phase ([testing](testing.md)); user docs with screenshots ([user-docs](user-docs.md)); a line under
  `## Unreleased` per commit ([versioning-and-changelog](versioning-and-changelog.md)).
- Scale ([scale](scale.md)), fully local client ([fully-local](fully-local.md)), security rules
  ([security](security.md)), size limits ([readability](readability.md)).
- A fact learnt on the way goes in the feature note of its function (`agents/docs/features/`), not only in the plan.

## Not in this epic

- An OAuth 2 authorization server in SFE (apps asking users for consent). The token model keeps room for it.
- CORS: browser apps served from another site calling the API.
- Publishing the library to nuget.org.
- Mounting a connection inside the library's folder tree.
- Trickplay, fingerprints, duplicates and similar videos on connection files.
- Providers other than Dropbox (the architecture is checked against them, none is built).
