# Search terms and their quirks

Code: `src/Sfe.Core/Search/` (`SearchQuery` (parser), `SearchPredicateBuilder`, `FileSearchService`); client mirror in
`src/app/library/search-explain/` ([search-explain.md](search-explain.md)). Tests: `SearchCombinationTests`. User
docs: `docs/reference/` (search syntax).

## Rule: every term is negatable

Every term the search accepts (words, `e:`, `s:`, `w:`/`h:`/`r:` (with `r:hr`/`r:lr`), `d:`, `type:`, `t:`,
`special:untagged`, any new one) must accept a leading `-` that finds exactly the files the term doesn't (pure
complement: `-w:1920` includes unknown resolutions). A new filter term goes through `SearchQuery.AddFilter`, so it gets
`SearchGroup.ExcludedFilters` for free; add it to `EveryTermAndItsNegationSplitTheVisibleFiles`.

## Expectations that are easy to get wrong

- `Files.Name` has **no extension** (use `e:` or a leading `*` full-path query).
- `*` only counts at the start of the query and then applies to every alternative.
- Several `e:` in a group are OR-ed, several `-e:` each excluded; `-r:WxH` is NOT(both sides), one `ExcludedFilters`
  group per negated token.
- Files with a hidden tag stay hidden unless the search names it or an ancestor.
- A malformed `-s:` / `-w:` is ignored like a malformed `s:`, not searched as a word (so is a full-path `*D:\Media`:
  `D:` is the duration prefix, as `E:`/`S:`/`H:`... are filters).
- `d:` without a unit is minutes, without an operator "about" (± half the unit typed; a clock ± 0.5 s), `d:=` the same
  whole second ([duration.md](duration.md)).
- `r:hr`/`r:lr` need the file's extension classified as image or video, and `r:hr` and `r:lr` are not complements
  (unknown resolutions are neither) ([resolution.md](resolution.md)).
- `type:` has no column: it is an OR of the `MediaClassifier` extension lists (`MediaKinds`), `type:other` the
  negation of all three; several OR-ed like `e:`.
- On SQLite, `LIKE` and `NOCASE` fold ASCII only: `русский` / `ESPAÑOL` don't find `Русский` / `Español` (SQL Server's
  CI collation does).
- Sorting and paging: [search-sort.md](search-sort.md).
