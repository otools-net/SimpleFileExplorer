# Portable build and packages

Code: `build/build.ps1`, `build/build.sh`, `build/portable/appsettings.json`, `build/windows/`, `build/linux/`,
`src/Sfe.Api/PortableBuild.cs`, `BrowserLauncher.cs`, `WindowsServiceSetup.cs`.

- `-p:SfePortable=true` = self-contained single file + `build/portable/appsettings.json` + `SfePortable` assembly
  attribute, which makes `PortableBuild` use the exe folder as content root (an explicit `--contentRoot` wins: the
  AppImage's `AppRun` passes `~/.config/sfe`).
- `src/Sfe.Api/data/` is never published.
- `build.ps1` publishes linux-x64 on Windows; only `appimagetool` and the smoke test run in WSL; the AppDir is built
  under `/tmp` (files on `/mnt/c` all look executable).
- Smoke tests: start the package on a temp library, check version, client (`<sfe-root` + its hashed `main-*.js`),
  browse, scan, search, data folder. Portable builds open the browser (`BrowserLauncher`, `Sfe:OpenBrowser`): smoke
  tests pass `--Sfe:OpenBrowser=false`.
- `build/windows/*.cmd` (install-service / uninstall-service) are published in the win-x64 package; `.cmd` files are
  CRLF ([line-endings.md](line-endings.md)).
- Version in package names: [app-version.md](../architecture/app-version.md).
