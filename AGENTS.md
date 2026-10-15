# AGENTS.md

Guidance for AI agents working on this repository. This file is an **index**: the basic rules, then where each
functionality lives (its notes in `agents/docs/` and its code). `README.md` is the short overview for users, `docs/` the
end-user documentation; building, testing and the code are described in `agents/docs/`.

**SFE — Simple File Explorer**: self-hosted web file explorer for a home media library (browse, preview
photo/video/audio, index, search, tag, find duplicates and similar videos), with accounts, rights and several libraries.
.NET 10 minimal APIs + EF Core (SQL Server and SQLite) in `src/Sfe.Core` / `src/Sfe.Api`, Angular client in
`src/Sfe.Web`, tests in `tests/Sfe.Tests`. **It is in production**: every change keeps existing installs working.

## Keeping these notes current

- Add what the next agent would need (a convention, a gotcha, a command that works or fails), fix or delete what is
  wrong. One fact per bullet, short.
- Details go in `agents/docs/<area>/<topic>.md`, **one small file per functionality**: prefer a new file over growing
  one that already covers several things. Each file starts with its title and the code files it is about.
- This file stays an index: a new topic file gets one line in the table below (what it covers, where the code is);
  never put implementation details here.
- Areas: `architecture/` (how the app is put together), `development/` (rules for changing it), `environment/`
  (commands, build, this machine), `features/` (one file per function of the app).
- Link between notes with relative links; `node agents/tools/docs-screenshots/check-links.mjs` checks them.

## Basic rules (details in the linked notes)

- **Production:** a change to the database, stored files, caches or settings ships with its tested migration path
  ([production-upgrades](agents/docs/development/production-upgrades.md)); both DB providers must work
  ([database](agents/docs/development/database.md)).
- **Version + changelog** once per commit: patch for a fix, minor for a feature, a `CHANGELOG.md` section with it;
  none for docs/tests/build only ([versioning-and-changelog](agents/docs/development/versioning-and-changelog.md)).
- **Tests** come with every feature and fix; not done until `dotnet test` and `npm test` pass
  ([testing](agents/docs/development/testing.md)).
- **User docs:** user-visible changes go in `docs/` with screenshots ([user-docs](agents/docs/development/user-docs.md)).
- **Rights:** every route names its right; new functions get one ([rights](agents/docs/features/rights.md)).
- **Scale:** millions of files; nothing proportional to a folder or the index per request
  ([scale](agents/docs/development/scale.md)).
- **Fully local:** no external URL in the client ([fully-local](agents/docs/development/fully-local.md)).
- **Git:** work is committed on `master` (PR branch `main`), messages `claude: <short summary>`; commit only when asked.
- **Agent tools:** put any reusable helper (scripts, drivers, generators) in `agents/tools/<job>/` with a `README.md`,
  never only in the scratchpad; work output goes to a temp folder, not the repo.

## Index

### Architecture — `agents/docs/architecture/`

| Note | Covers | Code |
|---|---|---|
| [stack](agents/docs/architecture/stack.md) | technologies, external references | `Sfe.slnx` |
| [source-layout](agents/docs/architecture/source-layout.md) | **what each folder holds** (start here to find code) | `src/`, `tests/`, `build/` |
| [tenants](agents/docs/architecture/tenants.md) | libraries: one Core container per library, DI rules | `Sfe.Api/Tenancy/` |
| [sessions-and-pipeline](agents/docs/architecture/sessions-and-pipeline.md) | cookie session, request guard, middleware order, client redirects | `Sfe.Api/Security/`, `core/api.service.ts` |
| [client-routes](agents/docs/architecture/client-routes.md) | Angular routes, lazy loading, virtual directory | `src/Sfe.Web/src/app/` |
| [config-files](agents/docs/architecture/config-files.md) | appsettings files and source order | `Sfe.Api/appsettings*.json`, `Program.cs` |
| [app-version](agents/docs/architecture/app-version.md) | version at run time, in packages | `AppVersion`, `Directory.Build.props` |

### Development rules — `agents/docs/development/`

| Note | Covers | Code |
|---|---|---|
| [production-upgrades](agents/docs/development/production-upgrades.md) | migration paths, upgrade steps | `MigrationCoordinator`, `Core/Upgrades/Steps/` |
| [versioning-and-changelog](agents/docs/development/versioning-and-changelog.md) | version bumps, `CHANGELOG.md` | `Directory.Build.props` |
| [database](agents/docs/development/database.md) | SQL Server migrations + SQLite path, schema | `Core/Data/` |
| [scale](agents/docs/development/scale.md) | paging, batches, snapshots | |
| [paths](agents/docs/development/paths.md) | root-relative paths, case | `Core/Paths/LibraryPaths` |
| [api-routes](agents/docs/development/api-routes.md) | minimal API conventions and gotchas | `Sfe.Api/Endpoints/` |
| [code-style](agents/docs/development/code-style.md) | C# style | |
| [frontend](agents/docs/development/frontend.md) | Angular conventions, CSS, icons | `src/Sfe.Web/src/` |
| [fully-local](agents/docs/development/fully-local.md) | no external resources | `ApiTests` |
| [user-docs](agents/docs/development/user-docs.md) | what goes in `docs/`, README, OpenAPI | `docs/` |
| [testing](agents/docs/development/testing.md) | test rules and helpers | `tests/Sfe.Tests/Infrastructure/`, `src/testing/` |
| [testing-backend](agents/docs/development/testing-backend.md) | SQLite pool, ffmpeg tests, junctions | `tests/Sfe.Tests/` |
| [testing-client](agents/docs/development/testing-client.md) | Vitest/jsdom gotchas | `src/testing/setup.ts` |

### Environment — `agents/docs/environment/`

| Note | Covers |
|---|---|
| [commands](agents/docs/environment/commands.md) | build, test, run, migrations, packages |
| [build](agents/docs/environment/build.md) | client build inside MSBuild, theme-boot, build errors, locked DLLs |
| [portable-build](agents/docs/environment/portable-build.md) | portable zip, AppImage, smoke tests |
| [running-the-app](agents/docs/environment/running-the-app.md) | the app on a scratch library |
| [visual-check](agents/docs/environment/visual-check.md) | headless Edge screenshots and clicks |
| [docs-screenshots](agents/docs/environment/docs-screenshots.md) | regenerating `docs/images/` |
| [dev-machine](agents/docs/environment/dev-machine.md) | this machine: node, ffmpeg path, shells |
| [line-endings](agents/docs/environment/line-endings.md) | LF/CRLF, Git Bash pitfalls, git warnings |

### Features — `agents/docs/features/`

| Note | Covers | Code |
|---|---|---|
| [rights](agents/docs/features/rights.md) | adding a right, how rights are evaluated | `Core/Access/RightCatalog`, `RightsEvaluator`, `core/rights.ts` |
| [accounts](agents/docs/features/accounts.md) | sign-in directories, space administrators | `Core/Access/`, `app/access/`, `app/account/` |
| [setup](agents/docs/features/setup.md) | setup code (configured / generated), first administrator | `SetupService`, `GeneratedSetupCode`, `app/account/` |
| [installation-page](agents/docs/features/installation-page.md) | root, Urls, ffmpeg from the web | `Maintenance/InstallationSettings` |
| [update-migration](agents/docs/features/update-migration.md) | version check and update page after an upgrade | `MigrationCoordinator`, `app/migration/` |
| [backups](agents/docs/features/backups.md) | backup zip, download links | `BackupSources`, `Core/Backup/` |
| [settings-new-key](agents/docs/features/settings-new-key.md) | adding a setting (options, schema, level, right) | `SfeOptions`, `EditableSettings`, `settings-schema.ts` |
| [settings-levels](agents/docs/features/settings-levels.md) | environment/server/library/user layers, locks, saving | `Core/Settings/`, `app/settings/` |
| [admin-page](agents/docs/features/admin-page.md) | administration sections, sub-pages, phones | `app/admin/` |
| [listing-page](agents/docs/features/listing-page.md) | listing state, navigation, dialogs, infinite scroll | `app/library/`, `LibraryBrowser` |
| [position-restore](agents/docs/features/position-restore.md) | scroll restore, same-URL reloads | `items/listing-position.ts` |
| [folder-navigation](agents/docs/features/folder-navigation.md) | up / previous / next folder | `LibraryBrowser.Siblings` |
| [folder-covers](agents/docs/features/folder-covers.md) | folder cards with pictures | `LibraryBrowser.Covers`, `CoversService` |
| [location-bar](agents/docs/features/location-bar.md) | compact sticky location bar | `app/library/location/` |
| [scroll-progress](agents/docs/features/scroll-progress.md) | position counter in the listing | `ScrollProgressComponent` |
| [scrollbar-gutter](agents/docs/features/scrollbar-gutter.md) | stable scrollbar gutter | `styles/base.css` |
| [quick-links](agents/docs/features/quick-links.md) | each user's quick links | `QuickLinkService`, `app/library/quick-links/` |
| [tags](agents/docs/features/tags.md) | tag model, tag dialogs, picker | `Core/Tagging/`, `app/tags/` |
| [scope-tagging](agents/docs/features/scope-tagging.md) | tagging a whole folder or search | `ScopeTagger`, `ScopeTagsDialog` |
| [search-terms](agents/docs/features/search-terms.md) | query language, negation rule, quirks | `Core/Search/` |
| [search-sort](agents/docs/features/search-sort.md) | sorts, keyset cursors, match count | `FileSearchService` |
| [search-explain](agents/docs/features/search-explain.md) | live wording of the query | `library/search-explain/` |
| [search-history](agents/docs/features/search-history.md) | last searches per browser | `search-history.service.ts` |
| [indexing](agents/docs/features/indexing.md) | scans, removing folders from the index | `Core/Indexing/` |
| [resolution](agents/docs/features/resolution.md) | width/height, HR/LR marks, `w:`/`h:`/`r:` | `MediaDimensions`, `Core/Search/` |
| [duration](agents/docs/features/duration.md) | durations, badges, `d:` | `MediaDimensions`, `DirectoryIndexer` |
| [not-indexed-marks](agents/docs/features/not-indexed-marks.md) | fading files not in the index | `AttachIndexDataAsync`, `styles/items.css` |
| [thumbnails](agents/docs/features/thumbnails.md) | thumbnail cache, previews, caching headers | `Core/Thumbnails/`, `PreviewsService` |
| [trickplay](agents/docs/features/trickplay.md) | timeline sheets, passes | `Core/Trickplay/`, `TrickplayWorker` |
| [hover-preview](agents/docs/features/hover-preview.md) | trickplay on hover / hold | `TrickplayHoverService` |
| [video-player](agents/docs/features/video-player.md) | shared player, size, cinema mode | `app/player/` |
| [video-streams](agents/docs/features/video-streams.md) | ffmpeg conversion while playing | `Core/Streaming/` |
| [external-player](agents/docs/features/external-player.md) | external player links, file tokens | `FileLinks`, `PlayerOptions` |
| [data-server](agents/docs/features/data-server.md) | serving files from another server | `DataServerUrl` |
| [copy-paths](agents/docs/features/copy-paths.md) | copying full server paths | `CopyPathService` |
| [theme](agents/docs/features/theme.md) | light/dark before first paint | `theme-boot.ts`, `ThemeService` |
| [picture-editor](agents/docs/features/picture-editor.md) | browser edits, server saves | `app/editor/`, `Core/Editing/` |
| [background-tasks](agents/docs/features/background-tasks.md) | jobs, run history, run logs | `Core/Tasks/`, `app/tasks/` |
| [schedules](agents/docs/features/schedules.md) | task schedules | `TaskScheduleService`, `ScheduleCalculator` |
| [missing-files](agents/docs/features/missing-files.md) | missing tagged files, relink | `Core/FileTracking/` |
| [duplicates](agents/docs/features/duplicates.md) | duplicate groups and removal | `DuplicateFinder`, `DuplicateRemover` |
| [similar-videos](agents/docs/features/similar-videos.md) | video fingerprints and matching | `VideoFingerprint`, `VideoMatcher` |
| [system-info](agents/docs/features/system-info.md) | System › About, Video streams | `api/system/info` |
