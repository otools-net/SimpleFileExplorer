# Folder access (design; not built yet)

Status: **designed, phases 2–4 to build** (phase 1, the right kinds, is done: [right-kinds.md](right-kinds.md)).
Planned code: `Core/Access/FolderPolicy.cs`, `FolderRuleService.cs`, `Data/Entities/Access/FolderRule.cs`,
`Api/Security/RightAuthorization.cs` (`RequireRightAt`, `FolderScoped`), `CurrentAccess`. What file management must
do with it: [file-operations.md](../development/file-operations.md). Prior art: Nextcloud group-folder ACLs (inherit
/ allow / deny per folder), NTFS (nearest rule wins), Seafile (read-only / read-write per sub-folder).

## Model

- **What** a subject may do = its groups' space rights (today); **where** = folder levels set by folder rules. A right
  applies at a path only if both allow it: `level(path) >= right.AtFolder` (null `AtFolder`: everywhere).
- A rule: at root-relative path P, a **principal** (a group of the space, Guests included, or one account) has a
  level `Hidden`, `Read` or `Write`; no rule = inherit. Stored normalised (`LibraryPaths.NormalizeRelative`).
- **Nearest rule wins per principal.** A group without a rule above P is unrestricted (`Write`: today's behaviour, so
  installs without rules are unchanged); an account without a rule above P has no opinion.
- **Union across principals** (user's choice), **with rights paired to the principal granting them**: rights at P =
  ∪ over groups g with `level_g(P) >= AtFolder` of g's rights, plus, when the account's own rule allows it, all its
  groups' rights. (A plain union of rights and levels would let A = {delete; /X Read} + B = {browse; /X Write} delete
  in /X.) So an account rule only adds; a folder is hidden from an account only if none of its groups can see it.
- Server administrators: everything everywhere. Space administrators (built-in Administrators) are unrestricted until
  a rule names them.
- **Path-only folders:** a folder the subject can't browse but holding a deeper browsable rule is listed with only the
  subfolders leading there; nothing else (files, counts, covers, tags, search results) and 404 for the rest.
- Tagging needs `Read` only (user's choice): folder levels govern files on disk; index data follows readability.

## Compiled policy (`FolderPolicy`)

- Per subject, the rules of all its principals compile into one nearest-wins table: the root plus every path holding
  any of their rules, each with the subject's rights there and a path-only flag. Exact: between the deepest table
  entry Q above P and P no principal has a rule, so rights(P) = rights(Q).
- `Unrestricted` (no rules) is the fast path: no filtering, no SQL change.
- `At(path)`: walk up the segments in a `PathComparer` dictionary, O(depth). `HiddenChildren(folder)`,
  `PathOnlyChildren(folder)`: O(rules) (a child differs from its folder only if a rule names it or below).
- `Visible(right)` → `PathFilter` for index queries: prune entries equal to their parent so visibility alternates;
  filter = ∨ over visible v of `InTree(v) ∧ ¬∨ InTree(hidden children of v)` with
  `SearchPredicateBuilder.InTree` (sargable `LIKE` on `FullName` + the host's case check). Size O(rules): **cap of 250
  rules per space** (SQL Server's 2100 parameters).
- Cached in `EffectiveAccess` (new `Folders` member), invalidated by `AccessChanges.Version`; the `PathFilter` (full
  paths) built lazily per space from its `LibraryPaths`.

## Canonical paths (security)

- Check the **canonical** relative path: `LibraryPaths.TryResolve` (runs `GetFullPath`) then `ToRelative`, never the
  raw input: removes `..`, `\`, Windows trailing dots/spaces (`Private.`), case (via `PathComparer`).
- 8.3 names (`PRIVAT~1`) and links/junctions inside the root into a restricted folder: single-file routes also check
  `LibraryPaths.RealPath` mapped back inside the root and keep the lower level. Check whether `DirectoryIndexer`
  follows reparse points (if so, search under `/Public/link` must not show `/Private`). Hard links can't be caught.

## Data

- `FolderRules` (AccessDbContext, schema `SFE`): Id; TenantId (FK cascade); Path (260); GroupId (FK **no cascade**:
  `Tenants→Groups→FolderRules` + `Tenants→FolderRules` is two cascade paths, which SQL Server rejects, so
  `AccessService` deletes a group's rules itself); UserId (FK cascade); exactly one principal; Level (string, like
  `BuiltIn`); Created, CreatedById. Indexes TenantId, GroupId, UserId; not unique on path (host case rules, as
  `QuickLinks`).
- New table only: SQL Server migration in `AccessMigrations` + `AddMissingSqliteObjectsAsync` (check the check
  constraint, else enforce in the service). Upgrade test: an access database without the table, rights unchanged.

## Enforcement (phase 3)

- `AccessState.At(path)` (canonical) → `FolderAccess`. Conventions `.RequireRightAt(right, "path")` and
  `.FolderScoped()` add `FolderMetadata`; a test next to `EveryRouteNamesARight`: every route with a path parameter or
  body declares it. 404 when the subject can't browse the path (same as missing), 403 when visible but not allowed.
- One path: `files/raw`, `files/animated`, `thumbnails`, `trickplay/files`, `streaming/probe|play`, `file-tags` GET,
  `file-tags/viewed`, `files/links`, `index/folder`, `scan` POST, `tasks/{task}/start`.
- Shared links (`files/shared/{token}`, token = tenant, user, stamp, path; rights re-evaluated per request): add
  `At(token path)`, so a rule made after the link ends it.
- Lists (`file-tags` POST, `trickplay/states`, `browse/covers`, `me/quick-links` POST): every path; one hidden fails
  the request. Two paths: `duplicates/remove` (Write at Path, Read at Keep), `editor/images` (Read source, Write at
  `EditedImages.TargetFolder`), `missing-files/{id}/relink` (Read target).
- Listing `browse`: drop `HiddenChildren`, mark path-only ones (the shared `LibraryBrowser` snapshot stays);
  `browse/siblings`: skip hidden.
- Core gets a `PathFilter?` (null = unrestricted), never the request: `FileSearchService` (next to `InTree`; cursor
  unchanged), `DuplicateFinder` and `SimilarVideoFinder` read side (drop hidden members, groups under 2),
  `ScopeTagger`, `TagService.FileCountsAsync` (cache per policy fingerprint), `LibraryBrowser.Covers`,
  `RelinkService` listing, `QuickLinkService` (hidden = `Exists: false`, can't add).
- `scan` GET blanks `Path`/`CurrentDirectory` not visible. Not filtered (admin, see every name): `tasks/runs`,
  `logs`, `trickplay/activity`.

## Management (phase 4)

- Right `tenant.members` (same power as editing group rights); server administrators any space; space
  administrators only their space's accounts (`membersOfTenantOnly`).
- `GET/PUT api/access/folder-rules` (`{ path, groupId | userId, level | null }`, null deletes; with `Exists`),
  `GET api/access/folder-access?path=` (effective access of every group and account there), `GET api/access/folders`
  (unfiltered folder names for the managers' picker). Changes bump `AccessChanges`, are logged (`LogSecurityWarning`
  when Guests gain access).
- Client: a "Folder access" page of the space; "Access…" on a folder's menu; `browse` returns the folder's
  `FolderAccess` and `ConfigService.canAt(right, access)` replaces `can()` for path rights (`item-parts.ts`,
  `folder-item`, `location`, `selection-bar`, `viewer`). Rules whose folder is gone show "folder not found" and can be
  re-pointed (the move of [file-operations.md](../development/file-operations.md) without the disk step).

## Phases (one commit each, minor version)

2. Rules model + evaluation: entity, migration, SQLite, upgrade test; `FolderPolicy`; `FolderRuleService` with the
   subtree move and the before/after access diff; unit tests (nothing uses it, nothing creates rules: safe to ship).
3. Enforcement: conventions + route test, every route above, Core filters, canonical paths, links; security.md attack
   vectors (case, `Private.`, `PRIVAT~1`, junction into a hidden folder, shared link after a rule).
4. Management API and UI, `canAt`, re-pointing, `docs/` with screenshots.

Tests to write: nearest wins, union, rights paired per principal, account rules only add, path-only, unrestricted
path, the cap; `PathFilter` on SQLite equals `At()` in memory over random trees; a route matrix (hidden, read-only,
path-only folder × guest, user, space admin, server admin) with no leak in search, duplicates, similar, tag counts,
covers, siblings, scan status, quick links.
