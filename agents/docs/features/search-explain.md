# Search explanation

Code: `src/app/library/search-explain/search-explain.ts` (+ `search-explain.spec.ts`); server parser
`src/Sfe.Core/Search/SearchQuery.cs`.

- `search-explain.ts` re-implements `SearchQuery.Parse` client-side (same quirks: `*` only at the start,
  `-e:`/`-s:`/`-w:` negate the filter, invalid sizes dropped) to word the query live as `Segment`s; a parser change
  needs the same change there (and in `search-explain.spec.ts`).
- It reads the box as typed (`SearchBoxState.text`), not the route; off with `Search:Explain` (`/api/config`
  `searchExplain`); open state in `localStorage`.
