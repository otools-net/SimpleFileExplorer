# Plan: bringing the code to the readability rules

Files: the whole of `src/` and `tests/`; tools `agents/tools/readability/measure.mjs`, `.editorconfig`. The rules are
[readability](readability.md) and [code-style](code-style.md); this plan says **in what order** existing code is
brought to them, what each step must check, and the traps met on the way. Delete it once phase 8 is done (see
[AGENTS.md](../../../AGENTS.md), "Keeping these notes current").

## Starting point (measured 2026-11-12 on `edge` at `c815d64`)

| Measure | Server | Server tests | Client | Client tests | e2e | Styles |
|---|---|---|---|---|---|---|
| lines > 120 characters | 2757 | 1727 | 2314 | 789 | 141 | 145 |
| files over the limit (400; tests 800) | 21 | 4 | 17 | 2 | 0 | 1 |
| functions > 60 lines | 48 | | 7 | | | |
| functions nested > 3 | 15 | | 7 | | | |
| functions > 5 parameters | 119 | | 2 | | | |
| classes > 8 dependencies | 6 | | 6 | | | |
| inline templates > 40 lines | | | 56 | | | |
| C# files with several types | 106 | | | | | |

Not measured by the tool, counted with `grep` (outside `node_modules` and migrations): 162 `catch (Exception`,
10 optional services in constructors (`Type? name = null`), 105 `)!` assertions in client code, 3 `any`,
79 section banners, 0 `TODO`. `dotnet format Sfe.slnx --verify-no-changes --severity warn`: about 285 findings
(118 IDE0011 braces, 92 whitespace, 28 IDE1006 names, 17 IDE0005 unused usings, 8 import order, 13 MSTEST0037 and
6 CA1416 in tests, 2 charset, 1 IDE0052, 1 IDE0044).

**Done when:** `measure.mjs` shows 0 everywhere (or only exceptions written in a comment at the code, as
[readability](readability.md) allows), the formatters' checks pass (see Phase 2), and phase 8's checks run in CI so
the numbers stay at 0.

## Progress

The work is spread over many sessions. **A session takes the first unticked items in order** (within a phase, items
may be taken in any order), reads the phase's section below, and ticks each item **in the commit that does it**,
with what's worth knowing in a few words after it (a trap met, a hash for `.git-blame-ignore-revs`). An item found
too big is split into sub-items here before starting it. A new trap found on the way goes in the phase's section.

### Phase 0: safety net

- [x] `.git-blame-ignore-revs` at the root, `git config blame.ignoreRevsFile` in
  [commands](../environment/commands.md)
- [x] `measure.mjs --check` / `--write-baseline`, `agents/tools/readability/baseline.json`, README (baseline written
  2026-11-12: 559 files over a limit)
- [x] Reference outputs: `references.mjs` (OpenAPI through `OpenApiExportTests`, EF pending model changes, client
  build files and sizes); two runs on the same code are identical

### Phase 1: formatting

- [x] `dotnet format` over `src/` (diff reviewed for renames): the IDE1006 renames and 5 braces of `if`/`else`
  chains done by hand; the braces grew the baseline (file lines +151, long functions +2, deep +1), accepted
- [x] `dotnet format` over `tests/`, plus the MSTest asserts, CA1416, IDE0052, IDE0044 by hand (two of the
  fixer's asserts did not compile, see the traps)
- [x] Hashes of both in `.git-blame-ignore-revs`; `dotnet format --verify-no-changes` passes (`40efee5`,
  `1d14721`; after it: server 2749 long lines, 50 long functions, 16 deep, 119 with many parameters)

### Phase 2: long lines (numbers: lines over 120 characters on 2026-11-12)

Done with formatters (decided 2026-11-12: CSharpier and Prettier for the whole repository, checked from then on),
then by hand for what they leave: comments and long strings. One commit per step and area.

- [x] Tools: CSharpier 1.4.0 (`dotnet-tools.json`, `.csharpierrc.yaml`, `.csharpierignore`), Prettier 3.10.1
  (`src/Sfe.Web`, `.prettierrc.json`), `rewrap-comments.mjs`; IDE0055 off; checks become CSharpier +
  `dotnet format style` + `dotnet format analyzers`
- [x] CSharpier over `src/` (with `dotnet format style` adding the braces of wrapped statements): long lines
  2749 -> 845 (621 of them comments), but functions over 60 lines 50 -> 88 and nested ones 14 -> 21 (braces added
  to wrapped `if`s count now); `measure.mjs` taught CSharpier's layout first
- [x] CSharpier over `tests/` (82 files; server tests' long lines 1724 -> 70)
- [x] Comments rewrapped: server and server tests 
  (656 paragraphs; one `<see cref>` longer than a line left)
- [x] Long strings of the server, split (`split-strings.mjs`: 137 splits, one SQL `FormattableString` by hand);
  OpenAPI document identical
- [x] OpenAPI descriptions in raw strings rewrapped (`rewrap-descriptions.mjs`, 43 paragraphs): the document
  differs only in its descriptions' line breaks (identical with whitespace collapsed; Markdown renders the same)
- [ ] Long strings of the server tests (about 360 lines)
- [ ] Prettier over the client (`src/Sfe.Web/src`: TypeScript, templates, CSS); `npm run format` / `format:check`
- [ ] Prettier over the e2e tests
- [ ] Comments of the client and e2e rewrapped
- [ ] What is left over 120 in the client, e2e and styles, by hand (`measure.mjs --area client`)
- [ ] Hashes of the formatter commits in `.git-blame-ignore-revs`

### Phase 3: one type per file, `models.ts` by area

- [ ] `Sfe.Api/Endpoints` (29 files): routes + `<Area>Contracts.cs`, 3–4 commits
- [ ] `Sfe.Core/Files` (16, `FileJobs` records and enums included, the class left for phase 6)
- [ ] `Sfe.Core/Access` (10), `Sfe.Api/Security` (9)
- [ ] `Sfe.Core/Options` (`SfeOptions.cs` by section) and `Settings` (5, `EditableSettings`)
- [ ] `Sfe.Core/Data` (5; `has-pending-model-changes` checked), `FileTracking` (5)
- [ ] Rest of the server (about 25 files in 14 folders)
- [ ] OpenAPI document identical to phase 0's reference
- [ ] Client: `core/models.ts` split by area, re-exported
- [ ] Client: importers moved to the new files, area by area (one item each when started), then re-exports removed

### Phase 4: templates (56 inline templates over 40 lines)

Move to `.html` (one commit per line):

- [ ] `access/` (7), `account/` (4), `admin/` (2)
- [ ] `formats/` (10)
- [ ] `library/` (10)
- [ ] `files/` (6), `tags/` (3), `tasks/` (3), `tracking/` (3)
- [ ] `editor/`, `logs/`, `menus/`, `migration/`, `player/`, `settings/`, `shared/`, `trickplay/` (1 each)

Tidy, one component per commit, worst first (add one line per component when starting):

- [ ] `admin-page` (298 lines)
- [ ] `archive-view` (181)
- [ ] `space-detail`, `setup-page`, `account-editor`, `settings-panel`, `migration-page`, `folder-access`,
  `tasks-section`
- [ ] the rest, from `measure.mjs --area client` (templates with tags over 120 characters)

### Phase 5: long, deep and many-parameter functions

- [ ] Route maps: `MapFileEndpoints`, `MapTagEndpoints`, `MapExploreEndpoints`, `MapLibraryEndpoints`, others over 60
- [ ] `[AsParameters]`: `ExploreEndpoints` (List, Search, GetText, Extract, GetEntry, OpenEntryAsync, Unlock,
  GetEntryAt)
- [ ] `[AsParameters]`: `LibraryEndpoints`, `TextEndpoints`, `EditorEndpoints`, `ConfigEndpoints`
- [ ] `[AsParameters]`: the other endpoints files (one item each when started)
- [ ] EF: `AccessDbContext.OnModelCreating`, `SfeDbContext.ConfigureTagging`
- [ ] `AddSfeCore`, `AddSfeSecurity`
- [ ] `ArchiveExtractor.Extract`
- [ ] `EditableSettings.From`, `ApplyTo`; `SettingsService.Validate` and its 99-line lambda
- [ ] `Uploads.FinishAsync`, `AppendAsync`
- [ ] `ChangeEndpoints.StreamAsync`, `SimilarVideoFinder.RunAsync`, `TagService.AttachIndexDataAsync`
- [ ] `Trash.RestoreAsync`, `PurgeExpiredAsync`, `DeleteCachesLater`
- [ ] `TrickplayService.GenerateAsync`, `ZipDirectory.Read`, `ZipWriter.AddFileAsync` (+ `AddCentral`,
  `WriteLocal` parameters), `FileContent.OverwriteAsync`, `InstallationSettings`
- [ ] The rest of the server list (`measure.mjs --area server`), nested ones included
- [ ] Client: `item-menu.entries`, `library-page.onKeydown`
- [ ] Client: `openEpub`, `openPdf`, `flash-player`, `trickplay-hover.attach`, `search-explain.parseGroup`
- [ ] Client nested: `settings-form.save`, `uploads.add`/`upload`, `listing.store.refresh`, `admin-search`,
  `archive-view.open`, `csv-table.columnsOf`; parameters: `crop.dragRect`, `json-tree.visit`

### Phase 6: classes (one item each; its split written here before starting)

- [ ] `FileJobs` (with `ExtractAsync`, `CompressAsync`)
- [ ] `Trash`
- [ ] `PathMoves`
- [ ] `FileOperations` (with `MoveOnDiskAsync`)
- [ ] `RelinkService`, `Uploads`
- [ ] The 10 optional services in constructors
- [ ] `TrickplayService`, `ArchiveCatalog`, `SettingsService`, `AccessService`, `TagService`, then the other server
  files over 400 lines (one item each when started)
- [ ] Client `LibraryPageComponent`, `ItemMenu`, `ViewerComponent`, `MainMenuComponent`, `FolderMenu`,
  `QuickLinksComponent`
- [ ] Client files over 400 lines: `archive-view`, `video-player`, `admin-page`, `context-menu`, `settings-schema`,
  `json-view`, `listing.store`, `settings-form.service`, `csv-view`, `api.service`, `image-editor`, `subtitles-view`,
  `text-view`, `uploads`, `scope-tags` (one item each when started)

### Phase 7: rules no tool measures

- [ ] `catch (Exception)` reviewed by folder (narrowing = a fix commit with its test)
- [ ] Doc comments on public members and exported functions, by folder
- [ ] Section banners gone with their splits (check what's left)
- [ ] Client: `)!` assertions, `any`, member order, `protected`/`readonly`, return types
- [ ] Async: suffixes and `CancellationToken`
- [ ] Test files over 800 lines split: `TaggingTests`, `ApiTests`, `AccessTests`, `SearchCombinationTests`,
  `items.spec.ts`, `admin-page.spec.ts`, `access.spec.ts`
- [ ] `reader.css` split; repeated CSS values as custom properties

### Phase 8: checks

- [ ] ESLint in `src/Sfe.Web` (warnings), `npm run lint`
- [ ] ESLint rules made errors
- [ ] `EnforceCodeStyleInBuild`; CA1502/CA1505/CA1506 as warnings
- [ ] CI workflow on `edge` and pull requests
- [ ] Notes moved into [readability](readability.md), plan deleted, index line and links removed

## Rules for every step

These repeat nothing of [readability › Refactoring existing code](readability.md#refactoring-existing-code); they add
what applies to this plan.

- **One commit per kind of change and per area**, message `claude: refactoring: <what>` (e.g.
  `claude: refactoring: long lines of Sfe.Core/Files`). No `CHANGELOG.md` line, no version bump: nothing a user sees
  changes ([versioning-and-changelog](versioning-and-changelog.md)).
- **Before and after each commit:** `dotnet test` and `npm test` pass (client-only commits: `npm test` only);
  `dotnet csharpier check .`, `dotnet format style` and `dotnet format analyzers` with `--verify-no-changes
  --severity warn` pass (from phase 2 on; before it, `dotnet format --verify-no-changes`);
  `measure.mjs --check` passes, then `--write-baseline` locks in what improved (`baseline.json` in the commit);
  `references.mjs` before and after compares equal for the phases that name it.
- **Tests do not change** except: a spec that reached into a moved private member, phase 1's analyzer fixes in tests,
  and the long lines and file splits of test files themselves (phases 2 and 7), in commits that touch only tests.
- **One file or one class at a time** for phases 5 and 6, merged quickly: big files are also the ones features
  change, and a long-lived refactoring branch will conflict.
- **Feature work meanwhile:** a file touched for a feature is brought to the rules first, in its own commit
  ([readability](readability.md#refactoring-existing-code)); a file being refactored is not changed for a feature in
  the same commit.
- Each finished phase updates the "Now over" column of [readability › Sizes](readability.md#sizes) and the table
  above.

## Phase 0: safety net (1–2 commits, before any code moves)

1. **`.git-blame-ignore-revs`** at the root (empty, with a comment line), and in
   [commands](../environment/commands.md): `git config blame.ignoreRevsFile .git-blame-ignore-revs`. Mechanical
   commits (phases 1–4) add their hash there in a following commit.
2. **A ratchet in `measure.mjs`:** `--check` exits 1 when a file is worse than in the committed baseline
   (`agents/tools/readability/baseline.json`), `--write-baseline` rewrites it. This makes "new code follows the
   rules now" checkable during the months the debt remains; each commit that pays debt off lowers the baseline.
   How to use both: the tool's [README](../../tools/readability/README.md).
3. **Reference outputs to compare against:** `references.mjs <temp folder>` writes them, `git diff --no-index`
   compares two runs:
   - the OpenAPI document, through `OpenApiExportTests` (it writes the document when `SFE_OPENAPI_OUT` names a
     file; the running app would need a setup and a session): phases 3 and 5, moved records and `[AsParameters]`,
     must leave it identical;
   - the EF model: `dotnet ef migrations has-pending-model-changes` for `SfeDbContext` and `AccessDbContext` must
     say "No changes" after every commit that touches `Data/` (phase 5 splits `OnModelCreating`);
   - the client build's files and sizes, as a check that template moves add nothing (phase 4).

## Phase 1: formatting (3 commits)

1. `dotnet format Sfe.slnx --severity warn` over `src/`: braces, whitespace, unused usings, import order, charset,
   private field names (`_` prefix: `ArchiveCatalog`, `ArchiveReader`, `BoundedStreams`) and PascalCase local
   constants (`Trash.batchSize`, `Ffmpeg.prefix`). Review the diff for renames: none may touch a public member, a
   JSON name or a log template. Add the hash to `.git-blame-ignore-revs`.
2. The same over `tests/`, plus by hand: the 13 MSTEST0037 asserts (`Assert.HasCount`, `Assert.DoesNotContain`…), the
   6 CA1416 in `SecretFilesTests` (`[SupportedOSPlatform("linux")]` / `OperatingSystem.IsWindows()` guard as the test
   already behaves), `TextRouteTests._tenant` (IDE0052), `TrashTests` readonly field.
3. From then on `dotnet format --verify-no-changes --severity warn` passes; it joins the checks of every later commit.

Traps met:

- `dotnet format` cannot fix IDE1006 ("NamingStyleCodeFixProvider doesn't support Fix All in Solution"), nor the
  braces of an `if`/`else` chain where one branch already has them: both by hand. Renaming a field to `_name` by
  text: keep `this.name = name` constructor parameters, and string literals equal to the name (`"folders"` in
  `ArchiveCatalog`).
- `dotnet format` applies the MSTEST0037 fixes itself, two of them not compiling: `Assert.EndsWith('\n', …)` (takes
  a string) and `Assert.HasCount(long, …)` (takes an int; `AreEqual(size, array.LongLength)` instead), and
  `Assert.AreEqual(0, enumValue)` (needs `(FileAttributes)0`); `dotnet format` itself still reports nothing then: build the tests. The
  CA1416 guard `if (OperatingSystem.IsWindows()) Assert.Inconclusive(…)` needs a `return;` after it for the analyzer.
- A local constant renamed by text: leave the content of raw string literals (`"""`) alone (test data such as
  `Video: h264 (High)` that a `Replace` looks for), and rename inside interpolations (`$"at {depth}"`).
- Added braces lengthen files and functions: `measure.mjs --check` fails for them; `--write-baseline` accepts it in
  this phase only.
- `references.mjs` fails when another `dotnet` build runs at the same time: run it alone.
- `dotnet test` takes about 9 minutes (1550 tests): see [long-runs](../environment/long-runs.md).

## Phase 2: long lines (formatters, then by hand)

The formatters break code at 120 columns by its structure; their layout is the rule from then on
([readability › Lines](readability.md#lines)). CSharpier proves each file's syntax tree unchanged; Prettier keeps
string contents. What they cannot break is done by hand, by area, worst first (`measure.mjs --area <area> --top 200`).

- **C#:** `dotnet csharpier format <folder>`, then `dotnet format style Sfe.slnx --severity warn --include <folder>`
  (statements CSharpier wrapped need braces: `when_multiline`), then CSharpier again, until both are quiet (two
  rounds on `src/`).
- **Comments:** `node agents/tools/readability/rewrap-comments.mjs <folder>`; a diff read through, a second run
  changes nothing.
- **Strings keep their exact text.** A raw string literal (`"""`) or a template literal spread over lines adds the
  line breaks and keeps the indentation rules of the language: for user texts, log templates, OpenAPI descriptions
  (`WithDescription`, `WithSummary`) and SQL compared in tests, prefer concatenation (`"…" +` on each line) or a
  named constant. A test asserting the text, or the OpenAPI diff, catches a slip.
- Data tables (`settings-schema.ts`, the icon sprite's paths): one entry per block; an SVG path too long for one line
  becomes a named constant, its content unchanged.
- Inline templates over 40 lines: Prettier formats them in place; phase 4 still moves them to `.html`.

Traps met:

- `dotnet format` (plain, or `whitespace`) and CSharpier disagree on continuation indents (raw strings, wrapped
  arguments): IDE0055 is `none` in `.editorconfig`, and the checks are `style` + `analyzers` + `csharpier check`.
- CSharpier re-indents the content of raw string literals with their closing `"""`: the value is unchanged (the
  OpenAPI document compared identical).
- CSharpier over `src/`: 187 files, +15389 / -4422 lines (about 2700 short lines re-laid out too); 224 code lines and
  621 comment lines still over 120 after it.
- `measure.mjs` read C# headers up to the line at the brace's indent: CSharpier's `)` on its own line hid every
  method with parameters one per line (functions with > 5 parameters fell from 121 to 3). Fixed before the
  baseline; compare `--json` counts before and after a layout change.
- awk's `length` counts bytes, so lines with `…` or `→` look long to it: measure with Node (`measure.mjs`).

## Phase 3: one type per file (server) and `models.ts` by area (client)

**Server, about 30 commits, one per folder.** Move each top-level type into `<Type>.cs` in the same folder and the
**same namespace** (entity and options namespaces are named by EF snapshots and configuration binding). Allowed groups,
under 400 lines each: `<Area>Contracts.cs` for the requests and answers of one endpoints file (next to it:
`FileEndpoints.cs` keeps the routes, `FileContracts.cs` its 12 records), `<Area>Models.cs` for small data types of one
contract. Worklist from `measure.mjs` ("files with several types", 106), with the known large ones:

| File | Types | Becomes |
|---|---|---|
| `Options/SfeOptions.cs` (805 lines) | 32 | one file per options section (`PlayerOptions.cs`, …); `SfeOptions.cs` keeps the root |
| `Settings/EditableSettings.cs` | 24 | the class alone; its records in `EditableSettingsModels.cs` (split if > 400) |
| `Tagging/TagModels.cs` | 21 | stays a models file if under 400 lines, else by sub-area (tags, groups, scope) |
| `Tasks/TaskModels.cs`, `Access/AccessModels.cs`, `Library/LibraryModels.cs` | 14, 10, 9 | same rule |
| `Endpoints/*Endpoints.cs` (File 13, Explore 10, Session 10, Config 9, Text 8…) | | routes + `<Area>Contracts.cs` |
| `Files/FileJobs.cs` | 10 | enums and records into their files now; the class itself waits for phase 6 |

Check: the OpenAPI document is identical (schema names come from type names, not files).

**Client, 1 + about 15 commits.** `core/models.ts` (1465 lines, 175 exports, imported by 152 files):

1. Move each group of types next to the feature that owns it (`files/file.models.ts`, `tags/tag.models.ts`,
   `access/access.models.ts`…; a type used by several areas stays in `core/`), each file under 400 lines;
   `core/models.ts` becomes `export * from` lines only. One commit.
2. Then, area by area, importers take the type from its new file; when none is left, the re-exports go.

## Phase 4: templates (about 60 commits, small)

1. **Move** each of the 56 inline templates over 40 lines into `name.component.html` (`templateUrl`), one commit per
   folder. Traps: escapes of the TypeScript literal (`` \` ``, `\\`, `\$`) must be unescaped in the `.html`; the
   unit-test builder (`@angular/build:unit-test`) resolves `templateUrl` itself, nothing to add in specs. The DOM is
   unchanged: the e2e quick set and `npm test` pass without change.
2. **Tidy** the moved templates, one component per commit, worst first (`admin-page` 298 lines, `archive-view`,
   `space-detail`, `setup-page`, `account-editor`…): long tags one attribute per line in the documented order;
   expressions with string building or long `?:` become `computed()` (named as the value); handlers become one call.
   A `computed()` replacing an expression gets a spec when the expression had none.

## Phase 5: long, deep and many-parameter functions (one function per commit)

Each: read its tests first; if the function's paths are not covered, add the tests that pin them **in a commit
before** the refactoring. Then extract named steps, guard clauses, parameter objects.

- **Route maps** (`MapFileEndpoints` 144 lines, `MapTagEndpoints`, `MapExploreEndpoints`, `MapLibraryEndpoints`):
  one `Map<SubGroup>` per sub-group, descriptions as constants by their handler ([api-routes](api-routes.md)).
  OpenAPI diff identical.
- **Handlers with more than 5 parameters** (119, mostly `Endpoints/`: `ExploreEndpoints.List` and `.Search` take 16):
  an `[AsParameters]` record per handler group. Check binding: property names keep the query names (binding is
  case-insensitive, but the OpenAPI document shows the declared name), optional values stay nullable with the same
  defaults, services stay `[FromServices]`. The route tests cover the wire; the OpenAPI diff covers the document.
- **EF configuration** (`AccessDbContext.OnModelCreating` 173 lines, `SfeDbContext.ConfigureTagging` 98):
  `IEntityTypeConfiguration<T>` classes or one `Configure<Entity>` method each; `has-pending-model-changes` must
  say no changes for both contexts, and no migration is added.
- **Core functions**, worst first: `ArchiveExtractor.Extract` (143), `FileJobs.ExtractAsync` (138) and
  `CompressAsync` (101) (with phase 6's split of `FileJobs` if it comes first), `EditableSettings.From` (125) and
  `ApplyTo` (90), `SettingsService.Validate` (109), `Uploads.FinishAsync` (105, nested 5),
  `ChangeEndpoints.StreamAsync`, `SimilarVideoFinder.RunAsync`, `TagService.AttachIndexDataAsync`,
  `Trash.RestoreAsync`, then the rest of the list.
- **DI registrations** (`AddSfeCore` 89, `AddSfeSecurity` 82): one `Add<Area>` per area, registration order kept (a
  later registration of the same service wins).
- **Client** (7 + 7): `item-menu.entries` (118) into one builder per group of entries, `openEpub`, `openPdf`,
  `trickplay-hover.attach`, `search-explain.parseGroup`, `library-page.onKeydown` (delegating to named methods),
  `flash-player`; nested: `settings-form.save`, `uploads.add`/`upload`, `listing.store.refresh`. Pure pieces become
  exported functions with their own spec.

## Phase 6: classes too large or with too many dependencies (one class per change)

The only phase that changes the design. Each class gets, in its commit message, the list of what moves where, and
the new classes are registered where the old one was (per library container or root:
[tenants](../architecture/tenants.md)).

| Class | Now | Split into (to be confirmed when reading it) |
|---|---|---|
| `FileJobs` | 1111 lines, 17 deps | job list and state, move, copy, extract, compress (five classes, as the rules say) |
| `Trash` | 712 lines, 15 deps | moving to the trash, restoring, purging, the trash listing |
| `PathMoves` | 12 deps | one mover per kind of stored path (index, settings, schedules, quick links, caches) |
| `FileOperations` | 11 deps | create/rename, move on disk, deletes |
| `RelinkService`, `Uploads` | 9 deps each | by step (`Uploads`: sessions, chunks, finishing) |
| `TrickplayService` (939), `ArchiveCatalog` (817), `SettingsService` (732), `AccessService` (663), `TagService` (631), `DirectoryIndexer`, `ArchiveReader`, … | over 400 lines | by responsibility, after phase 5 has named their steps |
| client `LibraryPageComponent` (13 injects), `ItemMenu`, `ViewerComponent` (12), `MainMenuComponent`, `FolderMenu`, `QuickLinksComponent` | > 8 deps | logic into services and plain functions, the component showing and forwarding |
| client `archive-view` (928), `video-player` (863), `viewer`, `admin-page`, `context-menu`, `json-view`, `csv-view`, … | over 400 lines | same |

- The 10 optional services in constructors go here: the class needs the service, or the part using it moves to
  another class ([readability › C#](readability.md#c)); tests build objects through their helpers.
- Public behaviour, routes and DI lifetimes stay; tests that construct the class directly may change only in how they
  build it (helpers in `tests/Sfe.Tests/Infrastructure/`).

## Phase 7: the rules no tool measures (by area, alongside phases 5–6)

- **`catch (Exception)`** (162): narrow to the types handled, or keep at a job's edge, logged, in a `Try…` method.
  Each change that alters which errors are caught is a **behaviour change**: it goes in a fix commit with a test, not
  in a refactoring.
- **Doc comments** on every public type and member and exported function; first sentence plain.
- **Section banners** (79): each section is a candidate file; remove the banner when its file is split.
- **Client:** `)!` assertions (105) narrowed once, the 3 `any` to `unknown`, member order, `protected`/`readonly`,
  return types of exported functions.
- **Async:** `Async` suffix, `CancellationToken cancellationToken` last and passed on, or a comment saying why not.
- **Tests:** files over 800 lines split by what they test (`TaggingTests`, `ApiTests`, `AccessTests`,
  `SearchCombinationTests`, `items.spec.ts`, `admin-page.spec.ts`); `access.spec.ts` by component.
- **Styles:** `reader.css` (869) split by part of the reader; repeated values into custom properties.

## Phase 8: the checks run by themselves

1. **Client lint:** `angular-eslint` + `typescript-eslint` in `src/Sfe.Web` (`npm run lint`), rules
   `max-len` 120, `max-lines` 400 (specs 800), `max-lines-per-function` 60, `max-depth` 3, `max-params` 5,
   `complexity` 25, `@typescript-eslint/no-explicit-any`, `@typescript-eslint/no-non-null-assertion` (warn),
   `@angular-eslint/template/*` (no complex expressions, one statement per handler). Started as warnings, made
   errors once clean.
2. **Server build:** `EnforceCodeStyleInBuild` in `Directory.Build.props`, so the `.editorconfig` warnings show in
   `dotnet build`; CA1502/CA1505/CA1506 raised from suggestion to warning once phase 6 is done.
3. **CI** (`.github/workflows/`, a workflow on pushes to `edge` and pull requests): `dotnet csharpier check .`,
   `dotnet format style` and `dotnet format analyzers` with `--verify-no-changes --severity warn`,
   `npm run format:check`, `npm run lint`, `node agents/tools/readability/measure.mjs --check`.
4. Move what stays true into [readability](readability.md) (the checks, the traps of phases 2–5), set its "Now
   over" column to 0 or remove it, delete this plan, its line in `AGENTS.md` and the links to it
   (`node agents/tools/docs-screenshots/check-links.mjs`).

## Order and size

Phases 0–1 are a day and unblock everything. Phases 2–4 are mechanical and can be done in any order area by area;
phase 3 before 6 on the server (a class is easier to split once its records have left the file). Phases 5 and 6
interleave per class: name the steps (5), then split by what the steps share (6). Phase 7 goes with whichever phase
touches the file. Phase 8.1 can start as soon as phase 4 is done for the client, with the rules as warnings.
