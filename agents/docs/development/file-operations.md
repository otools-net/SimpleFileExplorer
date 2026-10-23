# File operations: rules for changing the library's files

The rules every change to the library's files keeps: new folders and files, rename, move, copy, delete, uploads
(built), the picture editor's saves, duplicate removal, and the text editor
([text-editor.md](../features/text-editor.md): overwriting a file in place). Folder access:
[folder-access.md](../features/folder-access.md); rights: [right-kinds.md](../features/right-kinds.md).

## Rights and checks

- Rights (`files.createFolders`, `files.rename`, `files.move`, `files.delete`, `files.createFiles`… one per operation),
  each added in the phase that used it: `Kind = Write`, `AtFolder = Write`,
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
- Fail-safe order (disk and access database share no transaction), built in `FolderRuleService` (1.52.0):
  1. `BeginMoveAsync(tenant, from, to, keepAccess, changedBy)` copies the subtree's rules to the new paths in one
     `SaveChanges` (both protected; bumps `AccessChanges`) and returns a `FolderRuleMove` that **holds the space's
     lock**: `await using` it;
  2. rename or move on disk;
  3. `move.CompleteAsync()` deletes the old paths' rules, or `move.AbortAsync()` deletes the copies (and pins).
  Disposing without either (an exception after the disk changed) only releases the lock: both sets stay. A crash
  between steps leaves both sets: never less protection, only stale rules ("folder not found"). Rule edits wait for
  the lock; the 250-rule cap doesn't apply to the copies.
- Case-only renames (`PathComparer` equal) rewrite the rows in place; abort writes the old spelling back.
- Refused: the root, into itself, the same path, and any existing rule at or below the destination (stale rules of a
  folder that is gone would apply to the moved one: `Conflict`, remove or re-point them first).
- Inheritance follows the new place: explicit rules move, inherited access comes from the new parent. Before moving,
  `CompareMoveAsync(tenant, from, to)` gives each group, and each account with rules, whose level at the folder
  changes (`FolderLevelChange`; null = an account without a say): anyone with `Gains` (Hidden → Read, Read → Write,
  Guests included; an account at Hidden adds nothing) needs an explicit confirmation naming who gains what;
  **Keep current access** (`keepAccess: true`) adds rules on the moved folder pinning each changed principal's
  previous level (an account without a say before gets Hidden). Comparing at the moved folder is enough: everything
  below either has its own rule (moved with it) or inherits the folder's level.

## Other data keyed by path (moved by the same operation)

- Quick links (`QuickLinks.Path`), the index (`Directories`/`Files.FullName`, so tags stay on their files),
  `Scan:ExcludedFolders`, task schedules' `Path`, the editor's `OutputFolder` when inside, cache keys (thumbnails,
  trickplay, fingerprints). Use the copy-then-delete order wherever the data protects something.

## Background jobs

- A job started by a user carries its `AccessSubject` and **re-evaluates the policy per item when it runs** (rights
  can change meanwhile; no check-then-act gap). Scans, schedules and similar-video passes stay system jobs.
- Every change is logged with the account ([logs.md](../features/logs.md)). `TaskSchedule` gets `CreatedById` only when
  schedules can change files.
