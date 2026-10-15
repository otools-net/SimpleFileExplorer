# Search sort and keyset paging

Code: `src/Sfe.Core/Search/FileSearchService.cs` (`ReadAsync`, `CountLimit`), `SfeDatabase.Configure`; client
`library/library-route.ts` (`currentSort`, `SEARCH_SORTS`).

- `SortField.Path` (search default; browse treats it as `Name`) or name/modified/size/type, ties by ascending
  `FullName` in both directions.
- Keyset cursor: bare path for `Path`, else `{value}/{path}` (the value has no `/`; a file without extension has
  `Extension` = `""`); the client must page with the sort it started with.
- Client: `route.sort` `''` = default, `currentSort` in `library/library-route.ts`.
- `Duration`/`Resolution` (search only, like `Path`: `SEARCH_SORTS`; browse falls back to name) read two parts
  (`FileSearchService.ReadAsync`): files with a value `> 0` by it (`IX_Files_Duration`, `IX_Files_Pixels`), then the
  rest by ascending path in both directions; their cursor is `/{path}` (empty value) once in the second part.
- `Files.Pixels` = Width × Height is a **virtual computed column** (EF never writes it, SQL Server
  `CAST(... AS bigint)`).
- The match count (`Take(10001).Count`) is unordered on purpose: EF's `RowLimitingOperationWithoutOrderByWarning` is
  ignored in `SfeDatabase.Configure` (even `ORDER BY (SELECT 1)` makes SQLite sort every match), so nothing warns
  about an unordered page any more: order every `Take`.
