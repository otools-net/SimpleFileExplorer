# Settings by level

Code: `src/Sfe.Core/Settings/` (`SettingsStore`, `SettingsService`, `SettingLayers`, `SettingRows`, `SettingCatalog`),
`src/Sfe.Api/Endpoints/` (settings routes); client `src/app/settings/` (`SettingsForm`, `SettingsPanel`). Adding a
key: [settings-new-key.md](settings-new-key.md).

- Levels: environment (configuration, `Sfe:Locked`) → server (`settings.json`, `"locked": [...]`) → library
  (`TenantSettings` rows, `Locked` column) → user (`UserSettings` rows).
- `SettingLayers.Resolve(sources, level)` gives value, source, lock and inherited value per key; `States` adds
  editable/lockable for a page (scope, locks above, the caller's section rights).
- Saving keeps only values differing from the inherited ones (normalised first: config `""` vs saved `null`); changing
  a non-editable key is a validation error.
- A library's `SfeOptions` = config + `SettingLayers.ApplyTo(options, tenantId)` (post-configure; `SettingLayers` is
  also a change-token source).
- Routes: `api/settings?level=server|tenant` (default: server for `server.settings`), `PUT api/settings/locks?level=`,
  `api/me/preferences`. `PATCH` on both (`SettingChanges`: `values` by flat key, `lock`, `unlock`) =
  `SettingsService.SaveChangesAsync`: writes only the keys sent over the level's stored layer (a value equal to the
  inherited one drops the row unless locked), validated with the values in effect, all or nothing; the client only uses
  it (the whole-values `PUT` stays for scripts and the docs tool).
- Client: `SettingsForm.level` (`levels()` from rights, remembered in `sfe.settings.level`; `forUser()` for My
  account), `states()` per key (null from old servers: then everything editable, server wording), lock boxes and notes
  in `SettingsPanel`.
- The level tabs keep each level's unsaved changes (`pending`, a `LevelEdits` per level: changed controls + lock
  changes), re-applied by `load()` over the level loaded again (dropping keys no longer editable/lockable);
  `changed`/`changedLevels` span every level; `save()` patches each level in `SAVE_ORDER` (server first), showing the
  first refused level with its errors; `discard()` clears every level.
- Program.cs moves per-library keys out of `settings.json` at start (`SettingLayers.MovePerLibrarySettingsAsync`).
