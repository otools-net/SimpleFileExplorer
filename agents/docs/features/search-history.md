# Search history

Code: `src/app/library/search-history.service.ts` (`SearchHistory`), the search box in `src/app/library/topbar/`.

- `SearchHistory` keeps the last searches in localStorage (`sfe.searchHistory`), per browser; the server only sends
  the size (`Search:HistorySize` → `/api/config` `searchHistory`, 0 = off).
- The search box shows them in the tag suggestion list while no `t:` term is at the caret, nothing highlighted at first
  (Enter still submits the box).
- It records on submit, on a pick, and on blur when the box holds the route's query (a live search).
