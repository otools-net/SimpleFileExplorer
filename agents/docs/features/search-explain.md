# Search explanation

Code: `src/app/library/search-explain/`: `search-query.ts` (parsing), `search-explain.ts` (wording),
`search-segments.ts` (pieces, `slot` / `phrase`), `search-notes.ts` (ignored terms); `search-explain.spec.ts`;
server parser `src/Sfe.Core/Search/SearchQuery.cs`.

- `search-query.ts` re-implements `SearchQuery.Parse` client-side (same quirks: `*` only at the start,
  `-e:`/`-s:`/`-w:` negate the filter, invalid sizes dropped) to word the query live as `Segment`s; a parser change
  needs the same change there (and in `search-explain.spec.ts`).
- Each sentence is one `$localize` message with `slot()` placeholders; `phrase()` puts the terms and logical words
  (`OPS`) into them, so a translation can reorder them ([translations](../development/translations.md)).
- It reads the box as typed (`SearchBoxState.text`), not the route; off with `Search:Explain` (`/api/config`
  `searchExplain`); open state in `localStorage`.
