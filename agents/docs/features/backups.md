# Backups

Code: `src/Sfe.Api/Maintenance/BackupSources.cs`, `BackupDownloads.cs`; `src/Sfe.Core/Backup/BackupWriter.cs`.

- `BackupSources` (Api) lists the main database, every other library's (`Tenants` read by projection, connection
  strings as `TenantHost.ConnectionString`), `settings.json` and the content root's `appsettings*.json`.
- `BackupWriter` (Core) writes a temp zip (databases + config files, manifest, README): SQLite via
  `SqliteConnection.BackupDatabase` (read-only, no pooling), SQL Server via `BACKUP DATABASE … COPY_ONLY` into
  `Sfe:Backup:SqlServerFolder` or the instance default, added if readable from here (untested: no SQL Server on the dev
  machine).
- Served with `FileOptions.DeleteOnClose`: `GET api/system/backup` (`server.backup`) directly, the update page through
  `POST api/migration/backup` (code) → a one-time `GET api/migration/backup/{token}` link (`BackupDownloads`, 15 min),
  so the browser streams it to disk.
- The manifest's `version` is the install's (`DatabaseVersion`), `madeBy` the running one.
