# Context menu (every right click)

Code: `src/Sfe.Web/src/app/menus/context-menu.ts` (`ContextMenu`, `MenuTarget`, `MenuEntry`, `[sfeContextMenu]`,
`useContextMenu`, `sfe-context-menu` in `app.ts`), `files/item-menu.ts` (`ItemMenu`: listed files and folders),
`library/folder-menu.ts` (`FolderMenu`: the folder listed or the search, folder links),
`library/quick-links/quick-links.component.ts`, `library/topbar/search-box.component.ts` (recent searches). Styles:
`styles/file-management.css` (`item-menu*`), `styles/phone-sheets.css`. Plan and what's left:
[context-menu-plan](../development/context-menu-plan.md). Check in a real browser:
`agents/tools/visual-check/context-menus.mjs`.

## How it works

- One menu for the app, rendered by `sfe-context-menu` (root component). An element with a menu declares a
  `MenuTarget` (`{kind, key, name, entries(), touch?(), item?}`): `[sfeContextMenu]="target"`, or
  `useContextMenu(() => target)` in a component's constructor for its host. The innermost element wins (the outer
  ones see `defaultPrevented`).
- A right click on nothing with a menu: the page's menu, `ContextMenu.page` (the listing page sets
  `FolderMenu.target()`, clears it when left). Inside a `<dialog>` (viewer, sheets, editor) the browser's menu stays
  for now.
- The browser's menu is kept (`keepsBrowserMenu`): Shift held, a text field (`isTextField`: text inputs, textarea,
  contenteditable; not check boxes), a right click on selected text; and when the target has no entries.
- Touch: a target with `touch` (listed items) does it on a long press instead of the menu (select); other targets open
  the menu, a sheet at the bottom on phones. iOS sends no `contextmenu`: the ⋮ buttons.
- Keyboard: the ContextMenu key and Shift+F10 (`document:keydown`) open the focused element's menu under it (else the
  page's), not in text fields; the browser's own `contextmenu` that follows the key is swallowed for 800 ms (checked
  in Edge: one event after the ContextMenu key, none after Shift+F10).
- Entries: `run`; or `href` (+ `newTab`): rendered as a real `<a target="_blank" rel="noopener">`, so Open in new tab
  is a user gesture (no popup blocker) and middle click works; or `submenu` (shown in place, Back / Left arrow).
- Closing: click elsewhere, Esc/Tab (focus back to the trigger), scroll, resize, navigation; item menus also when the
  listing changes (`ItemMenu`, `ListingStore.generation`, skipping the effect's first run).

## Which menu (built so far)

| Where | Target | Entries |
|---|---|---|
| File tile / row, folder card | `file` / `folder` (`ItemMenu.target`) | Open, Open in new tab, Open containing folder (searching), then the file management entries; none of the Open ones for a selection of several |
| Listing background, counts, buttons of the page | `background` (`FolderMenu.target`) | Folder: New folder, New file, Upload files / a folder, Paste, Select all, Refresh, Tags of this folder, Scans and previews, quick link, Copy full path, Open in new tab, Properties. Search: Select all, Tag the results, Search again, Open in new tab, Copy link |
| Breadcrumbs, up / previous / next, a search result's folder | `folderLink` (`FolderMenu.linkTarget`) | Open, Open in new tab, Paste into, quick link, Copy full path, Properties |
| Quick link | `folderLink` / `fileLink` | Its folder's entries (or Show in its folder, Open in new tab, Copy full path, Properties), then Move up / down, Remove; only those when it's not found any more |
| Recent search | `historyEntry` | Search, Open in new tab, Remove from the history |

- Links: a file's `ItemActions.fileUrl` (its `view?path=` page for formats with one, else the raw file); a listing's
  `ListingStore.pageUrl(params)` (what RouterLink makes: `LocationStrategy.prepareExternalUrl`, so it holds under a
  virtual directory).
- A folder link knows only its path: no entry needing the folder's own rules (Paste into is checked by the server).
