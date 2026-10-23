# Context menu (one app menu for every right-click): plan and progress (epic)

Code: `src/Sfe.Web/src/app/files/item-menu.ts` (`ItemMenu`, `sfe-item-menu`, `button[sfeItemMenu]`; becomes
`app/menus/context-menu.ts`), `library/viewer/viewer.component.ts` (its own ⋮ menu, `ViewerMenuEntry`),
`styles/file-management.css`, `styles/phone-sheets.css`. Related: [new-folder-and-rename](../features/new-folder-and-rename.md),
[selection-and-keys](../features/selection-and-keys.md), [frontend](frontend.md), [rights](../features/rights.md).

Goal: a right-click gives today either the app's item menu (listed files and folders only) or the browser's menu
(everywhere else). The app handles **every** right-click, and its menu offers **Open in new tab** wherever the target
is a file, a folder or a page, which replaces the browser's "Open link in new tab" users reach today with
Shift+right-click (`docs/user-guide/managing-files.md`). Client only: no server change, no new right, setting or
migration; every entry reuses an action that already has its right.

**Progress: 6/34 done. Next: Phase 1, file menu: Open, Open in new tab.**

## How to use this file

- Built over several sessions. **Starting:** read this file and take the first unticked item of the earliest open
  phase. **Ending:** update this file in the commit that does the work. Tick `[x]` with the commit's short hash, and
  mark half-done items `[~]` with one line on what is left. Add tasks found on the way to their phase, and update the
  progress line above.
- An item is ticked only when its tests pass (`npm test`, plus `dotnet test` if server code changed) and its
  `CHANGELOG.md` line is written (none for docs/tests only).
- Gotchas go in [Found while building](#found-while-building); lasting facts move to `agents/docs/features/context-menu.md`
  (indexed in `AGENTS.md`) when a phase is done. This file keeps the map and the checklist.

Status legend: `[ ]` to do, `[~]` started, `[x]` done (commit), `[-]` dropped (why).

## Decisions

- Editable fields and selected text keep the browser's menu (spellcheck, paste, copy selection).
- Shift+right-click stays the escape hatch to the browser's menu.
- Every surface is covered, in three phases (library page, viewers and player, the rest).
- Open in new tab is a real `<a href target="_blank" rel="noopener">` entry (as the viewer's menu already does), never
  `window.open`: it is a user gesture (no popup blocker), middle/Ctrl-click work, URLs stay relative (virtual directory).

## Rules: which menu opens

One resolver decides for each `contextmenu` event, in this order:

1. Shift held → the browser's menu, untouched.
2. Target editable (`input`, `textarea`, `select`, `[contenteditable]`, CodeMirror's editor) → the browser's menu.
3. Non-empty text selection and the click inside it → the browser's menu.
4. The element or an ancestor declares a target (`[sfeContextMenu]`) → `preventDefault`, the app menu for it.
5. Otherwise → `preventDefault`, the page menu of the active page (`pageTarget`). Modal dialogs, the folder picker and
   the picture editor canvas: no menu at all (native suppressed; their fields keep it under rule 2).

- Touch: a long press on a listed item keeps toggling selection (`fromTouch`); on any other target it opens the menu as
  a phone sheet. The player keeps its long-press fast play. iOS sends no `contextmenu`: ⋮ buttons stay the way in.
- Keyboard: the ContextMenu key and Shift+F10 open the focused element's menu at its rectangle (no handler today).

## Map: where each menu appears

| Surface (code) | Target | Menu |
|---|---|---|
| File tile / row (`library/items/file-tile.component.ts`) | file | File menu |
| Folder card / line (`folder-item.component.ts`) | folder | Folder menu |
| Item part of a multi-selection | selection | Today's entries on the selection; no Open / Open in new tab (popup blockers) |
| Search result's folder link (`item-parts.ts`) | folder link (today it opens the file's menu) | Folder link menu |
| Tag chip on an item (`item-parts.ts`, `tags/tag-chip.component.ts`) | tag | Search this tag · Add to search · Exclude (`-tag`) · Remove from this file (if allowed) · Edit tag (manage right) |
| Listing background, section titles, empty states (`listing.component.ts`) | background | Background menu |
| Library chrome: location header and bar, selection bar, sidebar, top bar buttons | page fallback | Background menu |
| Breadcrumbs (`location/location.component.ts`), folder nav, location strip | folder link | Folder link menu |
| Quick links (`quick-links/quick-links.component.ts`) | folder or file link | Link menu + Edit / Move up / Move down / Remove |
| Search history and suggestions (`topbar/search-box.component.ts`) | history entry | Search · Open in new tab (`?q=`) · Remove from history |
| Viewer stage: image, audio (`viewer/viewer.component.ts`) | file shown | File menu + Fit / Actual size, Pop out, External player; the viewer's ⋮ shows the same menu (`menuEntries` goes) |
| Video player surface, mouse (`player/video-player.component.ts`) | file + player | Play/Pause · Speed › · Loop · Picture-in-picture · Pop out · Copy link at this time, then the file menu |
| Text, Markdown, JSON, CSV, PDF viewers (`app/formats/`) | file viewed; Markdown links → file / folder / external link | File menu; JSON node + Copy path / value; CSV cell + Copy cell |
| EPUB (iframe, `formats/epub/epub-document.ts`) | file, hooked inside the rendition | File menu |
| `/view` and `/player` pages | file (folder link → folder link menu) | File menu |
| Duplicates / Similar / Missing rows (`tracking/tracking-parts.ts`) | file | File menu + Open containing folder; missing files: Relink / Forget / Copy path |
| Trash rows (`files/trash-section.component.ts`) | trashed item | Restore · Delete permanently · Open original folder · Open in new tab |
| Uploads and operations panel rows | job | Cancel / Retry · Open target folder · Open in new tab |
| Exclusions list, uploads' target folder link | folder link | Folder link menu |
| Main menu links, admin navigation, admin system links, brand | app link | Open · Open in new tab · Copy link address |
| Admin, logs, tasks, settings pages (non-link areas) | page | Page menu |
| Modal dialogs, folder picker, picture editor canvas | none | Nothing |

### Menu contents

- **File menu**: Open · Open in new tab (`view?path=` for formats with a page, else the raw file: the tile's `url()`) ·
  Open containing folder (search results, quick links, admin lists), then today's entries of `ItemMenu.entries()`
  with the same right checks.
- **Folder menu**: Open · Open in new tab (router URL from `store.browseParams`), then today's folder entries.
- **Folder link menu** (only a path, no `LibraryItem`): Open · Open in new tab · Paste into (clipboard not empty) ·
  Add/Remove quick link · Copy full path · Properties. No write action needing folder-aware rights.
- **Background menu**: folder listed: New folder (Shift+N) · New file · Upload files / folder · Paste (Ctrl+V) ·
  Select all (Ctrl+A) · Refresh · Tags of this folder · Default tags · Scans and previews · Add/Remove quick link ·
  Copy folder path · Open in new tab · Properties. Search listed: Select all · Tag the results · Copy link · Open in
  new tab. Each entry keeps its existing right check.
- **Page menu**: Back · Reload · Copy page link · Open in new tab.
- URLs: `config.links.view/file`; folders and searches through the Router (`createUrlTree(['/'], {queryParams:
  routeParams(...)})`, serialized); admin links from their route.

## Building blocks

Built in phase 0 (`app/menus/context-menu.ts`):

- `ContextMenu` (root service): `open` holds `{target, x, y, trigger}`; `page` is the page's own menu (a page sets it
  when shown, clears it when left); `fromEvent`, `fromPage`, `fromKey`, `showAt`, `toggle`, `close`.
- A `MenuTarget` brings its own entries: `{kind, key, name, entries(), touch?(), item?}`. `kind` is one of `file |
  folder | folderLink | fileLink | tag | background | page | job | trash | historyEntry | appLink` (the menu's
  `data-menu`); `key` makes a ⋮ button a toggle; `touch` replaces the menu on a long press (listed items: select).
  Each feature builds its targets (listed items: `ItemMenu.target(item)`, entries acting on the selection when the
  item is part of it), so the hub imports none of them and lazy pages add theirs.
- `MenuEntry`: `run`, or `href` + `newTab` (a real `<a>`), or `submenu` (shown in the menu's place with Back; Right /
  Left arrows). Classes stay `item-menu*` (`styles/file-management.css`), shared with the viewer's ⋮ menu for now.
- `[sfeContextMenu]="target"` on elements; `useContextMenu(() => target)` in a component's constructor for its host.
  Both register the element for the keyboard. The innermost element handles the event (`defaultPrevented` stops
  the outer ones and the page fallback).
- `sfe-context-menu` in `app.ts`: renders the menu, `document:contextmenu` → page fallback, `document:keydown` →
  ContextMenu key / Shift+F10 (the browser's own `contextmenu` that follows the key is swallowed for 800 ms).
- `ItemMenu` closes an open item menu when the listing changes (`generation`; not on its first run).

## Checklist

### Phase 0: groundwork

- [x] This note + AGENTS.md index line
- [x] Generic `ContextMenu` (`app/menus/context-menu.ts`) holding the open menu; `ItemMenu` stays as the provider of
  listed items' targets (`ItemMenu.target(item)`), no behavior change
- [x] Entry model: `href` / `newTab` entries and submenus
- [x] `[sfeContextMenu]` directive and `useContextMenu()` (item hosts) replacing the three host `(contextmenu)` bindings
- [x] `sfe-context-menu` in the app root: `document` listener, rules 1–4, page fallback (`ContextMenu.page`, unset
  until a page sets it, so the browser's menu stays there); rule 5's "no menu" places are phase 3
- [x] ContextMenu key / Shift+F10 open the focused element's menu; tests

### Phase 1: library page

- [ ] File menu: Open, Open in new tab (view vs raw URL), Open containing folder in search results; href tests
- [ ] Folder menu: Open, Open in new tab (router URL with sort, virtual directory); tests
- [ ] No Open / Open in new tab on a multi-selection; test
- [ ] Background menu (folder listed, search listed) with right checks; background, empty states, library chrome fall
  back to it
- [ ] Folder link menu: breadcrumbs, folder nav, location strip, search result folder link
- [ ] Quick links: link menu + Edit / Move up / Move down / Remove
- [ ] Search history and suggestions menu
- [ ] Touch: long press still selects items; other targets open the sheet; tests
- [ ] Phone sheet check at ≤ 640 px for every new menu
- [ ] Docs: `new-folder-and-rename.md` (outdated entry list and `hasActions`), `selection-and-keys.md`, a rule in
  `frontend.md` (every right-click goes through `ContextMenu`), new `features/context-menu.md`; user docs
  `docs/user-guide/managing-files.md` + screenshots; CHANGELOG

### Phase 2: viewers and player

- [ ] Viewer stage (image, audio): file menu + viewer entries
- [ ] Viewer ⋮ uses the shared menu; remove `menuEntries` / `ViewerMenuEntry`
- [ ] Video player surface (mouse): player entries + Speed submenu + file menu; touch fast play unchanged
- [ ] Text viewer (outside the editor), Markdown (link targets), JSON (Copy path / value), CSV (Copy cell), PDF
- [ ] EPUB: `contextmenu` hooked inside the rendition iframe
- [ ] `/view` and `/player` pages
- [ ] Tests for each; user docs + screenshots; CHANGELOG

### Phase 3: the rest

- [ ] Tag chips menu
- [ ] Duplicates / Similar / Missing rows
- [ ] Trash rows
- [ ] Uploads and operations panel rows (job menu)
- [ ] App links: main menu, admin navigation, admin system links, brand
- [ ] Page menu on admin, logs, tasks, settings pages
- [ ] No menu in modal dialogs, folder picker, picture editor canvas
- [ ] Tests; user docs + screenshots; CHANGELOG

### Closing

- [ ] Visual check of every row of the map, desktop and phone width ([visual-check](../environment/visual-check.md))
- [ ] Remove leftover Shift+right-click advice from titles and docs (keep the escape hatch mention)
- [ ] Epic marked done here; `edge` merged into `main` with a minor version bump

## Verification

- Vitest: resolver rules (Shift, editable, selection, declared target, page fallback, dialog suppression); entries per
  target kind and rights; Open in new tab hrefs (view vs raw, folder with sort, search, virtual directory); none on a
  multi-selection; touch long press still selects.
- `npm test` and `dotnet test` pass; visual check of the map on a scratch library
  ([running-the-app](../environment/running-the-app.md)), Shift+right-click and text fields still give the browser's
  menu.

## Found while building

- A root service's `effect` runs once when the service is first injected: `ItemMenu`'s "listing changed" effect
  closed the menu it had just opened until it skipped its first value.
- The initial bundle is already over its 600 kB budget (a build warning, 793 kB before this epic).
