# Folder access

Status: built (1.51–1.54): right kinds ([right-kinds.md](right-kinds.md)), rules, evaluation, moves, enforcement on
every route, management API and the space page's **Folder access** card. What file management must still do with it:
[file-operations.md](../development/file-operations.md). Code: `Core/Access/FolderPolicy.cs` (`FolderPolicy`,
`FolderPrincipal`, `FolderAccess`, `PathFilter`, `PathFilterQueries.WhereVisible`), `FolderRuleService.cs`
(`FolderRuleMove`), `RightsEvaluator` (`EffectiveAccess.Folders`/`At`/`FilterFor`), `Data/Entities/Access/FolderRule.cs`,
`AccessMigrations/*_FolderRules`; `Api/Security/FolderChecks.cs` (`At`, `Check`, `Visible`, `Seen`,
`ListingFilterOf`, `FolderScoped`, `NotLibraryPaths`); `Core/Library/LibraryBrowser` (`ListingFilter`);
`Api/Endpoints/FolderRuleEndpoints.cs`; client `access/folder-access.component.ts`, `ListingStore.can`/`folderAccess`. Tests:
`FolderPolicyTests`, `FolderRuleServiceTests`, `FolderFilterIndexTests`, `FolderRulesUpgradeTests`,
`FolderAccessRouteTests`, `AccessTests.EveryRouteTakingAPathSaysHowFolderRulesApply`. What file management must do with it:
[file-operations.md](../development/file-operations.md). Prior art: Nextcloud group-folder ACLs (inherit / allow /
deny per folder), NTFS (nearest rule wins), Seafile (read-only / read-write per sub-folder).

## As built (phase 2)

- `RightsEvaluator` compiles the policy with the rights (cached together, `AccessChanges` invalidates both): one
  `FolderPrincipal` per group of the subject (its own allowed rights, unrestricted by default) plus one for the
  account (all its groups' rights, no say by default); visitors: the Guests group. No rule row = `Folders` null =
  unrestricted (no extra cost). Server administrators: always unrestricted.
- `EffectiveAccess.At(path)` intersects with `Rights` at each call, so `Sfe:Rights:Disabled` (applied after the
  cache) still wins; `Has(right)` stays "somewhere in the space" (what `RequireRight` checks).
- Two rows for one principal and path (concurrent edits) count as the stricter (`RightsEvaluator.RuleMap`).
- `FolderPolicy.ChildrenWithRules(folder)` gives the children whose access differs from the folder's (for listings);
  `AllowsThroughout(folder, right)` the check before changing a tree; `FilterFor(right)` gives a `PathFilter`
  (`Includes` in memory, `ToPredicate` for EF: every `InTree` shares the parameter of `fullName`, so the bodies combine
  without rewriting).
- `FolderRuleService.SetAsync(tenant, path, principal, level or null, changedBy)`: `NormalizeRelative` paths;
  refuses Windows names ending in a dot or a space, a group of another space, an account in no group of the space,
  more than `MaxRules` (250); one lock per space (`SemaphoreSlim`) shared with moves.
- Checked on LocalDB: the check constraint holds; deleting a group with rules is refused by the database (so
  `DeleteGroupAsync` deletes them first, inside an execution strategy); deleting a space cascades through both paths.

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

## Enforcement (phase 3, built in 1.53.0)

- As built: each handler checks (no endpoint filter): `state.At(paths, path)` (canonical: `TryResolve` then
  `ToRelative`, and `LibraryPaths.RealRelative` for links/8.3 names, the stricter wins via `FolderAccess.And`;
  nothing read from disk when unrestricted) and `state.Check(paths, path, right)` → `PathVerdict` (404 not seen,
  403 seen but not allowed). `.FolderScoped("how")` / `.NotLibraryPaths()` add `FolderMetadata`;
  `EveryRouteTakingAPathSaysHowFolderRulesApply` finds routes by a string `path`/`paths` parameter or an `Sfe.*`
  record body with `Path`/`Paths`/`Keep` (services and `HttpRequest` have a `Path` too: not counted).
- `RealRelative` measures from the root's own real path (a root reached through a junction would otherwise put every
  link target "outside").
- Scans follow folder links to siblings (only links back to an ancestor are skipped), so index rows can lead through a
  link into a hidden folder: `Seen` re-checks the items of search, duplicates and similar-video pages for restricted
  subjects (counts and totals may still include them).
- Path-only folders list only the children leading to what is seen (`ListingFilter.Only`); listings, siblings and
  covers of a path-only folder show nothing of its own. The listing's paging works on the filtered list
  (`CachedListing.Positions`, built once per snapshot).
- The planned design (kept for reference):
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

## Management (phase 4, built in 1.54.0)

- As built: `FolderRuleEndpoints` (`GET/PUT api/access/folder-rules`, `POST api/access/folder-rules/repoint`,
  `GET api/access/folder-access`, `GET api/access/folders`), right `tenant.members`, `tenant` for other spaces (server
  administrators), like `AccessEndpoints`; another space's paths come from its `TenantHost` (`TenantRegistry`).
  Re-pointing refuses a `from` that still exists (409) and is `BeginMoveAsync` + `CompleteAsync` without a disk step.
  Rules giving Guests Read/Write and re-points are `LogSecurityWarning`, other changes `LogInformation`.
- `GET api/browse` carries `access` (`FolderAccessView`: rights here, `pathOnly`, `children` that differ, by name) for
  restricted subjects only; `ListingStore.can(right, item?)` narrows `config.can` with it (the tag button, folder
  tagging, the location bar, the selection bar, the viewer's tags/download/edit). Searches carry none: the server
  refuses there. Children are matched by name, then ignoring case (the rule's spelling can differ on Windows).
- The card (`sfe-folder-access`, last on the space page so the cards before keep their order for the specs): rules
  grouped by folder, a level per rule (`Inherit (remove)` deletes it), **New rule** with the unfiltered picker,
  **Who has access** (`folder-access`: accounts and visitors with `sees` / `changesFiles`, computed with
  `RightsEvaluator` per account), **Re-point…** for folders not found.
- Not built: an "Access…" item on a folder's menu in the listing (the card's report covers it).
- The planned design (kept for reference):

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

2. Done (1.52.0): rules model and evaluation, the subtree move and the before/after access comparison.
3. Done (1.53.0): enforcement on every route, Core filters, canonical and real paths, links, route test.
4. Done (1.54.0): management API and card, `ListingStore.can`, re-pointing, `docs/` with `folder-access.png`.

Tests to write: nearest wins, union, rights paired per principal, account rules only add, path-only, unrestricted
path, the cap; `PathFilter` on SQLite equals `At()` in memory over random trees; a route matrix (hidden, read-only,
path-only folder × guest, user, space admin, server admin) with no leak in search, duplicates, similar, tag counts,
covers, siblings, scan status, quick links.
