# Commands

```sh
dotnet build ; dotnet test                                            # the Sfe.Api build runs npm ci + ng build
cd src/Sfe.Web ; npm test ; npm run build ; npm start                 # client: Vitest, build into wwwroot, ng serve :4200
cd tests/e2e ; npm ci ; npm run browsers ; npm run e2e              # end-to-end on phone-sized browsers (testing-e2e.md)
dotnet build -p:SfeSkipWebClient=true                                 # without rebuilding the client
SFE_TEST_FFMPEG=<path to ffmpeg> dotnet test                          # also run the ffmpeg tests (path: dev-machine.md)
dotnet run --project src/Sfe.Api                                      # :5080, Development
dotnet tool restore                                                   # once: dotnet-ef, CSharpier, Kiota (dotnet-tools.json)
dotnet csharpier format .                                             # C# layout; checks: readability.md#checking
node agents/tools/client-generation/generate.mjs [--check]            # C# client's Generated/ from the v1 baseline
dotnet pack src/Sfe.Client -c Release -o <folder>                     # the C# client package alone (build.ps1 packs it too)
SFE_PUBLIC_API=write dotnet test tests/Sfe.Client.Tests --filter FullyQualifiedName~PublicApiTests  # its API snapshot
dotnet ef migrations add <Name> --context SfeDbContext --project src/Sfe.Core --output-dir Data/Migrations
dotnet ef migrations add <Name> --context AccessDbContext --project src/Sfe.Core --output-dir Data/AccessMigrations
build/build.ps1 [-Target windows|linux] [-Version 1.2.0] [-SkipTests] [-SkipSmokeTest] [-NoTests]  # -> artifacts/
build/build.sh  [--target linux|windows|all] [--version V] [--skip-tests] [--skip-smoke-test] [--no-tests]
dotnet msbuild src/Sfe.Api/Sfe.Api.csproj -getProperty:Version       # current version
git config blame.ignoreRevsFile .git-blame-ignore-revs               # once per clone: blame skips reformat commits
```

- How the client build hooks into MSBuild, and build errors: [build.md](build.md).
- Running the app on a scratch library: [running-the-app.md](running-the-app.md).
- `dotnet test -p:SfeSkipWebClient=true` skips the client build (then `ServesTheWebClientAndOpenApi` needs an existing
  `wwwroot`).
