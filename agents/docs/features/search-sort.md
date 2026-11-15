# Search sort and keyset paging

Code: `src/Sfe.Core/Search/SearchPages.cs` (`ReadAsync`, `Order`, `After`: generic over `ISearchableFile<T>`),
`SearchCursor`, `FileSearchService` (`CountLimit`), `Connections/Indexing/RemoteIndexSearch`, `SfeDatabase.Configure`;
client `library/library-route.ts` (`currentSort`, `SEARCH_SORTS`). Tests: `SearchCombinationTests`,
`RemoteIndexSearchTests`, `SearchSqlTests`.

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
- The keyset is shared by the library's files and a connection's (`SearchPages`, `ISearchableFile<T>`): the name
  sort reads `T.SortName` (`FileEntry.Name`; `RemoteFile.NameKey`, the listings' natural key). A connection's rows
  without a `NameKey` yet come after the others by key (lowered path), as unknown durations do (`/{path}` cursor);
  `SortNameMayBeMissing` turns that second part on.
- **The library's SQL must not change by accident**: `SearchSqlTests` (+ `…SqlServerTests`, LocalDB) runs a set of
  searches (terms, sorts, three keyset pages each, a folder, folder rules) and compares every command's text with
  `tests/samples/search-sql/{sqlite,sqlserver}.sql`. Parameter names come from the captured variables' names
  (`ids`, `hiddenIds`, `text`, `path`…): renaming one changes the SQL. A change meant to alter it rewrites the files
  with `SFE_SEARCH_SQL=write dotnet test tests/Sfe.Tests --filter FullyQualifiedName~SearchSqlTests`.
