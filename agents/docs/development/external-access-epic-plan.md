# Plan (epic): versioned API, access tokens, C# client library, external connections

Files: the six plans below; code named in each. This file is the **overview**: what was decided, in what order the
plans run, what none of them does. Delete it with the last plan (see [AGENTS.md](../../../AGENTS.md), "Keeping these
notes current"), moving the decisions that stay true into the feature notes.

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
| 1 | [api-v1-plan](api-v1-plan.md) | every route under `/api/v1`, old paths as aliases, compatibility net | |
| 2 | [api-tokens-plan](api-tokens-plan.md) | personal access tokens (`Authorization: Bearer`) | 1 |
| 3 | [namespace-rename-plan](namespace-rename-plan.md) | C# namespaces `Sfe.*` become `Otools.Net.Sfe.*` (refactoring) | |
| 4 | [client-library-plan](client-library-plan.md) | `Otools.Net.Sfe.Client` package over the v1 API | 1, 2, 3 |
| 5 | [connections-plan](connections-plan.md) | providers, Dropbox, browse, file management, transfers, rules | 1 (4 for its last phase) |
| 6 | [connections-index-plan](connections-index-plan.md) | index, search, tags and previews of connection files | 5 |

**A session takes the first unticked item of the first unfinished plan**, reads that plan's phase section, and ticks
the item in the commit that does it (as [readability-refactoring-plan](readability-refactoring-plan.md) does).

## Status board (done / next)

Updated in every commit of the epic, so a new session knows where to resume without reading history. The detail is
in each plan's `## Progress` checklist; this board is the summary.

| # | Plan | State | Done | Next |
|---|---|---|---|---|
| 1 | api-v1 | in progress | phase 0 (rules: [api-versioning](api-versioning.md)); phase 1 (`ApiRoutes`, base still `/api`) | phase 2: `LegacyApiPaths` + `ApiRoutes.Base = "/api/v1"` in one commit |
| 2 | api-tokens | not started | | |
| 3 | namespace-rename | not started | plan written; scope: namespaces only (projects, assemblies, exe keep their names) | phase 1: `Sfe.Core` namespaces |
| 4 | client-library | not started | | |
| 5 | connections | in progress (started before plans 1 to 4 at the owner request; its routes are mapped through `ApiRoutes`, so they follow the base) | phase 0 (results in the plan: `Dropbox.Api` SDK, twin rule table, capabilities); phase 1 (interfaces, `RemotePaths`, `OutboundPolicy`, `FakeProvider`; tables and migration `Connections`; rights, settings, `api/config`; `ConnectionService`, `ConnectionSharing`, `ConnectionAccess`, credentials protected, audit lines); phase 2 items 1 to 3 (`DropboxProvider` sign-in, app key setting, write-only app secret, provider routes; authorize, callback, complete; reauthorize) | phase 2 item 4: the client pages (see [connections-handover](connections-handover.md)) |
| 6 | connections-index | not started | | |

**Session log** (newest first; one line per session: date, what was done, anything left half-way):

- 2026-11-13 (third session): connections phase 1 finished (services, access rules, Data Protection of credentials,
  feature note `connections.md`); phase 2 items 1 (owner: app secret on the page, write-only) to 3.
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
