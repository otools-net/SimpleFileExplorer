# Installation page (root, Urls, ffmpeg from the web)

Code: `src/Sfe.Api/Maintenance/InstallationSettings.cs`, `Security/InstallationAccess.cs`,
`Endpoints/` (installation routes); client `src/app/account/` (`InstallationForm`, also System › Installation),
`src/app/shared/` (`FolderPicker`).

- `api/installation/check` (code, rate-limited) answers a token (`InstallationAccess`, data protection, 1 h, hash of
  the code) sent in `X-SFE-Installation` to `GET|PUT api/installation` and `GET api/installation/folders` (all
  `AllowAnonymous`, open before the setup: `RequestGuard` and `TenantMiddleware` let `/api/installation` through).
- `PUT` writes only changed keys and reloads the file's provider; a new root or ffmpeg runs
  `TenantRegistry.RestartAsync` (the libraries read both once), a new `Urls` answers `restartRequired`.
- `Field` reports a key as locked when a provider after the file gives it
  ([config-files.md](../architecture/config-files.md)).
- `Ffmpeg.Find` also accepts ffmpeg's folder (or its `bin`).
- Only the allow-list `InstallationSettings.Keys` (library root, `Urls`, ffmpeg path) is writable here, into
  `data/installation.json`, behind the setup code: never as `EditableSettings`
  ([settings-new-key.md](settings-new-key.md)).
