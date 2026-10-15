# Update after an upgrade (migration page)

Code: `src/Sfe.Api/Maintenance/MigrationCoordinator.cs`, `src/Sfe.Core/Upgrades/` (`InstalledVersion`, `Changelog`,
`SemanticVersion`, `Steps/`); client `src/app/migration/` (`MigrationPage`, `changelog.ts`). Rules for writing a
migration path: [production-upgrades.md](../development/production-upgrades.md).

- `MigrationCoordinator.InitializeAsync` (Program.cs, before `app.Run`) reads the install's version read-only
  (`InstalledVersion`: database exists + has tables? `ServerProperties` table? its `Version` row).
- No database or no tables = new install; same version = current: both run `UpdateAsync` (access schema, first space,
  `GrantNewRightsAsync`, `MovePerLibrarySettingsAsync`) and record the version.
- Earlier or unknown (installs before 1.37.0 have no table) = `Required`; later = `Newer`; unreadable = `Unavailable`
  (retried by `RequestGuard` every 10 s).
- Every status but `None` blocks: `RequestGuard` answers 503 `migration-required` except `api/migration*`,
  `TenantRegistry` starts no library, `SfeSession.ValidateAsync` skips the accounts' query.
- `POST api/migration` (setup code, `PasswordHashing.SameSecret`) runs `UpdateAsync` strictly in the background, starts
  each library and initializes its index again (so an index error fails the update), records the version; the page
  polls `GET api/migration`.
- `Sfe:Database:MigrateOnStartup=false` skips the check.
- The client's `Api` opens `/migration` on any 503 `migration-required`, even for calls with redirects off.
- The update page shows the embedded `CHANGELOG.md` ([versioning-and-changelog.md](../development/versioning-and-changelog.md))
  and offers a backup ([backups.md](backups.md)).
- Tests that make a database before starting the server: `ApiClients.MigrateAsync(factory)` first
  ([testing.md](../development/testing.md)).
