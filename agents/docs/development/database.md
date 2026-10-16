# Database: two providers

Code: `src/Sfe.Core/Data/` (`SfeDbContext`, `Migrations/`, `DatabaseInitializer`, `SfeDatabase`, `AccessDbContext`,
`AccessMigrations/`, `AccessDatabaseInitializer`), entities in `Data/Entities/`. Migration commands:
[commands.md](../environment/commands.md). Upgrade rules: [production-upgrades.md](production-upgrades.md).

- **Both providers must work.** SQL Server: add a migration (`Data/Migrations`, schema `SFE`). Migrations are
  shipped: only add new ones.
- SQLite has no migrations: `DatabaseInitializer.AddMissingSqliteObjectsAsync` must create new tables **and indexes**
  in existing files (it also sets WAL) and adds new **nullable** columns (`ALTER TABLE ADD COLUMN`, from the model;
  computed ones last, as `GENERATED ALWAYS AS (…) VIRTUAL`; `pragma_table_xinfo`, since `table_info` hides generated
  columns). Other column changes to existing tables are not handled on SQLite: make new columns nullable.
- Indexed strings need a max length on SQL Server; SQL Server caps paths at 260 (`SfeDbContext.MaxPathLength`).
- Schema: tables in schema `SFE` on SQL Server (`Directories`, `Files`, tags in `TagDefinitions`). Entities: one class
  per file in `Index/ Tags/ MissingFiles/ Tasks/ Access/`, all in namespace `Sfe.Core.Data`.
- `dotnet ef migrations add` writes CRLF (with BOM): normalise the new files and the snapshot to LF
  ([line-endings.md](../environment/line-endings.md)).
- The match count query is unordered on purpose and EF's `RowLimitingOperationWithoutOrderByWarning` is ignored: order
  every `Take` ([search-sort.md](../features/search-sort.md)).
- SQL Server uses `EnableRetryOnFailure`: an explicit transaction must run inside
  `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` (as `SettingRows`, `DeleteGroupAsync`), or it throws.
- SQL Server refuses two cascade paths to one table (e.g. `Tenants→Groups→FolderRules` and `Tenants→FolderRules`):
  make one `ClientCascade` and delete those rows in the service.
- SQLite connection pool gotchas: [testing-backend.md](testing-backend.md).
