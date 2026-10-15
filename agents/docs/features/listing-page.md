# Listing page (state, navigation, dialogs, infinite scroll)

Code: `src/app/library/` (`LibraryPageComponent`, `ListingStore`, `library-route.ts`, Dialogs registry, `items/`
(`ListingComponent`), `topbar/`, `location/`, `sidebar/`, `viewer/`); server `src/Sfe.Core/Library/LibraryBrowser.cs`.
User docs: `docs/user-guide/`.

- `LibraryPageComponent` loads `/api/config` (`ConfigService`), then turns each `NavigationEnd` into
  `ListingStore.route` + `load()`; on an imperative `NavigationStart` it writes `scrollY`/`loaded` into the current
  `history.state`, and a `popstate` navigation restores them (`restoredState`; the page opened with
  `initialHistoryState`, read before the router replaces it). Details: [position-restore.md](position-restore.md).
- `ListingStore` (root) holds route, items, pages, siblings, selection and the `generation` signal: services keyed on a
  listing (`CoversService`, `TrickplayHoverService`) reset in an `effect` on it.
- Navigate with `store.navigate(changes, { replace })` or a `routerLink="/"` + `store.browseParams(path)` (keeps the
  sort).
- Item objects are replaced, never mutated (`applyFileTags`/`applyTagChanged`/`applyTagDeleted`): track lists by
  `path`.
- Dialogs register themselves in `Dialogs` (`registerDialog`) so any part opens them: `dialogs.fileTags()?.open(item)`.
- Infinite scroll: the sentinel's `IntersectionObserver` in `ListingComponent` re-observes after the next render
  (`afterNextRender`), otherwise it would still see the sentinel and load every page.
- Browse reuses a sorted snapshot ([scale.md](../development/scale.md)); sorts: [search-sort.md](search-sort.md).
