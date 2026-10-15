# Running the app on a scratch library

- Environment: `ASPNETCORE_ENVIRONMENT=Development` + env `Sfe__Root`, `ConnectionStrings__Sfe`,
  `Sfe__Database__Provider=Sqlite`, `Sfe__SettingsFile`, `Sfe__Thumbnails__CacheDirectory`, `Urls`, then
  `dotnet run --no-launch-profile` (or `dotnet <artifacts>/bin/Sfe.Api/debug/Sfe.Api.dll --contentRoot <absolute
  src/Sfe.Api>`: a relative content root resolves against the dll's folder).
- Without Development the page is unstyled (no static web assets). `MapStaticAssets` only serves files present at
  build time: rebuild and restart after a client change (or use `npm start`).
- Add `Sfe__Setup__Code` (≥ 12 characters) and `Sfe__Security__KeysDirectory` (a scratch folder), then create the
  administrator on the setup page (or `POST api/setup` with `X-SFE-Request: 1`).
- After a version bump the app meets the update gate first ([update-migration.md](../features/update-migration.md)).
- Looking at the page: [visual-check.md](visual-check.md).
