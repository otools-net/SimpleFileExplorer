# Adding a setting

Code: `src/Sfe.Core/Options/SfeOptions.cs`, `Settings/EditableSettings.cs`, `Settings/SettingCatalog.cs`,
`Settings/SettingsService.cs` (`Validate`); client `src/app/settings/settings-schema.ts`, `admin/admin-sections.ts`,
`core/rights.ts` (`SETTINGS_RIGHTS`). Levels: [settings-levels.md](settings-levels.md). Never rename or remove a key
silently: [production-upgrades.md](../development/production-upgrades.md).

- New settings go in `SfeOptions`. UI-editable ones also need `EditableSettings` (`From` + `ApplyTo`), a range check in
  `SettingsService.Validate` and a field in `settings/settings-schema.ts`.
- `settings-schema.ts`: `TABS` → groups → fields; a new tab also needs a sub-page `{ settings: id }` in
  `admin/admin-sections.ts`. Field types `number`, `checkbox`, `text`, `list`, `extensions`, `select` with
  `options: [[value, label]]` (`numeric: true` on the same line: its values are sent as numbers, and the API test expects a number), `preset` = an unsaved choice filling the `fills` fields from `presets`, e.g.
  `EXTERNAL_PLAYERS`.
- Add its key to `SIMPLE_SETTINGS` (same file) to show it in the default simple view, whose groups without a listed
  key are hidden (a tab with none shows a "Show all settings" note; an error on a hidden field shows all).
- `EverySettingOfTheClientsSettingsDialogExists` reads `settings-schema.ts` with a regex (`{ key: '…', label: '…',
  type: '…'`): keep a field's first three properties in that order, on one line.
- Saved values override appsettings/env and flow through `IOptionsMonitor`.
- **Level:** a new key of `EditableSettings` is a library setting (`SettingLevel.Tenant`) unless listed in
  `SettingCatalog.ServerKeys` (server resources) or `UserKeys` (personal preferences); paths inside a library go in
  `PerLibraryKeys`. Its right comes from its prefix (`RightsByPrefix`, matching its settings tab and
  `SETTINGS_RIGHTS`).
- User-level values never reach `SfeOptions`: the server reads them through `SettingsService.GetEffectiveAsync`
  (`/api/config`), or takes them as request parameters.
- **Never editable from the UI:** DB, cache dir, setup, security or tenant storage options: they run
  programs or decide where data lives, which only whoever runs the server may decide. The library root, `Urls` and the
  ffmpeg path are the only exceptions, and only through `InstallationSettings`
  ([installation-page.md](installation-page.md)), never as `EditableSettings`. `openBrowser`/`fastSignIn` are
  server-level `EditableSettings` with right `server.settings` ([local-start.md](local-start.md)).
- Document a configuration key in `docs/configuration.md`.
