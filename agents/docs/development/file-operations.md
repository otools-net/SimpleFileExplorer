# File operations: rules for changing the library's files (planned)

Status: **requirements for the file-management feature** (create, rename, move, copy, delete), to build after folder
access ([folder-access.md](../features/folder-access.md)). Today only `files.edit` (a new file) and
`tracking.duplicatesRemove` (deletes a copy) write to the library. Rights: [right-kinds.md](../features/right-kinds.md).

## Rights and checks

- New rights (`files.create`, `files.rename`, `files.move`, `files.delete`, …): `Kind = Write`, `AtFolder = Write`,
  `GuestAllowed = false`, `DefaultGroups = []`, `since` (nobody gets them by upgrade). `folder:write` in
  `Sfe:Rights:Disabled` or the space's **Files read-only** switches them off with the others.
- Move: Write at the source and at the destination parent. Copy: Read at the source, Write at the destination.
  Overwrite: Write at the target. A clash with a hidden file answers "exists" and tells nothing else.
- Delete, rename or move a folder: Write on it **and** no rule of the subject's principals below it gives less than
  Write (O(rules)): nobody deletes or moves what they can't see or change.
- Paths go through the canonical check of folder access (`TryResolve` → `ToRelative`, `RealPath` for links and 8.3
  names) and `ServerFiles`.

## Rules follow folders renamed or moved in the app

- Every rename or move of a folder through SFE migrates the folder rules of that folder **and its whole subtree**, for
  every principal (also rules the person can't see): rules whose path is the folder or starts with `folder/`
  (`PathComparer`; `Photos2` is not under `Photos`) get the new prefix. Renaming a parent rewrites the deep rules.
  Case-only renames on Windows rewrite the stored path too. The root never moves.
- Fail-safe order (disk and access database share no transaction), `FolderRuleService`:
  1. `BeginMoveAsync(tenant, from, to)`: copy the subtree's rules to the new paths in one transaction (both protected;
     bump `AccessChanges`);
  2. rename or move on disk;
  3. `CompleteMoveAsync` deletes the old paths' rules, or `AbortMoveAsync` deletes the copies on failure.
  A crash between steps leaves both sets: never less protection, only stale rules ("folder not found"). A lock per
  space keeps rule edits and moves apart; the 250-rule cap doesn't apply to the copies.
- Inheritance follows the new place: explicit rules move, inherited access comes from the new parent. Before moving,
  compare the moved folder's effective access before/after for every group and account (`FolderPolicy`, no disk):
  anyone **gaining** access (Hidden → Read, Read → Write, Guests included) needs an explicit confirmation naming who
  gains what; **Keep current access** adds rules on the moved folder pinning each principal's previous level.

## Other data keyed by path (moved by the same operation)

- Quick links (`QuickLinks.Path`), the index (`Directories`/`Files.FullName`, so tags stay on their files),
  `Scan:ExcludedFolders`, task schedules' `Path`, the editor's `OutputFolder` when inside, cache keys (thumbnails,
  trickplay, fingerprints). Use the copy-then-delete order wherever the data protects something.

## Background jobs

- A job started by a user carries its `AccessSubject` and **re-evaluates the policy per item when it runs** (rights
  can change meanwhile; no check-then-act gap). Scans, schedules and similar-video passes stay system jobs.
- Every change is logged with the account ([logs.md](../features/logs.md)). `TaskSchedule` gets `CreatedById` only when
  schedules can change files.
