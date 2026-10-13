# AGENTS.md

Guidance for AI agents working on this repository. `README.md` is the end-user documentation (setup, settings,
features); everything about building, testing and the code lives here.

**Keep this file current:** add what the next agent would need (a convention, a gotcha, a command that works or
fails), fix or delete what is wrong. One fact per bullet, short.

## 1. General app purpose

**SFE — Simple File Explorer**: self-hosted web file explorer for a home media library. Browse, preview
photo/video/audio, index into a database, search with a small query language, tag files (TagStudio model), find
duplicates and similar videos. Runs on one machine or a private LAN, with accounts, groups, rights, guests and several
libraries (tenants) since 1.34.0. Port of an
ASP.NET MVC 5 / .NET Framework 4.8 app (EF6, jQuery, Accord/System.Drawing); the old app's database and thumbnail
cache are not adopted. **SFE itself is in production** (since 1.33.0): installs must survive every upgrade (§2).

| Area | Stack |
|---|---|
| Runtime | .NET 10, C# latest, `Nullable` + `ImplicitUsings`; solution `Sfe.slnx` |
| Web | ASP.NET Core minimal APIs (`MapGroup`, `TypedResults`), ProblemDetails, enums as camelCase strings |
| API docs | `Microsoft.AspNetCore.OpenApi` (`/openapi/v1.json`) + Swashbuckle Swagger UI only (`/api/docs`) |
| Data | EF Core 10, **SQL Server** (migrations) and **SQLite** (`EnsureCreated`); `IDbContextFactory<SfeDbContext>` |
| Media | external **ffmpeg** CLI (optional): thumbnails, trickplay, video fingerprints |
| Frontend | **Angular 22** + TypeScript 6 in `src/Sfe.Web`: standalone components, signals, zoneless, OnPush, `HttpClient`, router; global CSS; built by npm into `src/Sfe.Api/wwwroot` (git-ignored) |
| Tests | MSTest 4, `WebApplicationFactory<Program>`, method-level parallelism; client: Vitest via `ng test` (`*.spec.ts`) |
| Hosting | Kestrel `:5080`, Windows service / systemd / IIS, portable zip + AppImage |

```
src/Sfe.Core/           Domain library, no ASP.NET dependency; DI in ServiceCollectionExtensions (singletons):
                        AddSfeCore = one library's services, AddSfeAccess = the server's accounts and settings levels
  Access/               RightCatalog (+ Rights ids), AccessService (libraries, accounts, groups), RightsEvaluator,
                        SignInService + IUserDirectory/LocalUserDirectory, SetupService, PasswordHashing, AccessChanges
  Data/                 SfeDbContext, Migrations (SQL Server), DatabaseInitializer, SfeDatabase; AccessDbContext,
                        AccessMigrations (its own history table), AccessDatabaseInitializer
    Entities/           one class per file in Index/ Tags/ MissingFiles/ Tasks/ Access/, all in namespace Sfe.Core.Data
  Indexing/             DirectoryIndexer, ScanCoordinator (background scan), FileFingerprint (partial SHA1),
                        MediaDimensions (image headers / ffmpeg → Width, Height)
  Library/ Paths/       LibraryBrowser, MediaClassifier, NaturalComparer / LibraryPaths (root-relative, escape-proof)
  Search/               SearchQuery (parser), SearchPredicateBuilder, FileSearchService
  Tagging/              TagService, TagGraph (parent inheritance), built-in tags, colors
  Thumbnails/           ThumbnailService (disk cache, concurrency gate), Ffmpeg (locate + run)
  Trickplay/            TrickplayService (background passes + player queue), VideoFingerprint
  FileTracking/         DuplicateFinder/Remover, RelinkService, MissingFileRecorder, SimilarVideoFinder, VideoMatcher
  Editing/              EditedImages (saves the web editor's pictures as new files: format check, name, index)
  Tasks/                BackgroundTasks (status/start/cancel of every job), TaskScheduleService (+ loop),
                        ScheduleCalculator, TaskHistory (TaskRuns), TaskRunLog + TaskLogProvider (run logs)
  Settings/ Options/    SettingsStore (server level, settings.json)/Service, EditableSettings, SettingCatalog (scope and
                        right of each key), SettingLayers (resolution by level), SettingRows (library/user rows) /
                        SfeOptions ("Sfe" section), AccessOptions (Setup, Security, Rights, Tenants)
  SystemCommands/       shutdown / dismount through the shell
src/Sfe.Api/            Program.cs, Endpoints/*Endpoints.cs (one class per area), TrickplayWorker, TaskScheduleWorker, PortableBuild,
                        AppVersion; wwwroot/ = the built client (generated, git-ignored)
  Security/             cookie session (SfeSession), CurrentAccess (the request's account, library, rights), RequireRight +
                        policy provider, RequestGuard (setup gate, X-SFE-Request, /api/docs), FileLinks (player tokens)
  Tenancy/              TenantRegistry/TenantHost (one AddSfeCore container per library), TenantMiddleware +
                        TenantRequestServices (routes get their library's Core services)
src/Sfe.Web/            the web client (Angular workspace: angular.json, package.json, proxy.conf.json)
  public/               copied as is: favicon.svg
  src/theme-boot.ts     classic script run before first paint (theme), compiled alone by `npm run boot`
  src/testing/          setup.ts (jsdom stand-ins, angular.json test > setupFiles), fakes.ts (mockApi, builders, settle)
  src/styles.css        imports styles/*.css, one partial per area of the page
  src/app/core/         Api (the only HTTP client), models (API shapes), ConfigService (+ Urls, rights: can/canAny),
                        rights (right ids, SETTINGS_RIGHTS), format, paths, preferences (localStorage), ToastService,
                        ThemeService, CopyPathService
  src/app/account/      LoginPage (`login`), SetupPage (`setup`), AccountSection (My account: preferences, password)
  src/app/access/       People (Accounts, Groups, GuestAccess) and Libraries sections of the admin page
  src/app/shared/       icon + IconSprite, SheetComponent (dialog card), BusyDirective, highlight, toasts, empty state
  src/app/library/      the listing page: LibraryPageComponent, ListingStore (state), library-route, Dialogs registry,
                        topbar/ location/ items/ search-explain/ scan/ sidebar/ viewer/, selection bar
  src/app/player/       VideoPlayerComponent, PlayerPageComponent (`player?path=&t=`)
  src/app/admin/        AdminPageComponent (`admin?section=&page=`), admin-sections.ts (sidebar groups, sections, sub-pages), System section
  src/app/settings/     settings-schema.ts, SettingsForm (one form for every settings tab), SettingsPanel (one tab)
  src/app/editor/       ImageEditorDialog (over the viewer), edits.ts (operations, EDIT_KINDS, renderEdits), crop.ts
  src/app/tags/ trickplay/ tracking/   their dialogs, sections of the admin page and parts
  src/app/tasks/        TasksSection (sub-pages Tasks/Schedules/History of the admin page), ScheduleForm, TaskHistory, tasks.ts wording
tests/Sfe.Tests/        unit, SQLite integration, HTTP tests; Infrastructure/ = TestLibrary + CoreServices + ApiClients
build/                  build.ps1 / build.sh (portable packages), portable/appsettings.json, windows/ linux/ smoke tests
Directory.Build.props   the app version (all projects)
```

## 2. Rules of development

- **The app is in production:** every change must keep existing installs (their database, caches, settings file,
  browsers' localStorage) working after an upgrade. A change to the architecture, the database, stored files or the
  configuration ships with its migration path in the same change, and that path is tested:
  - database: a new SQL Server migration (never edit, remove or squash a shipped one) **and** the SQLite path (next
    bullet); new columns nullable, existing rows back-filled (e.g. the next scan reads what is still null, as for
    `Files.Duration`) or given a correct default; no data dropped without the user asking;
  - settings: never rename or remove a key of `SfeOptions`/`EditableSettings` silently (saved `settings.json` files and
    appsettings use it); a new key needs no step (`SettingsStore` keeps the configured value for keys missing from the
    file); per-browser keys (`core/preferences.ts`, `sfe.*`) are read in their old form too, or dropped harmlessly;
  - caches (thumbnails, trickplay, fingerprints): a layout change either reads the old one or rebuilds it lazily,
    without breaking listings meanwhile;
  - test the upgrade from the previous shape (e.g. drop the new column / null the new values, then initialize and
    scan: `AddsTheDurationColumnToAnExistingSqliteIndex`), and say in `README.md` what an upgrade does when the user
    notices (a slower first scan, rebuilt caches, a setting moved).
- **Versioning** (`<Version>` in `Directory.Build.props`, semantic `major.minor.patch`; the build scripts accept
  nothing else). Once per delivery, i.e. per commit: when the version was already bumped by uncommitted work (compare
  with `git diff HEAD -- Directory.Build.props`), later requests before the commit keep it (a minor bump covers fixes
  too). Otherwise, in the same change:
  - **bug fix** → bump patch: `1.0.0` → `1.0.1`;
  - **feature added or changed** → bump minor, reset patch: `1.0.1` → `1.1.0` (a task doing both bumps minor only);
  - major only when the user asks; no bump for changes to docs, tests or build scripts alone.
- **Docs:** user-visible changes (behaviour, settings, UI) go in `README.md`, kept user-oriented; implementation
  facts go here. Routes are documented by OpenAPI, not in the README: give every route `.WithName`,
  `.WithSummary`, `.WithTags` (and `.WithDescription` for details).
- **Two DB providers must both work.** SQL Server: add a migration (`Data/Migrations`, schema `SFE`). SQLite has no
  migrations: `DatabaseInitializer.AddMissingSqliteObjectsAsync` must create new tables **and indexes** in existing
  files (it also sets WAL) and adds new **nullable** columns (`ALTER TABLE ADD COLUMN`, from the model). Other column
  changes to existing tables are not handled on SQLite: make new columns nullable. Indexed strings need a max
  length on SQL Server.
- **Scale:** millions of files, folders of 100k+. No per-request work proportional to a folder or the index: browse
  reuses a sorted snapshot, search/duplicates page by key (`after` cursor, no `OFFSET`), indexer batches of 1000 +
  `ChangeTracker.Clear()`, tag counts cached briefly, unwanted thumbnail jobs dropped before ffmpeg.
- **Schema:** tables in schema `SFE` on SQL Server (`Directories`, `Files`, tags in `TagDefinitions`); thumbnail cache
  `{SHA1(folder)}/{SHA1(file)}.bin` (empty file = no thumbnail). Migrations are shipped: only add new ones.
- **Search terms are negatable:** every term the search accepts (words, `e:`, `s:`, `w:`/`h:`/`r:` (with `r:hr`/`r:lr`),
  `d:`, `t:`, `special:untagged`, any new one) must accept a leading `-` that finds exactly the files the term doesn't (pure
  complement: `-w:1920` includes unknown resolutions). A new filter term goes through `SearchQuery.AddFilter`, so it
  gets `SearchGroup.ExcludedFilters` for free; add it to `EveryTermAndItsNegationSplitTheVisibleFiles`.
- **Paths:** API paths are root-relative with `/`; always resolve through `LibraryPaths`. Comparison follows the
  host file system (case-sensitive on Linux). SQL Server caps paths at 260 (`SfeDbContext.MaxPathLength`).
- **Fully local:** neither the client's sources nor the built `wwwroot` reference an external URL
  (`ApiTests.WebClientHasNoExternalResources`; the bundle may only name `angular.dev/` in framework error texts); no
  npm package that loads anything from the network at run time. Swagger UI's validator is off.
- **Rights:** every route names its right(s): `.RequireRight(Rights.X)` (any of several), or `.AllowAnonymous()` for
  the session/setup routes (a route checking a token itself adds `RightMetadata` too). `AccessTests.EveryRouteNamesARight`
  fails otherwise. A new function gets a right in `RightCatalog` (scope, `GuestAllowed` = false for anything that
  changes something or tells about the server, default groups), the same id in the client's `core/rights.ts`, and the
  client hides what needs it (`config.can(...)`, `AdminSubpage.rights`). A right added after 1.34 gets
  `since: RightCatalog.Revision + 1` (`RightDefinition.Since`): `AccessService.GrantNewRightsAsync` (Program.cs, each
  start) gives it once to the built-in groups of existing spaces by `DefaultGroups`, tracked by `Tenants.RightsRevision`
  (null = 0); new spaces get `Revision`. Without it, existing installs never get the right.
- **Settings levels:** a new key of `EditableSettings` is a library setting (`SettingLevel.Tenant`) unless listed in
  `SettingCatalog.ServerKeys` (server resources) or `UserKeys` (personal preferences); paths inside a library go in
  `PerLibraryKeys`. Its right comes from its prefix (`RightsByPrefix`, matching its settings tab and `SETTINGS_RIGHTS`).
  User-level values never reach `SfeOptions`: the server reads them through `SettingsService.GetEffectiveAsync`
  (`/api/config`), or takes them as request parameters.
- **Libraries (tenants):** Core services are per library (`AddSfeCore` per `TenantHost`); routes get them through
  `TenantRequestServices`. Services of the whole server (access, settings levels, `FfmpegLimits`) are registered in the
  application and handed to each library's container (`TenantHost.StartAsync`). A root singleton must never depend on a
  Core type (the root's registrations of Core types throw: they only tell minimal APIs which parameters are services).
- **Options:** new settings go in `SfeOptions`. UI-editable ones also need `EditableSettings` (`From` + `ApplyTo`),
  a range check in `SettingsService.Validate` and a field in `settings/settings-schema.ts` (`TABS` → groups → fields; a
  new tab also needs a sub-page `{ settings: id }` in `admin/admin-sections.ts`; types
  `number`, `checkbox`, `text`, `list`, `extensions`, `select` with `options: [[value, label]]`, `preset` = an unsaved
  choice filling the `fills` fields from `presets`, e.g. `EXTERNAL_PLAYERS`); add its key to
  `SIMPLE_SETTINGS` (same file) to show it in the default simple view, whose groups without a listed key
  are hidden (a tab with none shows a "Show all settings" note; an error on a hidden field shows all). Saved values
  override appsettings/env and flow through `IOptionsMonitor`. Never make root, DB, ffmpeg path, system commands,
  cache dir, setup, security or tenant storage options editable from the UI: they run programs or decide where data
  lives, which only whoever runs the server may decide.
- **Frontend:** standalone components with `ChangeDetectionStrategy.OnPush`, `input()`/`output()`/`viewChild()`,
  state in signals (the app is zoneless: a change outside a signal is not drawn), `inject()`, built-in control flow
  (`@if`/`@for`/`@switch`), 4-space indents, `strict` + `strictTemplates`. Only `core/api.service.ts` makes HTTP
  requests (promises; an `AbortSignal` cancels); URLs stay relative (virtual directory). Styling is global CSS in
  `src/styles/` by class name, no component styles; hosts carry the classes (`host: { class: 'tile' }`, attribute
  selectors like `article[sfeFileTile]`) so the DOM and the CSS stay as they were. Icons: `<svg sfeIcon="name">`
  from `IconSpriteComponent`. Per-browser preferences through `core/preferences.ts`. Every button (and icon link)
  gets a `title` hint saying what it does, with its shortcut if any.
- **Code style:** primary constructors, file-scoped namespaces, collection expressions, `[GeneratedRegex]`, XML doc
  comments explaining *why*; match the surrounding comment density.
- **Every new feature comes with tests**, in the same change: backend (MSTest in `tests/Sfe.Tests`) for server
  behaviour, frontend (a `*.spec.ts`) for client behaviour, both when it spans the two. A bug fix gets a test that
  fails without it. Not done until `dotnet test` and `npm test` pass.
- **Tests:** each test gets its own `TestLibrary` (temp tree with odd/Unicode names + own SQLite file, setup code,
  keys folder); `CoreServices.CreateAsync` for Core (no accounts: the server level only), `WebApplicationFactory<Program>`
  for HTTP through `ApiClients` (`CreateFactory`, `SetUpAsync` = signed in as the server administrator, `SignInAsync`,
  `Create` = a visitor; all send `X-SFE-Request`). Core has
  `InternalsVisibleTo Sfe.Tests`. New search cases go in `SearchCombinationTests` (data rows over its fixture).
  Client: a `*.spec.ts` next to the code; components get `mockApi()` + `testProviders(api)` + `loadConfig(api)` from
  `src/testing/fakes.ts` (`fakeConfig()` = a server administrator with every right; `limitedConfig(rights)` = another
  account; `settingsValues()` = a valid value for every settings field); `Api` itself is tested with `HttpTestingController`. The administration page:
  `adminPageProviders(api)` + `RouterTestingHarness` (navigate to `/` and back for a new page and form).
- **Git:** work is committed on `master` (PR branch `main`), messages `claude: <short summary>`; commit only when asked.

## 3. Useful commands and trivia

### Commands

```sh
dotnet build ; dotnet test                                            # the Sfe.Api build runs npm ci + ng build
cd src/Sfe.Web ; npm test ; npm run build ; npm start                 # client: Vitest, build into wwwroot, ng serve :4200
dotnet build -p:SfeSkipWebClient=true                                 # without rebuilding the client
SFE_TEST_FFMPEG=C:/Portables/ffmpeg/bin/ffmpeg.exe dotnet test        # also run the ffmpeg tests
dotnet run --project src/Sfe.Api                                      # :5080, Development
dotnet tool restore                                                   # once, for dotnet-ef 10.0.12
dotnet ef migrations add <Name> --context SfeDbContext --project src/Sfe.Core --output-dir Data/Migrations
dotnet ef migrations add <Name> --context AccessDbContext --project src/Sfe.Core --output-dir Data/AccessMigrations
build/build.ps1 [-Target windows|linux] [-Version 1.2.0] [-SkipTests] [-SkipSmokeTest]  # -> artifacts/
build/build.sh  [--target linux|windows|all] [--version V] [--skip-tests] [--skip-smoke-test]
dotnet msbuild src/Sfe.Api/Sfe.Api.csproj -getProperty:Version       # current version
```

- The client is built by MSBuild targets in `Sfe.Api.csproj` (`SfeWebInstall`: `npm ci` when `node_modules` is
  missing; `SfeWebBuild`: `npm run build`, skipped while `wwwroot/index.html` is newer than `src/Sfe.Web/src`,
  `public` and `*.json`; `SfeWebContent` re-reads `wwwroot` as `Content`, since the file names carry content hashes).
  After `npm install <pkg>`, commit `package-lock.json` (`npm ci` needs it).
- `src/theme-boot.ts` is not in the Angular bundle (that runs after first paint) and must stay a script (no
  import/export): `npm run boot` (`tsc -p tsconfig.boot.json`) compiles it to `out-tsc/boot/theme-boot.js`, which
  angular.json copies next to `index.html`. `npm run build`/`start`/`watch` run it first (npm `pre*` scripts); a bare
  `ng build` without it ships no theme script.
- `npm start` (`ng serve`) proxies `/api` and `/openapi` to `http://localhost:5080` (`proxy.conf.json`): run the
  API alongside for live-reloading client work.
- The build scripts test (`dotnet test` + the client's `npm test`), publish, package and smoke-test. AppImage tools are downloaded once into
  `artifacts/tools`; offline, put them there or set `APPIMAGETOOL` / `APPIMAGE_RUNTIME`.

- `StaticWebAsset ... No file exists for the asset ... main-XXXX.js` during `dotnet build/test`: the client was rebuilt
  with a new hash mid-build; build again.
- A POST endpoint taking a JSON body param doesn't match a request without `Content-Type: application/json`, and
  `api.MapFallback` then answers 404 (not 415): take optional inputs of bodyless actions from the query
  (`POST api/tasks/{task}/start?path=`).
- User's app running → locked DLLs: build/test with `--artifacts-path <scratch dir>`, don't stop it.
- App on a scratch library: `ASPNETCORE_ENVIRONMENT=Development` + env `Sfe__Root`, `ConnectionStrings__Sfe`,
  `Sfe__Database__Provider=Sqlite`, `Sfe__SettingsFile`, `Sfe__Thumbnails__CacheDirectory`, `Urls`, then
  `dotnet run --no-launch-profile` (or `dotnet <artifacts>/bin/Sfe.Api/debug/Sfe.Api.dll --contentRoot <absolute
  src/Sfe.Api>`: a relative content root resolves against the dll's folder). Without Development the page is
  unstyled (no static web assets). `MapStaticAssets` only serves files present at build time: rebuild and restart
  after a client change (or use `npm start`). Add `Sfe__Setup__Code` (≥ 12 characters) and `Sfe__Security__KeysDirectory`
  (a scratch folder), then create the administrator on the setup page (or `POST api/setup` with `X-SFE-Request: 1`).
- A route handler whose only parameter is `HttpContext` binds as a `RequestDelegate` and its result is silently
  dropped (warning ASP0016): give it another parameter (`CancellationToken`) or cast it to `Delegate`.
- A DI factory registration (`AddScoped(type, sp => …)`) owns what it returns: the scope disposes it. Never forward
  another container's singletons that way (that's why routes get library services through `TenantRequestServices`).
- Anything non-GET under `/api` without `X-SFE-Request` is a 400 (curl, PowerShell, tests): add the header.
- Visual check: `msedge --headless=new --screenshot=out.png --virtual-time-budget=6000 URL` (fresh `--user-data-dir`
  each run; no H.264, use WebM). To click: Edge `--remote-debugging-port=0` (fixed ports like 9333 are reserved
  here: "bind() … forbidden"; read the port from `<user-data-dir>/DevToolsActivePort`), then a Node script with the
  built-in `WebSocket`: `Runtime.evaluate` (setup with `fetch('api/setup', …)`) then `Page.captureScreenshot`. Covers and previews load lazily: give a screenshot a budget of
  ~20 s before calling an image missing.

### Dev machine

- Windows; shells PowerShell 5.1 and Git Bash. The `node` on PATH is **26.11** (checked 2026-10-13): `npm test`,
  `npm run build` and the full `dotnet build`/`test` work. `dotnet test -p:SfeSkipWebClient=true` skips the client build
  (then `ServesTheWebClientAndOpenApi` needs an existing `wwwroot`). Python 3.12 is installed (no `websockets`). ffmpeg is not installed (the
  `C:/Portables/ffmpeg` of earlier notes is gone): the ffmpeg tests are skipped. With it on PATH, the default
  `FfmpegPath` (`ffmpeg`) finds it, so scans in tests run it on the fixture's fake media too (assert either way). `docker` points at
  an unreachable remote context. WSL Ubuntu exists but has no .NET SDK.
- Line endings: the index is all LF; working copies are mostly LF, a few may be CRLF. `dotnet ef migrations add`
  writes CRLF (with BOM): normalise the new files and the snapshot to LF (`sed -i 's/\r$//'`). List with `git ls-files --eol`, check one with `file` (Git
  Bash `grep` doesn't see `\r`). Keep each file's existing endings. `sed`/`perl` multi-line edits silently miss on
  CRLF files, and inserted lines come out LF: prefer the Edit tool, check `git diff --stat`, normalise with
  `sed -i 's/\r*$/\r/'`. New `.sh` files need LF (`.gitattributes`). Git Bash's `perl` reading a CRLF file through
  stdin drops the CRs, `awk` too; a heredoc holding `'` inside `$(…)`/quotes breaks the whole command: write long files
  with the Write tool.
- `warning: in the working copy of '…', LF will be replaced by CRLF` on every git command touching a changed LF
  file is harmless noise from the system gitconfig's `core.autocrlf=true` (commits stay LF): don't convert files
  to "fix" it; run git as `git -c core.safecrlf=false …` (e.g. `diff`, `status`, `add`) to keep the output clean.
- PowerShell 5.1 turns native stderr into errors under `ErrorActionPreference=Stop`, and a function's native output
  becomes its return value: `Invoke-Native` in `build.ps1` handles both.

### Tests

- Never call `SqliteConnection.ClearAllPools()` in tests: under method-level parallelism it disposes handles
  other tests are opening (`ObjectDisposedException: SQLitePCL.sqlite3`, "database is locked"). `TestLibrary`
  clears only its own pool. If SQLite contention still shows up occasionally, rerun before chasing it.
- ffmpeg tests (`TrickplayTests`, `SimilarVideoTests`) are inconclusive without `SFE_TEST_FFMPEG`/PATH. Tiny clips
  finish in milliseconds: assert "waiting" states before starting `RunAsync`. Moving test pictures: lavfi
  `mandelbrot`, `life`.
- File-tracking tests make junctions/hard links with `cmd /c mklink /J|/H` (no admin); `TestLibrary` can't delete
  through a junction, remove it first. "Registered after" ordering needs separate scans (one scan is parallel).
- Search expectations that are easy to get wrong: `Files.Name` has **no extension** (use `e:` or a leading `*`
  full-path query); `*` only counts at the start of the query and then applies to every alternative; several `e:` in
  a group are OR-ed, several `-e:` each excluded; `-r:WxH` is NOT(both sides), one `ExcludedFilters` group per
  negated token; files with a hidden tag stay hidden unless the search names it or an ancestor; a malformed `-s:` /
  `-w:` is ignored like a malformed `s:`, not searched as a word (so is a full-path `*D:\Media`: `D:` is the duration
  prefix, as `E:`/`S:`/`H:`... are filters); `d:` without a unit is minutes, without an operator "about" (± half the
  unit typed; a clock ± 0.5 s), `d:=` the same whole second; `r:hr`/`r:lr` need the file's extension classified as
  image or video, and `r:hr` and `r:lr` are not complements (unknown resolutions are neither). On SQLite, `LIKE` and `NOCASE` fold ASCII only:
  `русский` / `ESPAÑOL` don't find `Русский` / `Español` (SQL Server's CI collation does).
- Client suite flakes under load, before 1.35 too: `CoversService` (parts of 100, known folder), `LocationComponent
  header visibility` and `CopyPathService` fail in some full runs and pass alone. Rerun before chasing them.
- Client specs (Vitest + jsdom, zoneless TestBed): `await settle(ms)` lets timers, promises and change detection
  run. It waits on a real timer, so it hangs under `vi.useFakeTimers()`: use `vi.advanceTimersByTimeAsync` +
  `fixture.detectChanges()` there. An `async` helper must not return the promise under test (it gets awaited): wrap
  it, `{ result }`.
- jsdom sanitises `type=number` values (`' 300 '` becomes `''`), resets `cancelBubble` after dispatch, and has no
  modal `<dialog>`, `matchMedia`, `IntersectionObserver` or `isSecureContext`: `setup.ts` adds stand-ins, driven by
  `setMediaQuery` / `intersect` from `fakes.ts`.
- jsdom doesn't implement `HTMLMediaElement` `play`/`pause`/`load`: `video-player.component.spec.ts` stubs them on the
  prototype and calls `TestBed.resetTestingModule()` before `vi.restoreAllMocks()` (the player pauses on destroy).
- A template event handler whose expression evaluates to `false` makes Angular call `preventDefault()`:
  `(pointerdown)="flag = a === b"` silently cancelled the mouse events of every press in the dialogs (native
  `<select>`s stopped opening). Put assignments with a boolean result in a method.
- `TagPickerComponent` keeps a module-level "pointer pressed" flag: a test sending `pointerdown` must send
  `pointerup` too. A dialog that focuses its picker on open needs `blur()` + `focus()` to load suggestions again.
- `WebClientHasNoExternalResources` scans the specs too: use dotless host names (`http://nas:8080`) in tests.
- `EverySettingOfTheClientsSettingsDialogExists` reads `settings-schema.ts` with a regex (`{ key: '…', label: '…',
  type: '…'`): keep a field's first three properties in that order, on one line.

### How things work

- **Client routes:** `''` = `LibraryPageComponent` (the query string `?path=&q=&sort=&desc=1` says what it lists),
  `admin` = `AdminPageComponent` (`config`, its former address, redirects there), `player` = `PlayerPageComponent`,
  `login` = `LoginPageComponent` (`?return=`), `setup` = `SetupPageComponent`; the last three load lazily (`loadComponent`:
  their chunks keep the initial bundle under its 600 kB budget), so the admin page's guard lives apart
  (`admin/leave-admin-page.ts`). `<base href="./">` + `APP_BASE_HREF` from
  `document.baseURI` keep them working under a virtual directory, which is why routes stay one level deep; the
  server's `MapFallbackToFile("index.html")` serves `/admin`, `/player`, `/login` and `/setup`.
- **Sessions:** a cookie (`sfe.session`: user id, security stamp, library id; `SfeSession`), because `<img>`/`<video>`
  send cookies and nothing else. `OnValidatePrincipal` rejects it when the stamp changed (password, disabled, kind,
  admin flag) through `AccessService.GetSessionUserAsync` (cached until `AccessChanges.Version` moves). Data Protection
  keys persist in `Sfe:Security:KeysDirectory`. Pipeline: authentication → `RequestGuard` (503 `setup-required` until
  an administrator exists, except `api/setup` and `api/session`; 400 for non-GET `/api` without `X-SFE-Request`;
  `/api/docs` needs `system.apiDocs`) → authorization (`RequireRight` policies `right:a,b`, `RightHandler` reads
  `CurrentAccess`) → rate limiter (`sign-in` per address) → `TenantMiddleware`. Client: `Api` sends `X-SFE-Request` on
  non-GET and `X-SFE-Tenant` once `ConfigService.load` knew the library; a 401 opens `login?return=`, a 503
  `setup-required` opens `setup`, a 409 `tenant-changed` reloads (another tab switched); `redirected(error)` tells pages
  not to show "not reachable" then. Session/setup calls never redirect (their pages handle the answers).
- **Rights:** `RightsEvaluator.GetAsync(new AccessSubject(tenant, user))`: server administrators everything; others the
  union of their groups' rights in the library; a visitor without an account the Guests group's when the library has
  `GuestAccess` (and `Sfe:Security:AnonymousGuests`); minus the library's `TenantDisabledRights`, minus guest-forbidden
  rights for guests (anonymous or `UserKind.Guest`), minus `Sfe:Rights:Disabled` (applied at each read). A library
  membership is a group in it. `CurrentAccess` picks the library: the cookie's if the account still reaches it, else
  its first; for visitors the `sfe.guest` cookie's if open to guests, else the first library.
- **Space administrators** (`tenant.members` without `server.users`) only touch accounts already in a group of their
  space: `AccessEndpoints` 404s the others, keeps a shared account's other fields, and passes `membersOfTenantOnly` to
  the group methods. An account in their space alone (`OwnedBy`) is fully theirs (password too), so any route putting
  outside accounts into a space's group must stay server-wide.
- **Setup:** `SetupService`: required while no enabled server administrator exists; needs `Sfe:Setup:Code` (≥ 12,
  constant-time compare); creates the local admin in the first library's Administrators group, renames the library.
  `Sfe:Setup:Recover` re-opens it (the app keeps working) to reset a local admin. Program.cs creates the first library
  (`EnsureDefaultTenantAsync`) at every start and moves per-library keys out of `settings.json`
  (`SettingLayers.MovePerLibrarySettingsAsync`).
- **Directories:** `SignInService` asks the `IUserDirectory`s in `Sfe:Security:Directories` order (empty = local);
  `DirectoryStatus.Unknown` passes to the next. A non-local account gets a `Users` row on first sign-in (Source +
  ExternalId), its groups with an `ExternalName` follow the directory's groups at each sign-in. Only `LocalUserDirectory`
  exists (lockout after `MaxFailedSignIns`, PBKDF2-SHA512 210k with rehash of older hashes).
- **Settings by level:** environment (configuration, `Sfe:Locked`) → server (`settings.json`, `"locked": [...]`) →
  library (`TenantSettings` rows, `Locked` column) → user (`UserSettings` rows). `SettingLayers.Resolve(sources, level)`
  gives value, source, lock and inherited value per key; `States` adds editable/lockable for a page (scope, locks above,
  the caller's section rights). Saving keeps only values differing from the inherited ones (normalised first: config
  `""` vs saved `null`); changing a non-editable key is a validation error. A library's `SfeOptions` = config +
  `SettingLayers.ApplyTo(options, tenantId)` (post-configure; `SettingLayers` is also a change-token source).
  `api/settings?level=server|tenant` (default: server for `server.settings`), `PUT api/settings/locks?level=`,
  `api/me/preferences`. Client: `SettingsForm.level` (`levels()` from rights, remembered in `sfe.settings.level`;
  `forUser()` for My account), `states()` per key (null from old servers: then everything editable, server wording),
  lock boxes and notes in `SettingsPanel`.
- **Libraries (tenants):** `TenantRegistry` (hosted service) starts a `TenantHost` per enabled library: a
  `ServiceCollection` with `AddSfeCore(config + overrides, contentRoot, tenantId)`, the application's log outputs,
  `SettingsStore`/`SettingLayers`/`FfmpegLimits` instances, and the workers; it initializes the library's index. The
  first library uses the configuration as it is; others get `Sfe:Root` = their root, their connection string
  (`TenantHost.ConnectionString`: template, else `tenants/{slug}.db` beside the SQLite file or `{db}_{slug}` on SQL
  Server) and `{CacheDirectory}/tenants/{slug}`. `TenantMiddleware` swaps `HttpContext.RequestServices` for
  `TenantRequestServices` (Core types from the library, the rest from the request) for `/api` and `/openapi`, except
  setup/session/docs.
- **Administration page:** the ⋮ menu keeps only the account line, Scan / Remove from index, the account's other
  libraries (`config.tenants`; switching reloads the page), Administration, Toggle fullscreen and Sign out/in, each by
  right; everything else lives in `AdminPageComponent` (sections × sub-pages from `admin-sections.ts`, filtered by
  `visibleSections(canAny)` on each sub-page's `rights`; none = "Nothing to administer"; the open section lists its
  sub-pages under it in the sidebar, phones show them as chips (`role=tab`) instead; only the active sub-page is
  rendered, so sections poll from their constructor and stop on destroy). A settings sub-page shows one tab of
  `settings-schema.ts` (ids differ: `locateSettings(tab)` finds its section and sub-page). The per-browser theme is
  the `browserTheme` group action of Interface › Appearance, outside the form. It is opened with history state
  `sfeFromLibrary`; section/sub-page changes `replaceUrl` and carry that state, so its back arrow is `history.back()` (the listing restores scroll and
  items) and otherwise navigates to `ListingStore.route`. Read the state with `Location.getState()` (tests mock it).
  `SettingsForm` is provided by the page: unsaved changes survive section changes, mark the nav (`changedTabs`,
  `faultyTabs`; on the section, or on its sub-pages while it is open), and `leaveAdminPage` (canDeactivate) + `beforeunload` ask before leaving. The listing page is
  created again on return: its first creation restores from `initialHistoryState`, later ones from `history.state`.
  Phones (`(max-width: 760px)`, the breakpoint of `admin.css`, read with `matchMedia`): `/admin` without `section` is
  the list of sections (`sectionList`), a section hides it; links from the list push an entry with state `sfeFromList`
  (kept by `go`), so back (arrow or browser) returns to the list.
  `TagManagerDialog` stays on the listing page (sidebar, tag dialogs) around the same `TagListComponent`.
- **Listing page:** `LibraryPageComponent` loads `/api/config` (`ConfigService`), then turns each `NavigationEnd`
  into `ListingStore.route` + `load()`; on an imperative `NavigationStart` it writes `scrollY`/`loaded` into the
  current `history.state`, and a `popstate` navigation restores them (`restoredState`; the page opened with
  `initialHistoryState`, read before the router replaces it). `ListingStore` (root) holds route, items, pages,
  siblings, selection and the `generation` signal: services keyed on a listing (`CoversService`,
  `TrickplayHoverService`) reset in an `effect` on it. Navigate with `store.navigate(changes, { replace })` or a
  `routerLink="/"` + `store.browseParams(path)` (keeps the sort). Item objects are replaced, never mutated
  (`applyFileTags`/`applyTagChanged`/`applyTagDeleted`): track lists by `path`. Dialogs register themselves in
  `Dialogs` (`registerDialog`) so any part opens them: `dialogs.fileTags()?.open(item)`. Infinite scroll: the
  sentinel's `IntersectionObserver` in `ListingComponent` re-observes after the next render (`afterNextRender`),
  otherwise it would still see the sentinel and load every page.
- **Client details:** the multi-file tags dialog (`FilesTagsDialog`) sends changes in parts of
  `config.maxFilesPerTagChange`.
- **Folder / search tagging:** `ScopeTagsDialog` (location header's tag button, `.folder-tags-button` in
  `sfe-folder-item`'s `.folder-actions`) never lists the files: `GET|POST api/file-tags/scope` (`path`, `recursive`,
  `q`) → Core `ScopeTagger`, which counts tags with one GROUP BY and changes files by batches of 1000 keyed on
  `Files.Id` (a search whose matches depend on the changed tag still reaches every file). A folder scope runs
  `DirectoryIndexer.IndexAsync` on it first (409 while a scan runs); a search scope reuses
  `FileSearchService.Matches` (hidden-tag rule included). Every change is confirmed in the dialog's `.scope-confirm`
  box; a new tag is created only on confirming. The listing is patched with `ListingStore.applyScopeTags` (by path
  for folders; for a search, every listed file when the route is that search), not reloaded. `VideoPlayerComponent` is shared by the viewer and the player page; the seek
  icons get the step (`config.seekSeconds`) overlaid; the viewer gives each item its own elements
  (`@for` over a one-item list tracked by path). Folder nav (up/prev/next by the crumbs): `loadSiblings` asks
  `api/browse/siblings` after the listing, with the current sort; `LibraryBrowser.Siblings` reuses the parent's
  cached sorted listing (`GetListing`), so stepping through a big parent lists it once. Folder cards (grid and
  single views, `.folders.covers`; off when `Browse:FolderPictures` = 0, sent as `/api/config` `folderPictures`): an
  `IntersectionObserver` in `CoversService` batches the cards near view into `POST api/browse/covers` (≤ 100 paths);
  `LibraryBrowser.Covers` keeps the first N images/videos by name (top-k over at most 5000 entries, videos only if
  `ThumbnailService.IsAvailable`), cached per folder by its `LastWriteTimeUtc` and N; the cells reuse
  `img[sfePreview]` (`PreviewImageDirective`: tries `PreviewsService.sources(item)` in turn), laid out ⌈√N⌉ columns
  wide (`--cover-columns`/`--cover-rows`). `ConfigService.isLargeImage` (size > `config.largeImageSize`, thumbnails
  on) picks thumbnail-only vs original in listings and the viewer; `Thumbnails:SingleViewOriginals` (`/api/config`
  `singleViewOriginals`) makes `FileTileComponent` ask `PreviewsService.sources(item, true)` in the `single` view.
- **Search explanation:** `library/search-explain/search-explain.ts` re-implements `SearchQuery.Parse` client-side
  (same quirks: `*` only at the start, `-e:`/`-s:`/`-w:` negate the filter, invalid sizes dropped) to word the query live
  as `Segment`s; a parser change needs the same change there (and in `search-explain.spec.ts`). It reads the box as
  typed (`SearchBoxState.text`), not the route; off with `Search:Explain` (`/api/config` `searchExplain`); open
  state in `localStorage`.
- **Compact location bar:** `LocationStripComponent` (before `section[sfeLocation]` in `.page`) is `position: sticky` with
  height 0, its bar absolute, so showing it moves nothing; shown while `LocationLinks.headHidden` (set by an
  `IntersectionObserver` on `.location-head` in `LocationComponent`). `LocationLinks` holds the crumbs and folder links
  for both, `nav[sfeFolderNav]` draws the buttons. It sticks below the scan panel: `ScanPanelComponent` writes its height
  to `--scan-panel-h` on `<html>` (ResizeObserver, 0 while hidden).
- **Scroll progress:** `ScrollProgressComponent` (in `sfe-listing`) counts the children of `.content > .folders` /
  `.files` whose top is above the screen bottom (binary search, reading order), on scroll/resize (rAF) and after items
  render; total = `ListingStore.total` (browse `total`; search `total`/`totalCapped` from its first page only,
  `FileSearchService.CountLimit` = `Take(10001).Count`), pages of `store.pageSize()`.
- **Same-URL navigation reloads** (`onSameUrlNavigation: 'reload'`): `load()` empties the list and scrolls to the top.
  The search box's `searchFor` skips its own query (Enter after a live search used to reset the scroll); `load(restore)`
  takes more pages while fewer than `loaded` came back (pages are capped by `MaxPageSize`).
- **Position restore:** the history state holds `scrollY`, `loaded` and `anchor`/`anchorTop` (the item at the top of the
  screen, `items/listing-position.ts`), written on leaving and 250 ms after scrolling stops: Chrome ignores a
  `replaceState` in `pagehide` on reload. `restorePosition` scrolls the anchor back (previews have other sizes after a
  reload) and holds it there 2.5 s while rows resize, until wheel/touch/key/pointer. Folder items carry `data-path` too.
- **Search history:** `SearchHistory` (`library/search-history.service.ts`) keeps the last searches in localStorage
  (`sfe.searchHistory`), per browser since there are no accounts; the server only sends the size (`Search:HistorySize`
  → `/api/config` `searchHistory`, 0 = off). The search box shows them in the tag suggestion list while no `t:` term is
  at the caret, nothing highlighted at first (Enter still submits the box); it records on submit, on a pick, and on blur
  when the box holds the route's query (a live search).
- **Theme:** `styles/base.css` has the dark palette on `:root`, the light one on `:root[data-theme="light"]`; there
  is no `prefers-color-scheme` rule, because JS always sets `data-theme` to `light`/`dark`. The classic script
  `src/theme-boot.ts` (in `<head>`, before the CSS) sets it before first paint from localStorage: `sfe.theme`
  (the browser's pick, Administration › Interface › Appearance) or `sfe.theme.default` (the last server default it saw). Then `ThemeService` takes
  over from `/api/config` `theme` (`Sfe:Appearance:Theme`, enum `Theme`) and follows device changes. Keep the two in
  step. The player page stays dark: `theme-boot.ts` forces it for `/player`, and nothing there injects
  `ThemeService`. `Theme` has a `JsonStringEnumConverter` attribute, so
  `settings.json` stores the name; the API's camelCase converter wins over it, so the API sends `light`.
- **Copy full paths:** `Browse:CopyPaths` (off by default); `/api/config` `rootPath` = `LibraryPaths.Root` (ends
  with the server's separator) only when on, null otherwise, so the server layout isn't leaked. `CopyPathService` joins
  it with the API path using that last character; `navigator.clipboard` only exists on secure pages, so plain-http
  LAN use falls back to a hidden `<textarea>` + `execCommand('copy')`, appended inside the open `<dialog>` (the rest
  of the page is inert under a modal). `CopyPathService.copied` (in memory only, gone on reload) gives tiles, rows and
  folders the class `copied` (dashed outline). A folder link sits in `sfe-folder-item` (`.folder-item`) next to its button
  (no button inside `<a>`); list rows put buttons in `.row-actions` (last grid column `auto`).
- **External player:** `Player:ExternalUrl` template (`{url}`, `{encodedUrl}`, else appended) + `ExternalName`, sent
  as `/api/config` `externalPlayer` (null when empty or without `files.external`); `ConfigService.externalLink(path)`
  asks `POST api/files/links` for a token link (`FileLinks`: `ITimeLimitedDataProtector` over tenant, user, stamp,
  path; `Player:LinkLifetimeMinutes`) unless a data server is set, then fills the template (`urls.external(fileUrl)`).
  `GET api/files/shared/{token}/{name}` (anonymous) checks the token, the account's stamp and rights, and serves the
  file from the token's library (`TenantRegistry`). `PlayerOptions.IsAllowedExternalUrl` refuses `javascript:`/`data:`…
  schemes (the link runs in every user's page); the config endpoint checks it again for values from appsettings.
- **Data server:** `Sfe:DataServer` is a base (`/` + path appended, trailing slashes trimmed) or a template with
  `{url}` (path, segments encoded) / `{encodedUrl}` (`encodeURIComponent` of the path), kept as written;
  `DataServerUrl` (Core) normalises and validates (http/https once filled), `dataServerUrl` in
  `core/config.service.ts` fills it. Not the external player's `{url}`, which is the file's full address.
- **Resolution:** `Files.Width`/`Height` (null = not read yet, 0 = read but not understood, so not read again until the
  file changes). The indexer reads them (`Scan:ReadResolution`) for new/changed files and for unchanged ones still null,
  like missing fingerprints; videos only while ffmpeg is available (`MediaDimensions.CanRead`). Images: headers of
  JPEG (with EXIF orientation 5–8 swapping the sides), PNG, GIF, BMP, WebP, else `ffmpeg -i`; videos: `ffmpeg -i`
  stderr, first non-`attached pic` video stream, rotation ±90 swaps. A changed file is attached with its stored
  Width/Height so that clearing them is written. Listings: `LibraryItem.Width/Height` only when > 0, browse fills them in
  `TagService.AttachIndexDataAsync` (with the tags), search from its row. Search: `w:`/`h:`/`r:WxH` → `DimensionFilter`s
  (no operator = equal), predicates require `> 0`. Display: `Browse:ShowResolution` → `/api/config` `showResolution`,
  `ConfigService.resolution(item)`.
  HR/LR marks: `Browse:ResolutionMark` + `Image|VideoHighResolutionPixels` → `/api/config` `resolutionMark`
  (`{ image, video }`, null = off), `ConfigService.resolutionMark(item)` compares the shorter side with its kind's;
  tiles put it in `.preview-marks` with the media `.badge` (bottom right), rows in the thumbnail; the trickplay hover canvas is inserted under them.
  `r:hr`/`r:lr` (`ResolutionLevel`, `SearchGroup.Levels`) use the same thresholds whether the marks are on or not:
  `FileSearchService` builds a `HighResolution` (thresholds + `MediaClassifier.Extensions(kind)`) for
  `SearchPredicateBuilder.Build`; HR = both sides ≥ the kind's threshold (= shorter side), LR = known and below.
- **Duration:** `Files.Duration` (seconds, `double?`; null = not read, 0 = ffmpeg gave none) is read with the
  resolution, from the same `ffmpeg -i` run (`MediaDimensions.ParseDuration`, the input's `Duration:` line), for videos
  and audio (`MediaProperties`; audio has `Size` null, so its Width/Height stay null and `w:`/`h:`/`r:` treat it as
  unknown, as before). Added in 1.33.0 (migration `VideoDuration`): `DirectoryIndexer.LacksMediaProperties` re-reads
  a video whose Width or Duration is null and an audio file whose Duration is null, so the first scan after the
  upgrade runs ffmpeg once over every indexed video and audio file. Listings: `LibraryItem.Duration` only when > 0 (`AttachIndexDataAsync` reads audio rows too); display
  `Browse:ShowDuration` → `/api/config` `showDuration`, `ConfigService.duration(item)`: tiles show it as a `.badge.duration`
  in `.preview-marks.start` (bottom left, before a video's trickplay mark, which moved into that group) and leave it
  out of `.meta` (`FileTileComponent.durationBadge`): videos with a preview, and audio always, over its placeholder
  (`.tile.kind-audio` overrides the rule hiding badges until an image loads); preview-less videos keep it in `.meta`.
  Rows put it in the size cell, the viewer in its details. Search: `d:` → `DurationFilter` (predicates require `> 0`).
- **Picture editor:** the browser edits, the server stores. `editor/edits.ts`: an edit is an `EditOperation` (kind +
  parameters) with an `EDIT_KINDS` handler (`size`, `draw` on a 2D canvas, `describe`) and a tool in `EDITOR_TOOLS`;
  `renderEdits` applies the list in turn from the original (each step a new canvas), `encode` = `canvas.toBlob` in the
  original's type if JPEG/PNG/WebP, else PNG, at `config.editor.quality`. A new edit (rotate, flip, resize) = a new
  operation + handler + tool (+ its panel in `ImageEditorDialog`); nothing changes on the server. The dialog loads the
  original through `urls.original` (always `api/files/raw`: a data server's picture would taint the canvas) via
  `PICTURE_LOADER` (tests provide fake pictures; `fakeCanvases()` stubs `getContext`/`toBlob`). Crop coordinates are in
  pixels of the current result (`crop.ts`, whole pixels; `ASPECTS` lists each ratio with its portrait twin, which
`flipAspect` relies on; picking one = `largestAround` the frame's centre); drags scale screen → picture pixels by the
  `.crop-layer` box (`.crop-clip` around it reaches past the picture so edge handles aren't cut). Save:
  `POST api/editor/images?path=` with the raw bytes (no JSON, any Content-Type) → `EditedImages.SaveAsync`: buffers
  ≤ 200 MB (`RequestSizeLimitAttribute` lifts Kestrel's 30 MB), sniffs JPEG/PNG/WebP and needs a readable size
  (`MediaDimensions.ReadImageHeader`), writes `FileMode.CreateNew` to `Editor:OutputFolder` + the original's folder
  (`name (2).ext`...; empty setting or already inside it → beside the original, `name (edited)`), then
  `DirectoryIndexer.IndexFilesAsync`. Errors are 404/400/413/500, never 409 (the client's `Api` resolves 409s).
  `editor.outputFolder` is per library (`PerLibraryKeys`); `/api/config` `editor` is null without `files.edit`.
- **Caching:** images/thumbnails are sent `no-store`; trickplay sheets are cacheable (URL carries its `version`).
- **Trickplay:** Core's `TrickplayService` run by the Api's `TrickplayWorker`. Per video
  `{CacheDirectory}/trickplay/{SHA1(folder)}/{SHA1(file)}/`: `info.json`, `{n}.jpg`, `fingerprint.bin`. Fresh =
  size + mtime + `ImageKey`; failures recorded. `PassSettings` changes cancel and restart the pass. ffmpeg
  `fps=1/N:round=up` (default rounding is half an interval early), then a second run tiles. A pass = one discovery
  task filling `_pending` (`PendingVideos`) + `Parallelism` workers; player requests go to `_requested` first.
  `GetStates` / `GetActivity` feed the listing marks and the Trickplay › Activity tab; the viewer polls while waiting.
- **Hover preview:** `TrickplayHoverService.attach` (called by the tile/row after render) draws the cells on a `<canvas>` in the thumbnail box,
  started by hovering the whole tile/row, or on touch by holding it (`pointerdown` + `HOLD_DELAY_MS`; the click after
  a hold is swallowed in the capture phase, `contextmenu` prevented, callout/selection off in CSS). Off with
  `Trickplay:HoverPreview` (`/api/config` `trickplayHover`). It asks with `?queue=false`
  (`TrickplayService.Get(path, queue: false)`) so hovering never queues a video.
- **Background tasks:** `BackgroundTasks` describes/starts/cancels scan, trickplay pass, similar videos and thumbnails
  (thumbnails: cancel only, `ThumbnailService.CancelAll`). Each run of the first three is a `TaskRuns` row written at
  start (`TaskHistory.Begin`) and completed at the end with outcome, summary and log JSON; rows still `Running` at
  start-up become `Interrupted` (`RecoverAsync`); purge after `Tasks:LogRetentionDays`. Logs: `TaskLogProvider` (an
  `ILoggerProvider`, filter `Sfe` ≥ Debug) copies entries of the async flow whose `AsyncLocal` `Begin` set — call
  `Begin` from the run's own method (an awaited async method restores the caller's value on return); tasks/threads it
  starts inherit it. Long logs keep the first and last 1000 lines. A new job: give it a trigger + `Begin`/`CompleteAsync`
  and a case in `BackgroundTasks`.
- **Schedules:** `TaskSchedules` (UTC `Start`, `Kind` Once/Interval/Weekly, `Days` bit 0 = Sunday; "every day" =
  weekly with all days). `ScheduleCalculator.Next` is strictly after a moment; weekly uses the server's time zone
  (skipped DST times run later, repeated ones the first time). The loop (`TaskScheduleService.RunAsync`, hosted by
  `TaskScheduleWorker`) starts what came due since its last check, never before `Updated` (no runs for the past),
  sleeps until the next occurrence (≤ 30 min) or a schedule change; missed runs aren't caught up; due while running →
  a `Skipped` run (a trickplay pass is queued instead). Trickplay's own `RecheckHours` pass stays; its time is
  `TrickplayStatus.NextPass` (`automaticNextRun`). The client's schedule form previews with `POST schedules/preview`
  (same calculation) rather than computing times itself.
- **Index folder removal:** `DELETE api/index/folder?path=&exclude=` → `ScanCoordinator.RemoveFromIndexAsync` (409
  while a scan runs) → `DirectoryIndexer.RemoveTreeAsync` (the scan's `DeleteTreeAsync`, so tagged files become
  missing files); `exclude` adds to `Scan:ExcludedFolders` (`SettingsService.ExcludeFolderAsync`), which
  `IsIgnoredDirectory` checks by full path next to the ignored names.
- **Missing files / relink:** the indexer never deletes a tagged file outright: `MissingFileRecorder` (static,
  avoids a DI cycle RelinkService → TagService → DirectoryIndexer) copies it to `MissingFiles`/`MissingFileTags` in
  the delete's transaction. Matching runs once per scan after `IndexAsync`. Auto-relink: single candidate (size +
  `Sha1`, or name/ext/size/mtime without fingerprint) registered *after* the original `RegisterTime`, same name breaks
  ties; the thumbnail/trickplay cache moves with it.
- **Search sort:** `SortField.Path` (search default; browse treats it as `Name`) or name/modified/size/type, ties by
  ascending `FullName` in both directions. Keyset cursor: bare path for `Path`, else `{value}/{path}` (the value has
  no `/`; a file without extension has `Extension` = `""`); the client must page with the sort it started with. Client: `route.sort` `''` = default,
  `currentSort` in `library/library-route.ts`.
  The match count (`Take(10001).Count`) is unordered on purpose: EF's `RowLimitingOperationWithoutOrderByWarning` is
  ignored in `SfeDatabase.Configure` (even `ORDER BY (SELECT 1)` makes SQLite sort every match), so nothing warns
  about an unordered page any more: order every `Take`.
- **Duplicates:** group by `(Size, Sha1)` on `IX_Files_Size_Sha1`; keep the query shape (no `InTree` for the whole
  library, order `Size DESC, Sha1 DESC`) for SQLite's covering index plan. The fingerprint (`FileFingerprint`) is SHA1 of the first MB.
  `DuplicateRemover` must keep the byte comparison (`FileIdentity.SameContentAsync`) and the same-file refusal
  (`FileIdentity.SameFile`: links in every segment, volume/file id on Windows); order merge tags → delete file →
  delete row.
- **Similar videos:** `fingerprint.bin` = per trickplay frame a dHash of 9×8 grey pixels + contrast
  (`VideoFingerprint`), made from the sheets with ffmpeg `untile` (no image library); trickplay without one (ffmpeg failed)
  is not fingerprinted again until it is remade. `SimilarVideoFinder` (on request, background) reads them for indexed videos;
  `VideoMatcher` gets candidates from a sorted 3-band index with offset votes, scores at the best offset. Match % =
  matched frames / informative frames of the longer video. Results in memory only (no table), cursor `{run}:{index}`.
- **Version:** `AppVersion` splits the informational version (`1.0.0+<sha>`, commit added by the SDK) for
  `/api/config` (`version`, `commit`; shown at the bottom of the ⋮ menu and in Administration › System), OpenAPI and the start-up log. Build scripts
  read it with `-getProperty:Version`, publish with `-p:Version`, name packages `SFE-<version>-…`; smoke tests check
  the app reports the version in the package name.
- **Portable build:** `-p:SfePortable=true` = self-contained single file + `build/portable/appsettings.json` +
  `SfePortable` assembly attribute, which makes `PortableBuild` use the exe folder as content root (an explicit
  `--contentRoot` wins: the AppImage's `AppRun` passes `~/.config/sfe`). `src/Sfe.Api/data/` is never published.
  `build.ps1` publishes linux-x64 on Windows; only `appimagetool` and the smoke test run in WSL; the AppDir is built
  under `/tmp` (files on `/mnt/c` all look executable). Smoke tests: start the package on a temp library, check
  version, client (`<sfe-root` + its hashed `main-*.js`), browse, scan, search, data folder.
- **Config files:** `src/Sfe.Api/appsettings*.json` hold this machine's values (Windows paths, `REVAN\SQLEXPRESS`;
  Development: SQLite in `src/Sfe.Api/data/`, git-ignored, root fixed to a local folder although the README says `~`).

## 4. External resources

- TagStudio (tag model): https://github.com/TagStudioDev/TagStudio
- Angular (client framework, signals, control flow): https://angular.dev/
- ffmpeg: https://ffmpeg.org/ (filters used: `fps`, `scale`, `tile`, `untile`, `format`)
- appimagetool 1.9.1 and AppImage type2-runtime (pinned, downloaded into `artifacts/tools` by the build):
  https://github.com/AppImage/appimagetool, https://github.com/AppImage/type2-runtime
- ASP.NET Core Hosting Bundle (IIS): https://dotnet.microsoft.com/download/dotnet/10.0
