# Default tags of folders

Code: `src/Sfe.Core/Tagging/FolderDefaultTags.cs` (service, models), `FileTagRows.cs` (batched link writes, shared
with `ScopeTagger`), entity `Data/Entities/Tags/FolderDefaultTag.cs` (table `FolderDefaultTags`, since 1.81),
`DirectoryIndexer.NewEntry`, `PathMoves`, `Trash` (`TrashEntry.DefaultTags`), routes `api/folder-default-tags` in
`TagEndpoints`, `LibraryEndpoints.Browse` (`MarkAsync`); client `tags/default-tags-badge.component.ts` (icon + panel),
the "Default tags" section of `ScopeTagsDialog`, the icon in `folder-item.component.ts` and `location.component.ts`. Tests: `FolderDefaultTagsTests`,
`FolderDefaultTagsRouteTests`, `scope-tags.dialog.spec.ts` (`default tags`), `default-tags-badge.spec.ts`.
User docs: `docs/user-guide/tags.md#default-tags-of-a-folder`.

## Decisions (2026-10-22)

- A default tag applies to the folder **and its sub-folders** (inherited; the dialog lists the parents' after the own).
- A file gets them **once, when it enters the index** in that tree (scan, new file, upload, copy, restore: every path
  goes through `DirectoryIndexer`, `NewEntry` adds the links with the row) or **is moved into it** from a place
  without them (`ApplyAfterMoveAsync`: tags of the new parent chain minus the old one; a rename gives nothing).
  Files already indexed are never re-tagged by a scan: a tag taken off a file stays off.
- Adding one asks: new files only, or the files already there too; removing one asks whether those files keep it.
  "Too" runs `ScopeTagger.ChangeAsync` recursively **before** saving the default (so files indexed on the way are
  counted as added, and a 409 for a running scan leaves nothing half-done). Removing from the files removes the tag
  from every file of the tree, even ones tagged by hand or under a sub-folder that also has it as a default.
- Right `tags.folderDefaults` (Write, `AtFolder` Read, Administrators + Users, `since: 12`); changing the files
  already there filters by `tags.assign` like the scope route.

## How it works

- Rows are keyed by **root-relative path** (like `TaskSchedules.Path`), not by directory id: a folder needn't be
  indexed, and a folder dropped from the index (drive offline) keeps them. Unique `(Path, TagId)`; FK to the tag with
  cascade, and `TagService.DeleteAsync` deletes them explicitly and calls `Invalidate()`.
- The service keeps every row in memory (a few per library), reloaded after `Invalidate()` (a version counter stops a
  load racing a change from being kept). Lookups walk up the path: O(depth) per folder a scan visits.
- Browse: `listing.defaultTags` (own + inherited, `FolderDefaults`) and `item.defaultTags` (own, folders only), loaded
  with one `TagGraph` only when the page has some.
- Renames/moves: `MoveAsync(fromRel, toRel)` in `PathMoves` (folders), then `ApplyAfterMoveAsync` (files and folders).
  Trash: `TakeTreeAsync` into `TrashEntry.DefaultTags` (JSON of `KeptDefaultTag`, paths relative to the folder), given
  back by `PutBackAsync` before the restored files are indexed; a permanent delete forgets them.
- Code adding tags after `IndexFilesAsync` must skip links that exist (the new row may have defaults already):
  `FileJobs` copy with `KeepTags` does since 1.81; `Trash`, `RelinkService`, `DuplicateRemover` already did.
- Shown as an icon (`DefaultTagsBadgeComponent`, sprite `tag-auto`: a smaller tag and a filled sparkle, so it isn't
  taken for the `tag` button that tags the files; count when more than one), never as text (names were cut off): its
  panel lists them all. A click or tap toggles it; **no hover opening** (tried, the user found it excessive,
  2026-10-23). Esc, a pointerdown outside, a scroll or a resize close it. The panel is a `popover="manual"` shown in the
  top layer (`showPopover`): a plain fixed panel was placed relative to `.folder-actions`, whose hover `translate`
  makes it the containing block, and cut by the card. It is placed by the component through `--pop-top`/`--pop-bottom`/`--pop-left`; on phones it is a
  phone sheet ([frontend.md](../development/frontend.md)), its layer holding the backdrop.
- On a card the icon sits next to the item count over the pictures, in `.folder-cover-marks`: a box outside the link
  (a button can't be in it) that lets the pointer through but on the icon; the count moved there too, its hint is now
  the link's. On a line it sits in `.folder-actions` (after the count), which fades its children one by one so the
  icon stays visible (lines get more `padding-right`).
- Not done: default tags in the admin page (a list of every folder having some), a scan re-applying them on demand.
