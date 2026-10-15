# Scroll progress

Code: `ScrollProgressComponent` (in `sfe-listing`, `src/app/library/items/`), `ListingStore.total`;
server `FileSearchService.CountLimit`.

- Counts the children of `.content > .folders` / `.files` whose top is above the screen bottom (binary search, reading
  order), on scroll/resize (rAF) and after items render.
- Total = `ListingStore.total` (browse `total`; search `total`/`totalCapped` from its first page only,
  `FileSearchService.CountLimit` = `Take(10001).Count`), pages of `store.pageSize()`.
