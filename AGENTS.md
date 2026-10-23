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
- **Version + changelog:** each commit on `edge` adds its lines under `## Unreleased` in `CHANGELOG.md`; the version
  is bumped only when `edge` is merged into `main` (patch for fixes only, minor for a feature); none for
  docs/tests/build only ([versioning-and-changelog](agents/docs/development/versioning-and-changelog.md)).
- **Tests** come with every feature and fix; not done until `dotnet test` and `npm test` pass
  ([testing](agents/docs/development/testing.md)).
- **User docs:** user-visible changes go in `docs/` with screenshots ([user-docs](agents/docs/development/user-docs.md)).
- **Rights:** every route names its right; new functions get one ([rights](agents/docs/features/rights.md)).
- **Scale:** millions of files; nothing proportional to a folder or the index per request
  ([scale](agents/docs/development/scale.md)).
- **Fully local:** no external URL in the client ([fully-local](agents/docs/development/fully-local.md)).
- **Git:** work is committed on the `edge` branch, merged into `main` when ready; messages `claude: <short summary>`;
  commit only when asked ([versioning-and-changelog](agents/docs/development/versioning-and-changelog.md)).
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
| [frontend](agents/docs/development/frontend.md) | Angular conventions, CSS, icons, phone sheets (popups at the bottom on phones) | `src/Sfe.Web/src/`, `styles/phone-sheets.css` |
| [fully-local](agents/docs/development/fully-local.md) | no external resources | `ApiTests` |
| [security](agents/docs/development/security.md) | security rules, audit findings and status, attack vectors to test | `Sfe.Api/Security/`, `LibraryEndpoints` |
| [file-operations](agents/docs/development/file-operations.md) | rules for changing library files (planned), moving folder rules with folders | `FolderRuleService.BeginMoveAsync` |
| [file-management-plan](agents/docs/development/file-management-plan.md) | **file management epic: phases, todo list, open questions** (update as it is built) | `Core/Files/` (planned) |
| [file-viewers-plan](agents/docs/development/file-viewers-plan.md) | **viewing and editing files epic: formats registry, phases, todo lists** (update as it is built) | `app/formats/`, `Core/Files/FileContent` |
| [server-modes-plan](agents/docs/development/server-modes-plan.md) | **server modes epic: Local / External, Docker image; phases, todo list, open questions** (update as it is built) | `ServerModes`, `Dockerfile` (planned) |
| [docker-publishing-plan](agents/docs/development/docker-publishing-plan.md) | **publishing the image on ghcr.io: GitHub Actions workflow, settings, release steps** (update as it is built) | `.github/workflows/` (planned) |
| [keep-alive-plan](agents/docs/development/keep-alive-plan.md) | **keep alive epic: pinging network folders so file servers don't drop idle sessions; design, phases, open questions** (update as it is built) | `Core/KeepAlive/`, `KeepAliveEndpoints` |
| [context-menu-plan](agents/docs/development/context-menu-plan.md) | **context menu epic: one app menu for every right-click, Open in new tab; map of which menu where, phases, todo list** (update as it is built) | `files/item-menu.ts` → `menus/context-menu.ts` (planned) |
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
| [docker](agents/docs/environment/docker.md) | the Docker image: stages, `/data` content root, environment, smoke test |
| [running-the-app](agents/docs/environment/running-the-app.md) | the app on a scratch library |
| [visual-check](agents/docs/environment/visual-check.md) | headless Edge screenshots and clicks |
| [docs-screenshots](agents/docs/environment/docs-screenshots.md) | regenerating `docs/images/` |
| [dev-machine](agents/docs/environment/dev-machine.md) | this machine: node, ffmpeg path, shells |
| [line-endings](agents/docs/environment/line-endings.md) | LF/CRLF, Git Bash pitfalls, git warnings |

### Features — `agents/docs/features/`

| Note | Covers | Code |
|---|---|---|
| [rights](agents/docs/features/rights.md) | adding a right, how rights are evaluated | `Core/Access/RightCatalog`, `RightsEvaluator`, `core/rights.ts` |
| [right-kinds](agents/docs/features/right-kinds.md) | Read/Write rights, `AtFolder`, `kind:write`/`folder:write`, read-only switches | `RightCatalog`, `access-parts.ts` |
| [folder-access](agents/docs/features/folder-access.md) | per-folder permissions: model, policy, rules, enforcement, management card | `FolderPolicy`, `FolderRuleService`, `FolderChecks`, `folder-access.component.ts` |
| [accounts](agents/docs/features/accounts.md) | sign-in directories, space administrators | `Core/Access/`, `app/access/`, `app/account/` |
| [setup](agents/docs/features/setup.md) | setup code (configured / generated), first administrator | `SetupService`, `GeneratedSetupCode`, `app/account/` |
| [local-start](agents/docs/features/local-start.md) | browser at start, `--no-browser`, startup logo and `--no-logo`, `--help`, setup without code locally, fast sign-in | `BrowserLauncher`, `StartupLogo`, `CommandLine`, `LocalVisitor`, `FastSignIn` |
| [installation-page](agents/docs/features/installation-page.md) | root, Urls, ffmpeg from the web | `Maintenance/InstallationSettings` |
| [server-modes](agents/docs/features/server-modes.md) | Local / External mode: `Sfe:Mode`, locks, switch rules; Local limits (this computer, administrators, no visitors), `local-only` page | `ServerModes`, `InstallationSettings`, `RequestGuard`, `SfeSession`, `local-only-page.component.ts` |
| [update-migration](agents/docs/features/update-migration.md) | version check and update page after an upgrade | `MigrationCoordinator`, `app/migration/` |
| [backups](agents/docs/features/backups.md) | backup zip, download links | `BackupSources`, `Core/Backup/` |
| [settings-new-key](agents/docs/features/settings-new-key.md) | adding a setting (options, schema, level, right) | `SfeOptions`, `EditableSettings`, `settings-schema.ts` |
| [settings-levels](agents/docs/features/settings-levels.md) | environment/server/library/user layers, locks, saving | `Core/Settings/`, `app/settings/` |
| [admin-page](agents/docs/features/admin-page.md) | administration sections, overview, sub-pages, moved pages, phones | `app/admin/` |
| [admin-search](agents/docs/features/admin-search.md) | search box of the administration page | `admin-search.ts` |
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
| [default-tags](agents/docs/features/default-tags.md) | default tags of folders: given to files entering the tree (scan, new, upload, copy, move), trash, chips | `FolderDefaultTags`, `DirectoryIndexer.NewEntry`, `ScopeTagsDialog` |
| [search-terms](agents/docs/features/search-terms.md) | query language, negation rule, quirks | `Core/Search/` |
| [search-sort](agents/docs/features/search-sort.md) | sorts, keyset cursors, match count | `FileSearchService` |
| [search-explain](agents/docs/features/search-explain.md) | live wording of the query | `library/search-explain/` |
| [search-history](agents/docs/features/search-history.md) | last searches per browser | `search-history.service.ts` |
| [indexing](agents/docs/features/indexing.md) | scans, removing folders from the index | `Core/Indexing/` |
| [resolution](agents/docs/features/resolution.md) | width/height, HR/LR marks, `w:`/`h:`/`r:` | `MediaDimensions`, `Core/Search/` |
| [duration](agents/docs/features/duration.md) | durations, badges, `d:` | `MediaDimensions`, `DirectoryIndexer` |
| [folder-exclusions](agents/docs/features/folder-exclusions.md) | folders kept out of scans (index frozen), trickplay, thumbnails: lists, right `index.exclude`, chip, dialog | `FolderExclusions`, `ScanEndpoints`, `folder-exclusions.dialog.ts` |
| [not-indexed-marks](agents/docs/features/not-indexed-marks.md) | fading files not in the index | `AttachIndexDataAsync`, `styles/items.css` |
| [thumbnails](agents/docs/features/thumbnails.md) | thumbnail cache, previews, caching headers | `Core/Thumbnails/`, `PreviewsService` |
| [trickplay](agents/docs/features/trickplay.md) | timeline sheets, passes | `Core/Trickplay/`, `TrickplayWorker` |
| [hover-preview](agents/docs/features/hover-preview.md) | trickplay on hover / hold | `TrickplayHoverService` |
| [animated-hover](agents/docs/features/animated-hover.md) | large animated images played on hover | `ImageAnimation`, `AnimatedHoverService` |
| [video-player](agents/docs/features/video-player.md) | shared player, size, cinema mode | `app/player/` |
| [video-streams](agents/docs/features/video-streams.md) | ffmpeg conversion while playing | `Core/Streaming/` |
| [external-player](agents/docs/features/external-player.md) | external player links, file tokens | `FileLinks`, `PlayerOptions` |
| [data-server](agents/docs/features/data-server.md) | serving files from another server | `DataServerUrl` |
| [copy-paths](agents/docs/features/copy-paths.md) | copying full server paths | `CopyPathService` |
| [theme](agents/docs/features/theme.md) | light/dark before first paint | `theme-boot.ts`, `ThemeService` |
| [picture-editor](agents/docs/features/picture-editor.md) | browser edits, server saves (a new file, or over the original through `FileContent`), picture addresses re-fetched after a save | `app/editor/`, `Core/Editing/`, `EditorEndpoints` |
| [text-viewer](agents/docs/features/text-viewer.md) | format registry, text files in the viewer (CodeMirror), Open as text, large files in parts, Follow, encodings, positions, `api/files/text` | `app/formats/`, `TextCodec`, `TextEndpoints` |
| [text-editor](agents/docs/features/text-editor.md) | editing and saving text files: rights (Replace rule), versions (412), previous version in the trash, encodings, unsaved-changes guards | `text-session.ts`, `FileContent.OverwriteAsync`, `TextEndpoints` |
| [reading-preferences](agents/docs/features/reading-preferences.md) | reading settings of text (profiles, "Aa" panel, bundled fonts, reader themes), the `view?path=` page | `app/formats/reading/`, `view-page.component.ts`, `reader.css` |
| [markdown-viewer](agents/docs/features/markdown-viewer.md) | Markdown rendered safely (markdown-it + DOMPurify), links and images rewritten, outline, source / split | `app/formats/markdown/` |
| [json-viewer](agents/docs/features/json-viewer.md) | JSON as a tree (jsonc-parser: order and numbers as written, comments), filter, paths, source / split, Format | `app/formats/json/` |
| [csv-viewer](agents/docs/features/csv-viewer.md) | CSV / TSV as a table (papaparse), sort, filter, header, separator; virtual rows (`VirtualRows`) | `app/formats/csv/`, `formats/virtual-rows.ts` |
| [subtitles-viewer](agents/docs/features/subtitles-viewer.md) | SRT / VTT cues (media-captions), Play in the video next to them, Shift times, `api/files/text/video` | `app/formats/subtitles/`, `Core/Files/Subtitles` |
| [pdf-viewer](agents/docs/features/pdf-viewer.md) | PDF with pdf.js bundled (worker, cmaps, fonts served by the app; CSP: no wasm), find, zoom, outline, dark pages | `app/formats/pdf/`, `angular.json` |
| [epub-reader](agents/docs/features/epub-reader.md) | EPUB with epub.js: page, two pages or scroll; the book's files served by the app (`api/files/epub`), reading preferences on its pages | `app/formats/epub/`, `EpubEntries`, `EpubEndpoints` |
| [context-menu](agents/docs/features/context-menu.md) | the app's one right-click menu: targets, which menu where, browser menu kept (Shift, text), keyboard, Open in new tab | `menus/context-menu.ts`, `ItemMenu`, `FolderMenu` |
| [new-folder-and-rename](agents/docs/features/new-folder-and-rename.md) | new folder, rename, item menu, F2 / Shift+N | `FileOperations`, `FileEndpoints`, `app/files/` |
| [trash](agents/docs/features/trash.md) | delete, trash, restore, purge task, links as links | `Trash`, `FileTree`, `app/files/delete.dialog.ts` |
| [move-and-copy](agents/docs/features/move-and-copy.md) | move/copy jobs, conflicts, clipboard, drops, operations panel | `FileJobs`, `FileJobAccess`, `app/files/file-transfers.ts` |
| [uploads](agents/docs/features/uploads.md) | resumable chunked uploads, folder uploads, desktop drops, admin list | `Uploads`, `UploadEndpoints`, `app/files/uploads.ts` |
| [archives](agents/docs/features/archives.md) | several files or a folder downloaded as one streamed zip, one-time links | `Archives`, `ArchiveEndpoints`, `ArchiveLinks`, `app/files/downloads.ts` |
| [properties](agents/docs/features/properties.md) | properties sheet, folder totals and disk counts, checksums | `Properties`, `PropertyJobs`, `PropertyEndpoints`, `app/files/properties.dialog.ts` |
| [batch-rename](agents/docs/features/batch-rename.md) | several items renamed at once: find/replace, numbered, preview, undo | `app/files/batch-rename.dialog.ts` |
| [change-feed](agents/docs/features/change-feed.md) | folders changed elsewhere told to open listings (SSE), refresh in place | `LibraryBrowser.FolderChanged`, `ChangeEndpoints`, `change-feed.service.ts` |
| [file-names](agents/docs/features/file-names.md) | names refused, keep-both names, portable names | `Core/Files/FileNames` |
| [work-folder](agents/docs/features/work-folder.md) | `.sfe` at the root or the server's data folder; the reserved name | `WorkFolder`, `LibraryPaths.TryResolve` |
| [selection-and-keys](agents/docs/features/selection-and-keys.md) | selecting files and folders, the selection bar, arrow keys | `ListingStore`, `listing-keys.ts` |
| [path-moves](agents/docs/features/path-moves.md) | index rows, settings, schedules, quick links, caches following a rename | `PathMoves` |
| [keep-alive](agents/docs/features/keep-alive.md) | network library folders pinged so file servers keep the session: modes, detection, timeouts, logs, status page, Ping now | `Core/KeepAlive/`, `KeepAliveWorker`, `KeepAliveEndpoints`, `keep-alive-status.component.ts` |
| [background-tasks](agents/docs/features/background-tasks.md) | jobs, run history, run logs | `Core/Tasks/`, `app/tasks/` |
| [schedules](agents/docs/features/schedules.md) | task schedules | `TaskScheduleService`, `ScheduleCalculator` |
| [logs](agents/docs/features/logs.md) | server log: capture, files, search, rights | `Core/Logs/`, `AuditLogWorker`, `app/logs/` |
| [missing-files](agents/docs/features/missing-files.md) | missing tagged files, relink | `Core/FileTracking/` |
| [duplicates](agents/docs/features/duplicates.md) | duplicate groups and removal | `DuplicateFinder`, `DuplicateRemover` |
| [similar-videos](agents/docs/features/similar-videos.md) | video fingerprints and matching | `VideoFingerprint`, `VideoMatcher` |
| [system-info](agents/docs/features/system-info.md) | System › About, Playback › Video streams | `api/system/info` |
