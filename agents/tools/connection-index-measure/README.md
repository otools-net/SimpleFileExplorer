# Connection index measurements

Measures the index of connection files planned in
[connections-index-plan](../../docs/development/connections-index-plan.md) (phase 0) without the app: a throwaway
`RemoteFiles` / `RemoteFileTags` pair (the planned columns and indexes) filled with synthetic rows in batches as a
first sync would write them, then the search's typical queries (name contains, type plus size, a tag, a folder, sorted,
a keyset page of 100, the match count capped at 10 001) on **SQLite and SQL Server LocalDB**. The predicates are
written once over `ISearchableFile<T>` (the shape chosen for the search) and the tool checks they give the same SQL as
predicates written on the entity. .NET 10 SDK only (a file-based app; packages restored on the first run).

```sh
# the plan's numbers: 3 million rows (80% in connection 1), both providers, fast writer: about 25 minutes
# (SQLite's fill 19, SQL Server's 4): run it in the background (agents/docs/environment/long-runs.md)
dotnet run -c Release agents/tools/connection-index-measure/Measure.cs -- --rows 3000000 --writer ado

# the sync's writer as the app writes today (EF: Add, SaveChanges, ChangeTracker.Clear per batch)
dotnet run -c Release agents/tools/connection-index-measure/Measure.cs -- --rows 200000 --writer ef

# one provider, other batch size, keep the databases to look at them
dotnet run -c Release agents/tools/connection-index-measure/Measure.cs -- --provider sqlite --batch 2000 --keep
```

- Never touches a real database: SQLite files in `<tmp>/sfe-connection-index-measure/`, LocalDB databases
  `sfe_measure_<random>`; both removed at the end (unless `--keep`; a run stopped half way leaves them: delete the
  folder, drop the database with `System.Data.SqlClient` from PowerShell, see
  [dev-machine](../../docs/environment/dev-machine.md)).
- Prints the insert rate per tenth of the rows, the managed heap and working set (bounded memory), the database size,
  then a Markdown table of median times (5 runs after a warm-up). The SQL of each query goes to
  `<tmp>/sfe-connection-index-measure/queries-<provider>.sql`, SQLite's query plans next to it (`.plans.txt`).
- `--writer ado`: SQLite one prepared `INSERT` per batch transaction, SQL Server `SqlBulkCopy` per batch; tags then
  added by one `INSERT … SELECT` (same share as the EF writer: 5% of files, a fifth of them "holiday", tags 1-3).
- Rows: folders of 1 000 files under `/Photos|Videos|Music|Documents|Archive/<year>/<word> <n>`, names of two words
  from 64 and a number, extensions weighted like a media library, a tenth of pictures and videos with a resolution
  and of videos with a duration. Connection 2 gets every fifth batch.
