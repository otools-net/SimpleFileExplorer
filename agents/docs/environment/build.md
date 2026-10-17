# Building (client + server)

Code: `src/Sfe.Api/Sfe.Api.csproj` (targets), `src/Sfe.Web/package.json`, `angular.json`, `tsconfig.boot.json`,
`build/`. Packages: [portable-build.md](portable-build.md).

- The client is built by MSBuild targets in `Sfe.Api.csproj` (`SfeWebInstall`: `npm ci` when `node_modules` is
  missing; `SfeWebBuild`: `npm run build`, skipped while `wwwroot/index.html` is newer than `src/Sfe.Web/src`, `public`
  and `*.json`; `SfeWebContent` re-reads `wwwroot` as `Content`, since the file names carry content hashes).
- After `npm install <pkg>`, commit `package-lock.json` (`npm ci` needs it).
- `src/theme-boot.ts` is not in the Angular bundle (that runs after first paint) and must stay a script (no
  import/export): `npm run boot` (`tsc -p tsconfig.boot.json`) compiles it to `out-tsc/boot/theme-boot.js`, which
  angular.json copies next to `index.html`. `npm run build`/`start`/`watch` run it first (npm `pre*` scripts); a bare
  `ng build` without it ships no theme script.
- `npm start` (`ng serve`) proxies `/api` and `/openapi` to `http://localhost:5080` (`proxy.conf.json`): run the API
  alongside for live-reloading client work.
- The build scripts test (`dotnet test` + the client's `npm test`), publish, package and smoke-test. AppImage tools are
  downloaded once into `artifacts/tools`; offline, put them there or set `APPIMAGETOOL` / `APPIMAGE_RUNTIME`.
- `StaticWebAsset ... No file exists for the asset ... main-XXXX.js` during `dotnet build/test`: the client was rebuilt
  with a new hash mid-build; build again.
- User's app running → locked DLLs: build/test with `--artifacts-path <scratch dir>`, don't stop it. There,
  `TheShippedChangelogHasTheRunningVersion` fails (it finds `CHANGELOG.md` from the output folder): run it alone
  without.
- The initial bundle budget is 600 kB (warning only): [client-routes.md](../architecture/client-routes.md).
- `ng build --configuration development` writes an unminified client into `src/Sfe.Api/wwwroot`, whose Angular
  comments hold sample URLs (cloudinary, imagekit): `WebClientHasNoExternalResources` then fails. Rebuild with `npm run
  build` (or `dotnet build`) before `dotnet test`.
