# Undo (Ctrl+Z)

Code: `core/undo-history.ts` (`UndoHistory`), `files/undo-made.ts` (`MadeUndo`), `files/undo-texts.ts`, the Ctrl+Z
key in `app.ts`, the ⋮ menu's entry (`library/topbar/main-menu.component.*`); entries added by `name.dialog.ts`,
`batch-rename.dialog.ts`, `delete.dialog.ts`, `file-transfers.ts` (`undoOf`), `tracking/duplicates-panel.component.ts`.
Tests: `files/undo.spec.ts`.

## How it works

- One history per tab (a root service, in memory): the latest 20 actions, oldest forgotten. Nothing across reloads,
  tabs or devices, nothing done by others; no redo.
- `UndoHistory.add(entry)` keeps the entry and returns the toast's **Undo** button for it: the toast, Ctrl+Z (⌘Z)
  and the ⋮ menu's **Undo** take the same entry, so each is undone once (it leaves the list when asked). Undos run one
  after the other (a promise queue), in the order asked; an exception becomes "Could not undo it: …".
- An entry: `done` (the sentence of its toast, shown under Undo in the ⋮ menu; Ctrl+Z toasts "Undoing: …"), `hint`
  (what Undo does), `run` (says itself, in toasts, what it could not undo), `place`.
- `place`: an action on `PlaceFiles` (rename, batch rename) uses the place listed then; its paths mean another item in
  another place, so it is undone only while that place is listed again (`blocked`: the ⋮ menu greys Undo with the
  reason under it, Ctrl+Z toasts it; the entry stays). Entries whose places are known (moves: the job's places;
  deletes and duplicates: the library's trash) have none.
- Ctrl+Z is left to text fields, `contenteditable` (the text editor) and any `<dialog>` (viewer, picture editor,
  sheets): their own undo. With nothing to undo the key is not taken.

## What is undone

| Action | Undo | Refused when |
|---|---|---|
| Rename, batch rename | old names back | the name was taken meanwhile, the item is gone |
| Delete to the trash | restored from the trash | a scan runs there, the name was taken (server's 409) |
| Move (library, transfers) | moved back, one job per origin folder; folders merged left out | names taken: the panel asks |
| Copy into the library | copies to the trash (`MadeUndo`) | a copy changed since |
| New folder, new file (library) | to the trash (`MadeUndo`); no toast at creation, only the history | it changed since |
| Duplicate removed | the copy restored from the trash | as a restore |

- **Changed since** (`MadeUndo`): `GET files/properties` per item (one call each, items of the action only, never a
  folder's content); its date later than the action's end (the created item's `modified`, the job's `finished`: both
  the server's clock) plus 2 s means someone's work is in it, so it stays, said in a toast. A folder's date changes
  when an item is added or removed directly in it, not deeper.
- Offered only when the copies can come back (`MadeUndo.possible`): the right `files.delete` and a space keeping
  deleted items (`deletePermanently` off). Copies into a connection and new folders there have no undo (no SFE trash).
- Operations only of this tab (`FileTransfers.startedHere`): the session's jobs listed after a reload, or another tab's,
  end with no Undo.
