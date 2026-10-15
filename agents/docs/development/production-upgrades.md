# Production: every change keeps existing installs working

SFE is in production since 1.33.0. Code: `src/Sfe.Api/Maintenance/MigrationCoordinator.cs`,
`src/Sfe.Core/Upgrades/` (`Steps/`, `UpgradeSteps.All`), `src/Sfe.Core/Data/`. How the update runs:
[update-migration.md](../features/update-migration.md).

Every change must keep existing installs (their database, caches, settings file, browsers' localStorage) working after
an upgrade. A change to the architecture, the database, stored files or the configuration ships with its migration
path in the same change, and that path is tested:

- **Database:** a new SQL Server migration (never edit, remove or squash a shipped one) **and** the SQLite path
  ([database.md](database.md)); new columns nullable, existing rows back-filled (e.g. the next scan reads what is still
  null, as for `Files.Duration`) or given a correct default; no data dropped without the user asking.
- **Nothing writes before the update is confirmed:** nothing writes to a database or the settings file before the
  update is confirmed: steps run at each start go in `MigrationCoordinator.UpdateAsync` (not Program.cs), and a new
  root hosted service checks `IsBlocking` first (the one exception: `data/setup-code.txt`, a new file the update page
  itself needs, `GeneratedSetupCode.Ensure`).
- **Settings:** never rename or remove a key of `SfeOptions`/`EditableSettings` silently (saved `settings.json` files
  and appsettings use it); a new key needs no step (`SettingsStore` keeps the configured value for keys missing from
  the file); per-browser keys (`core/preferences.ts`, `sfe.*`) are read in their old form too, or dropped harmlessly.
- **Caches** (thumbnails, trickplay, fingerprints): a layout change either reads the old one, rebuilds it lazily
  without breaking listings meanwhile, or moves it with an upgrade step (next bullet).
- **Upgrade steps:** anything outside the databases' schemas (cache layouts, stored files) that an update must convert
  is an idempotent `IUpgradeStep` in `Core/Upgrades/Steps/V<major>_<minor>_<patch>/`, listed in `UpgradeSteps.All`:
  `MigrationCoordinator` runs those after the installed version, after the accounts' update and before any library
  starts (all of them at each start with `MigrateOnStartup=false`); `UpgradeContext` gives every library's cache folder
  and the update page's log.
- **Test the upgrade** from the previous shape (e.g. drop the new column / null the new values, then initialize and
  scan: `AddsTheDurationColumnToAnExistingSqliteIndex`), and say in `docs/upgrading.md` what an upgrade does when the
  user notices (a slower first scan, rebuilt caches, a setting moved).
- New rights for existing installs: [rights.md](../features/rights.md) (`since`).
