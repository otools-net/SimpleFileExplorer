# Rename several at once

Code: `app/files/batch-rename.dialog.ts` (`planRenames`, `BatchRenameDialog`, slot `Dialogs.batchRename`),
`ItemMenu.renameSelection` (menu, F2), the selection bar's Rename. Tests: `comfort.spec.ts`. User docs:
`docs/user-guide/managing-files.md#rename-several-at-once`.

- Client only: the renames are the existing `POST api/files/rename`, one after the other (the server checks each).
- `planRenames` (pure): find and replace in the name without its extension (literal text, `gi` unless Match case),
  or numbered names (`#`… = the number padded to as many digits; no `#`: ` n` added). Problems: no name left (also
  only the extension), `/` or `\`, two alike (case-insensitive), taken by another listed item of the folder, or the
  current name of another item renamed (no swaps: two steps).
- Only in a folder, not a search (names are compared with the folder's loaded items; the server catches the rest).
- Failures are listed in the dialog, which stays open with the renamed ones under their new names; the toast's Undo
  renames back in reverse order.
