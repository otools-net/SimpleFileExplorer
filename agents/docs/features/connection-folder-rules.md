# Folder rules of connections

Code: Core `Data/Entities/Access/ConnectionFolderRule.cs`, `AccessMigrations/20261114103741_*`,
`Connections/ConnectionRuleService`, `ConnectionRuleChecks`, `ConnectionRuleModels`, `ConnectionPolicies`,
`ConnectionPolicyModels`, `ConnectionFolders`, `ConnectionAccess.FoldersAsync`, `RemoteSnapshot`,
`ConnectionRuleMoves`, `ConnectionRuleMove`, `ConnectionRuleMoveRows`, `ConnectionMoveComparison`,
`Transfers/RemoteTransferChecks`, `IConnectionJobAccess`; Access `FolderPolicy`, `FolderRuleMoves`; Api
`ConnectionFolderRuleEndpoints` (`…Services`, `…Contracts`), `OpenConnection` (`At`, `Check`, `CheckThroughout`,
`AnswerOf`), `ConnectionWriteChecks`, `ConnectionJobAccess`, `ConnectionMoves`, `ConnectionRuleIds`; client
`access/folder-access.component.ts`, `access/folder-rules-source.ts`, `PlaceTransfers.answer`,
`TransferDialog.ask(…, connection)`, `PlaceFiles.transfer` (`AccessQuestion`). Tests: `ConnectionFolderRuleDataTests`,
`ConnectionRuleServiceTests`, `ConnectionFolderRuleRouteTests`, `ConnectionPoliciesTests`, `RemoteListingFilterTests`,
`ConnectionFolderAccessRouteTests`, `ConnectionFolderAccessJobTests` (on `RuledConnection`),
`ConnectionRuleFollowingRouteTests`, `RemoteTransferEndpointTests`, `TransferJobsTests`, `folder-rules-source.spec.ts`,
`place-transfers.spec.ts`. The library's rules: [folder-access](folder-access.md); who may use a connection at all:
[connections](connections.md).

## Model (`ConnectionFolderRules`)

- A **twin table**, not a column of `FolderRules`, compiled by the same `FolderPolicy`: the library's queries read
  `FolderRules` by `TenantId` alone (one forgotten `ConnectionId == null` would let a connection's rule hide or open a
  library folder); a server connection has rules per library (key: tenant, connection, path); paths compare by the
  provider's rules (`FolderPolicy.Compile(principals, comparer)`), the library's by the host's.
- Columns: Id, ConnectionId, TenantId, Path (≤ 1000), GroupId xor UserId, Level, ItemId (the provider's id of the
  folder), Created, CreatedById. Tenant and user cascade; group and connection `ClientCascade` (removed by their
  services). SQL Server migration `20261114103741` in `AccessMigrations`, SQLite by `AddMissingSqliteObjectsAsync`.
- `ConnectionRuleService`: a library's rules of a connection it reaches, replaced at once; at most 250 per library and
  connection (its own count, apart from the library's); paths by the provider's case; **none on account-level
  connections**; a lock per connection; bumps `AccessChanges`. Removed with their group, account, connection, or the
  library's offer of a server connection.

## Evaluation (`ConnectionPolicies`)

- The `ConnectionGroup` row is the gate (`ConnectionAccess`): rules never open a connection to a group without one.
  `ConnectionPolicies` says where and what: one `FolderPrincipal` per group of the subject **with a row**, its own
  library rights, `DefaultLevel` = the row's level; plus the account (rights of those groups, no say without its own
  rule). Rules raise or lower (nearest wins), so a rule may raise a group above its row's level; `ReadOnly` caps every
  level at Read (rules too). Owner of an account-level one and server administrators: unrestricted (`Whole`), but for
  the cap.
- **A right counts only where the group granting it has the level**: group A (`files.delete`, row Read) + group B
  (Write, no delete) deletes nowhere. (Before phase 6, the best row level was checked against the library-wide
  rights, so B's level added to A's right; fixed before release.)
- Cached per subject, connection and provider comparison until `AccessChanges.Version`;
  `ConnectionService.UpdateAsync` and `RemoveAsync` bump it (read only, off).

## Enforcement

- `ConnectionFileServices.OpenAsync(write: true)` answers 403 when the route's right is allowed nowhere
  (`AllowsSomewhere`); then each route checks its paths **before any provider call**: hidden 404 (never told apart
  from missing), seen but refused 403.
- Single items (raw, thumbnails for restricted subjects, properties) are checked again at the provider's own
  `item.Path` (stricter wins: a name differing in case or folding). Delete, rename, move: the right at the item, its
  folder and throughout (`CheckThroughout`); a server-side copy needs `files.view` throughout the source; a name
  hidden from the session counts as taken (409 / item `exists`). Per-item routes (delete, transfer) answer per-item
  `notFound` / `forbidden`.
- Listings: `RemoteListings.ListAsync(…, ListingFilter)` filters the shared snapshot per page (offsets, `Total`,
  `DirectoryCount` of what is left). A window past the cap remembers the watched (hidden) names that leave it, with
  their places; a filter naming what wasn't watched reads the folder anew. A path-only folder (seen only to reach a
  visible one below) is not listed: its leading children are asked one by one (`GetAsync`).
  `DirectoryListing.Access` is filled for restricted subjects.
- Zips: chosen paths checked (404/403) at both steps; the walk skips what isn't viewable and downloadable without
  naming it in the left-out entry.
- Transfers: `TransferOpening` checks the target folder at once; the job's `RemoteTransferEndpoint` checks every item
  through `IConnectionJobAccess` (`ConnectionJobAccess`, read again when `AccessChanges.Version` moved): hidden items
  not found nor listed, folders without `files.view` left out, a hidden name taken (also for free names), a move's
  source folder holding a hidden one stays.

## Management

- `GET`/`PUT api/connections/{id}/folder-rules`, `GET …/folders?path=` (every folder, whatever rules apply to the
  caller): right `connections.library`, on the request's library only.
- Client: the library's folder-access card reused through a `FolderRulesSource` adapter (`folder-rules-source.ts`),
  opened from the connection's **Manage** › **Folder rules…**. No "Who has access" report for a connection.

## Following moves

- `rename` and a `transfer` move inside the connection go through `ConnectionMoves`, in the library's order: begin =
  copies of the rules at the new paths under the connection's lock, every library; the provider; complete = originals
  go, or abort = copies go. Abort only when the provider says nothing moved (not found, exists, invalid name, refused,
  not supported, too large, wait); any other failure leaves both sets (never less protection). A connection without
  any rule skips all of it (`HasRulesAsync`, one query per request).
- Rules of the request's library left where the folder goes: 409 `folder-rules` (item status `conflict`), as the
  library; another library's left there (their folder is gone) are removed on complete. Case-only renames rewrite in
  place. A keep-both move in a ruled connection finds the free name itself first (`PlaceAsync`, `name (2)`…, 20
  tries), so the rules never begin at a taken name.
- The keep-access question: compared as the library (`FolderLevelChange`), a group's default being its row level,
  accounts without a say, `ReadOnly` capping; only items passing the route's checks, only folders (a `GetAsync` for
  those with gains). Unanswered (`keepAccess` absent) → 409 `access-gains` with `gains` (the `AccessGain` of
  `api/files/jobs/check`); `true` pins the request's library, `false` moves. A rename never asks (same parent: nobody
  gains). Other libraries' rules follow with a `LogSecurityWarning` naming them.
- Complete records the moved folder's id (`ItemId`) on the rules at its new path and takes the provider's path (a
  numbered name). `PUT …/folder-rules` looks up the ids of rules sent without one (`ConnectionRuleIds`, best effort,
  50 per change, skipped while the connection waits).
- Moved out by a transfer: `RemoteTransferEndpoint.DeleteSourceAsync` of a folder calls `RemoteTransferSide.FolderGone`
  → `RemoveFolderAsync` (every library). A folder only deleted keeps its rules (shown as not found, to re-point).
- Renamed outside SFE: `ConnectionPolicy.RulePlaces` (the subject's rules by `ItemId`); `ConnectionFolders.At(path,
  itemId)` adds the access at the rule's place (`FolderAccess.And`, stricter). Applied only where the item's id is at
  hand: listing pages (`WithoutHiddenByIds`, counts lowered; `ViewOf(…, items)`), single items read
  (`OpenConnection.Check(asked, item, right)`), the transfer job's listings and gets. Not below such a folder (its
  children's ids aren't the rule's), not on write routes (no `GetAsync`).
- Not built: the "moved to X" mark of the management GET (no lookup by id in `IRemoteStorage`) and a manual Follow:
  the rule is re-pointed by hand. Automatic following comes with the change cursor of
  [connections-index-plan](../development/connections-index-plan.md).
