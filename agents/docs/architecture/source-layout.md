# Source layout

```
src/Sfe.Core/           Domain library, no ASP.NET dependency; DI in ServiceCollectionExtensions (singletons):
                        AddSfeCore = one library's services, AddSfeAccess = the server's accounts and settings levels
  Access/               RightCatalog (+ Rights ids), AccessService (libraries, accounts, groups), RightsEvaluator,
                        SignInService + IUserDirectory/LocalUserDirectory, SetupService, GeneratedSetupCode (the code the
                        server makes when none is configured), PasswordHashing, AccessChanges,
                        QuickLinkService (each user's quick links per library)
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
  Streaming/            VideoStreamer (probe + ffmpeg → fragmented MP4 while it plays), VideoStreams (server-wide list + limit)
  FileTracking/         DuplicateFinder/Remover, RelinkService, MissingFileRecorder, SimilarVideoFinder, VideoMatcher
  Editing/              EditedImages (saves the web editor's pictures as new files: format check, name, index)
  Upgrades/             InstalledVersion (the install's version, ServerProperties table), Changelog (CHANGELOG.md parser),
                        SemanticVersion
    Steps/              IUpgradeStep + UpgradeSteps.All (changes outside the DB schemas), one folder per version:
                        V1_40_0/ShardCacheLayout (caches → ab/cd/rest)
  Logs/                 AuditLogProvider (every logger's entries, queued) + AuditLogService (database, daily files,
                        search, purge)
  Backup/               BackupWriter (zip of databases + config files, manifest, README)
  Tasks/                BackgroundTasks (status/start/cancel of every job), TaskScheduleService (+ loop),
                        ScheduleCalculator, TaskHistory (TaskRuns), TaskRunLog + TaskLogProvider (run logs)
  Settings/ Options/    SettingsStore (server level, settings.json)/Service, EditableSettings, SettingCatalog (scope and
                        right of each key), SettingLayers (resolution by level), SettingRows (library/user rows) /
                        SfeOptions ("Sfe" section), AccessOptions (Setup, Security, Rights, Tenants)
  SystemCommands/       shutdown / dismount through the shell
src/Sfe.Api/            Program.cs, Endpoints/*Endpoints.cs (one class per area), TrickplayWorker, TaskScheduleWorker, AuditLogWorker, PortableBuild,
                        AppVersion, StartupErrors (bad JSON, port in use), BrowserLauncher (portable: open the browser),
                        WindowsServiceSetup (--install-service); wwwroot/ = the built client (generated, git-ignored)
  Security/             cookie session (SfeSession), CurrentAccess (the request's account, library, rights), RequireRight +
                        policy provider, RequestGuard (setup gate, X-SFE-Request, /api/docs), FileLinks (player tokens),
                        InstallationAccess (setup code -> token of the installation page)
  Tenancy/              TenantRegistry/TenantHost (one AddSfeCore container per library), TenantMiddleware +
                        TenantRequestServices (routes get their library's Core services)
  Maintenance/          MigrationCoordinator (version check at start-up, the update gate, the update itself),
                        BackupSources (what a backup holds) + BackupDownloads (one-time links of the update page),
                        InstallationSettings (data/installation.json: root, Urls, ffmpeg from the web)
src/Sfe.Web/            the web client (Angular workspace: angular.json, package.json, proxy.conf.json)
  public/               copied as is: favicon.svg
  src/theme-boot.ts     classic script run before first paint (theme), compiled alone by `npm run boot`
  src/testing/          setup.ts (jsdom stand-ins, angular.json test > setupFiles), fakes.ts (mockApi, builders, settle)
  src/styles.css        imports styles/*.css, one partial per area of the page
  src/app/core/         Api (the only HTTP client), models (API shapes), ConfigService (+ Urls, rights: can/canAny),
                        rights (right ids, SETTINGS_RIGHTS), format, paths, preferences (localStorage), ToastService,
                        ThemeService, CopyPathService
  src/app/account/      LoginPage (`login`), SetupPage (`setup`: code, library, administrator), SetupCodeSteps (how to set
                        the code), InstallationForm (folder, network, ffmpeg; also System › Installation), AccountSection
  src/app/migration/    MigrationPage (`migration`: the update after an upgrade), changelog.ts (inline Markdown of items)
  src/app/access/       People (Accounts, Groups, GuestAccess) and Libraries sections of the admin page
  src/app/shared/       icon + IconSprite, SheetComponent (dialog card), BusyDirective, highlight, toasts, empty state,
                        FolderPicker (click through the server's folders)
  src/app/library/      the listing page: LibraryPageComponent, ListingStore (state), library-route, Dialogs registry,
                        topbar/ location/ items/ search-explain/ scan/ sidebar/ quick-links/ viewer/, selection bar
  src/app/player/       VideoPlayerComponent, PlayerPageComponent (`player?path=&t=`)
  src/app/admin/        AdminPageComponent (`admin?section=&page=`), admin-sections.ts (sidebar groups, sections, sub-pages), System section
  src/app/settings/     settings-schema.ts, SettingsForm (one form for every settings tab), SettingsPanel (one tab)
  src/app/editor/       ImageEditorDialog (over the viewer), edits.ts (operations, EDIT_KINDS, renderEdits), crop.ts
  src/app/tags/ trickplay/ tracking/   their dialogs, sections of the admin page and parts
  src/app/logs/         LogsSection (Administration › Logs › Log), logs.ts (filters → api/logs query)
  src/app/tasks/        TasksSection (sub-pages Tasks/Schedules/History of the admin page), ScheduleForm, TaskHistory, tasks.ts wording
tests/Sfe.Tests/        unit, SQLite integration, HTTP tests; Infrastructure/ = TestLibrary + CoreServices + ApiClients
build/                  build.ps1 / build.sh (portable packages), portable/appsettings.json, windows/ linux/ smoke tests,
                        windows/*.cmd (install-service / uninstall-service, published in the win-x64 package)
agents/docs/            these notes for agents, one small file per topic, indexed by AGENTS.md
agents/tools/           reusable helpers for agents, one folder per job, each with a README.md
  docs-screenshots/     regenerates docs/images (run.mjs = everything; cdp.mjs = headless Edge driver) + check-links.mjs
CHANGELOG.md            what each version added or fixed (1.33.0 on; earlier versions are not recorded)
docs/                   end-user documentation: installation, first-start, configuration, upgrading, user-guide/,
                        administration/ (one page per admin section), reference/, images/ (screenshots)
Directory.Build.props   the app version (all projects)
```
