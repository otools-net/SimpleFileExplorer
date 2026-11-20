# Readability refactoring: handover

Files: [readability-refactoring-plan](readability-refactoring-plan.md) (the checklist and each phase's traps),
[readability](readability.md) (the rules), `agents/tools/readability/` (the tools, their
[README](../../tools/readability/README.md)). For the next session that continues the plan; delete it with the plan
(phase 8).

## Where it stands (2026-11-13, `edge`)

| Phase | State |
|---|---|
| 0 Safety net | done |
| 1 Formatting | done (`dotnet format`) |
| 2 Long lines | done: CSharpier (C#) and Prettier (client) at 120 columns, comments and strings by scripts |
| 3 One type per file, `models.ts` by area | done |
| 4 Templates | moves and line breaks done; the **tidy by hand** is open (see below) |
| 5 Functions | **in progress**: route maps done, `[AsParameters]` done for every endpoints file but three handlers (below) |
| 6–8 | not started |

What is still over the limits (`node agents/tools/readability/measure.mjs`):

| | server | client | templates |
|---|---|---|---|
| lines over 120 | 1 (an unsplittable `<see cref>`) | 11 | 120 |
| functions over 60 lines | 75 | 12 | |
| nested deeper than 3 | 21 | 7 | |
| more than 5 parameters | 38 | 2 | |
| classes over 8 dependencies | 6 | 6 | |
| files over 400 lines | 31 | 20 | 2 (`admin-page`, `archive-view`) |

## How to resume

1. Read the plan's Progress list: take the **first unticked item of phase 5**, then the core functions in the order
   listed. Tick each item in the commit that does it. Paused 2026-11-20 after the EF configuration (tree clean);
   next: `AddSfeCore`, `AddSfeSecurity` (one `Add<Area>` per area, registration order kept).
2. One function (or one endpoints file for `[AsParameters]`) per commit, message `claude: refactoring: …`; no
   `CHANGELOG.md` line, no version bump.
3. Before a function whose paths no test pins (most core functions of phase 5: `ArchiveExtractor.Extract`,
   `FileJobs.ExtractAsync`, `SettingsService.Validate`…), add the tests that pin them **in a commit before** the
   refactoring.

## Checks for every commit

```sh
dotnet csharpier check .
dotnet format style Sfe.slnx --verify-no-changes --severity warn
dotnet format analyzers Sfe.slnx --verify-no-changes --severity warn
dotnet build Sfe.slnx -p:SfeSkipWebClient=true
node agents/tools/readability/references.mjs <temp>/after --only openapi,routes   # server routes or handlers
node agents/tools/readability/references.mjs <temp>/after --only ef               # anything under Data/
node agents/tools/readability/references.mjs <temp>/after --only client           # client changes
git diff --no-index <temp>/before <temp>/after                                     # nothing
set -o pipefail; dotnet test Sfe.slnx --no-build | grep -E " error |Failed |Passed!"   # ~8 min: in the background
cd C:/workspace/SimpleFileExplorer/src/Sfe.Web && npm test                        # client: a C:/ path, see below
node agents/tools/readability/measure.mjs --check ; node agents/tools/readability/measure.mjs --write-baseline
```

- Write the "before" references on the commit before the change (`git stash` the change, run, `git stash pop`; an
  untracked file cannot be named in `git stash push`: use `-u` on its folder).
- `routes.txt` of `[AsParameters]` changes differs in its `metadata` lines: compare it without them
  (`grep -v "^  metadata:"`); rights, folder scopes, policies and rate limits must stay identical.
- The client reference hashes each built file's content (chunk names left out): equal sizes are not enough.

## Patterns set so far (reuse them)

- **Route maps:** descriptions in fields by their route (`ExtractDescriptions.cs`), one `Map<Group>` per group or
  subject (`SplitRouteMaps.cs`, or by hand); registration order kept ([api-routes](api-routes.md)).
- **Handler parameters:** one services record per endpoints file bound with `[AsParameters]`
  (`ExploreServices`, `TextServices`, `LibraryRequest`…), its own file; query and header values in a request
  record in `<Area>Contracts.cs` with `[FromQuery(Name = "…")]`, defaults kept (`int Offset = 0`); route values
  stay handler parameters (OpenAPI documents a `long` inside a record as `integer` or `string`). Handlers over the
  limit only; one record may serve a handler that uses part of it (the services are singletons). A private helper
  over the limit takes the handler's record (`DeleteVerdict(FileServices …)`); one shared between endpoints files
  takes a small record of its own (`Refused(verdict, RefusedAttempt, …)`). Bodies start with
  `var (a, _, c) = services;` so they keep their names. A route served without a session (`Shared`) gets a record
  of its own: never make it resolve library services it did not take.
- **Query order:** a record's query values come in the OpenAPI document where the record is; a value that stays a
  parameter (a `long`) goes after it, and the values after that one stay parameters too (`LogFilter`, then
  `before`, `limit`), so the order is unchanged.
- **Tool for these:** `agents/tools/readability/handler_params.py` (rewrites a handler's parameter list and adds
  the deconstruction; a short script per endpoints file uses it).

## Open work outside phase 5 (left on purpose)

- **Phase 4, tidy by hand:** about 49 template lines with a long `{{ }}` and 41 with one long binding (a
  `computed()` each, with its spec), 41 SVG path lines of `icon-sprite` (one constant per path, or a written
  exception), and the two templates over 400 lines (child components).
- **11 client `.ts` lines** over 120, and the client's long functions (phase 5's client items).

## Environment traps that cost time

- Feature work between sessions brings new warnings (2026-11-19: braces, unused usings, names, MSTest asserts,
  EF1002 in the connections epic's code): run the three format checks first when resuming, and fix what they find in
  their own commits (src, then tests) before the next item.
- `dotnet test` took 19 minutes on 2026-11-19 (2056 tests): over 10, so it needs the cron progress report
  ([long-runs](../environment/long-runs.md)).

- `npm test` from Git Bash fails every suite ("Vitest failed to find the runner") after `cd /c/workspace/…`; use
  `cd C:/workspace/…` or PowerShell ([testing-client](testing-client.md)).
- A background `dotnet test | grep` exits 0 when the build fails: `set -o pipefail`, and read the output.
- `dotnet test --no-build` lets you edit sources while it runs, but not build (the tests' DLLs are locked).
- Runs over 10 minutes report every 30 minutes through a cron task ([long-runs](../environment/long-runs.md)).
- Python heredocs in Git Bash mangle `\n` and `\\`: write a script file, or use the editor's edit tool.
- `git checkout -- <folder>` also undoes uncommitted work in it: commit or stash each step first.
- One endpoints file per commit without waiting: while one file's `dotnet test --no-build` runs, apply the next
  file's script (sources only, no build) once `--write-baseline` has run, and `git add` only the tested file's
  paths when committing.
- `grep -r … src` from Git Bash walks `src/Sfe.Web/node_modules` and stalls past 2 minutes: use the editor's search
  (ripgrep) or name the folder (`src/Sfe.Core`).
- After a services record, the build (not `dotnet format`) warns of `using`s left unused (IDE0005) in the endpoints
  file and in the record's file: read the build's warnings, remove them, build again.
- The "after" references of one endpoints file are the "before" of the next: no stash needed when going file by file.
- Tests read handlers by reflection (`AccessTests.EveryRouteTakingAPathSaysHowFolderRulesApply` looks for a path in
  parameters and in the records they bind): an `[AsParameters]` record can change what they see; run the full suite
  after each endpoints file, not only the route tests.
