# Scroll position restore and same-URL reloads

Code: `src/app/library/` (`LibraryPageComponent`, `ListingStore`, `items/listing-position.ts`), search box in
`topbar/`.

- **Same-URL navigation reloads** (`onSameUrlNavigation: 'reload'`): `load()` empties the list and scrolls to the top.
  The search box's `searchFor` skips its own query (Enter after a live search used to reset the scroll);
  `load(restore)` takes more pages while fewer than `loaded` came back (pages are capped by `MaxPageSize`).
- **Position restore:** the history state holds `scrollY`, `loaded` and `anchor`/`anchorTop` (the item at the top of
  the screen, `items/listing-position.ts`), written on leaving and 250 ms after scrolling stops: Chrome ignores a
  `replaceState` in `pagehide` on reload.
- `restorePosition` scrolls the anchor back (previews have other sizes after a reload) and holds it there 2.5 s while
  rows resize, until wheel/touch/key/pointer. Folder items carry `data-path` too.
