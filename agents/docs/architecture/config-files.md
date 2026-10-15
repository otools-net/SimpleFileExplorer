# Configuration files

Code: `src/Sfe.Api/Program.cs`, `src/Sfe.Api/appsettings*.json`, `Maintenance/InstallationSettings.cs`.

- `src/Sfe.Api/appsettings.json` is neutral (root `~`, SQLite `data/sfe.db`); this machine's values (Windows paths,
  `REVAN\SQLEXPRESS`) are in the git-ignored `appsettings.Local.json`, inserted by Program.cs right after
  `appsettings.json` (so `appsettings.Development.json` still overrides it: SQLite in `src/Sfe.Api/data/`, git-ignored,
  root fixed to a local folder) and never published by portable builds.
- Source order: appsettings.json, Local, {Environment}, `installation.json` (`InstallationSettings.AddTo`, after the
  last `appsettings*`), env, command line.
- Tests: `WebApplicationFactory`'s `UseSetting` values come after `installation.json`, so they show as locked on the
  installation page (remove `Sfe:Root` from the test settings to change it there).
- Settings editable from the UI: [settings-new-key.md](../features/settings-new-key.md); levels:
  [settings-levels.md](../features/settings-levels.md).
