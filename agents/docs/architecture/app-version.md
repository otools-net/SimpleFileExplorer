# App version

Code: `Directory.Build.props` (`<Version>`), `src/Sfe.Api/AppVersion.cs`. Bumping rules: [versioning-and-changelog.md](../development/versioning-and-changelog.md).

- `AppVersion` splits the informational version (`1.0.0+<sha>`, commit added by the SDK) for `/api/config` (`version`,
  `commit`; shown at the bottom of the ⋮ menu and in Administration › System), OpenAPI and the start-up log.
- Build scripts read it with `-getProperty:Version`, publish with `-p:Version`, name packages `SFE-<version>-…`; smoke
  tests check the app reports the version in the package name.
- Current version: `dotnet msbuild src/Sfe.Api/Sfe.Api.csproj -getProperty:Version`.
- The installed version (database) vs the running one: [update-migration.md](../features/update-migration.md).
