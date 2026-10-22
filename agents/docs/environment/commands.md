# Commands

```sh
dotnet build ; dotnet test                                            # the Sfe.Api build runs npm ci + ng build
cd src/Sfe.Web ; npm test ; npm run build ; npm start                 # client: Vitest, build into wwwroot, ng serve :4200
dotnet build -p:SfeSkipWebClient=true                                 # without rebuilding the client
SFE_TEST_FFMPEG=<path to ffmpeg> dotnet test                          # also run the ffmpeg tests (path: dev-machine.md)
dotnet run --project src/Sfe.Api                                      # :5080, Development
dotnet tool restore                                                   # once, for dotnet-ef 10.0.12
dotnet ef migrations add <Name> --context SfeDbContext --project src/Sfe.Core --output-dir Data/Migrations
dotnet ef migrations add <Name> --context AccessDbContext --project src/Sfe.Core --output-dir Data/AccessMigrations
build/build.ps1 [-Target windows|linux] [-Version 1.2.0] [-SkipTests] [-SkipSmokeTest] [-NoTests]  # -> artifacts/
build/build.sh  [--target linux|windows|all] [--version V] [--skip-tests] [--skip-smoke-test] [--no-tests]
dotnet msbuild src/Sfe.Api/Sfe.Api.csproj -getProperty:Version       # current version
```

- How the client build hooks into MSBuild, and build errors: [build.md](build.md).
- Running the app on a scratch library: [running-the-app.md](running-the-app.md).
- `dotnet test -p:SfeSkipWebClient=true` skips the client build (then `ServesTheWebClientAndOpenApi` needs an existing
  `wwwroot`).
