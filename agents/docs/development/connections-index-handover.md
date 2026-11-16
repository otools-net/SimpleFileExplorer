# Handover: the external-access epic, paused before plan 6's phase 6

Files: [external-access-epic-plan](external-access-epic-plan.md) (status board),
[connections-index-plan](connections-index-plan.md) (plan 6, its "As built" notes per phase). Paused on 2026-11-16
at the owner's request, after phase 5 of plan 6 was committed (`e10acbf`). Delete this file when the epic is
finalized (with the epic plan's other leftovers).

## Where it stands

- Plans 1 (api-v1), 2 (api-tokens), 3 (namespace-rename), 4 (client-library) and 5 (connections): **done**, plans
  deleted, what stays true in their notes (`api-versioning`, `access-tokens`, `namespaces`, `client-library`,
  `connections` and its siblings).
- Plan 6 (connections-index): phases 0 to 5 **done** and committed; phase 6 (close) not started. The working tree
  was clean at the pause.
- Commits of this run on `edge`: `git log --oneline 1ef49d3^..HEAD`. Nothing is merged into `main` and no version
  was bumped: the `## Unreleased` lines of `CHANGELOG.md` carry it all.

## Left to do in plan 6 (phase 6)

1. **C# library, search and tags on a connection**: `sfe.Connections[id].Search` (like `SfeSearch`, the route
   `GET api/v1/connections/{id}/search`, its `coverage` answer) and tags (`GET`/`POST connections/{id}/file-tags`,
   `…/file-tags/scope`, the 409 `connection-not-indexed`). Then remove them from the "left to `Raw`" list of
   `FacadeCoverageTests` (also listed there: the index routes, `files/text`, `streaming/probe` and `play`; decide
   for each, the plan only asks search and tags) and rewrite the public API snapshot
   (`Compatibility/PublicApiTests`, command in its failure message).
2. **Feature note `connection-index.md`** (`connection-previews.md` exists): the tables, `ConnectionIndexer`,
   listings from the index (when "complete"), `NameKey`, search over remote rows, tags, with what stays true from
   the plan's phases 0 to 4 "As built" notes and its Traps; one line in the `AGENTS.md` Features table.
3. **Security**: check that the plan's "Attack vectors to test" are all in [security](security.md) with their tests
   (phases 3 to 5 added theirs; phases 1 and 2 may not have).
4. **User docs and screenshots** (`docs/administration/connections.md`, `docs/user-guide/search.md`,
   `docs/user-guide/tags.md`): none of plan 6's pages has screenshots. The screenshot tool
   (`agents/tools/docs-screenshots/`) has no connection to show: it needs a scene registering the test
   `FakeProvider` or a recorded one; otherwise leave the pages without images and say so.
5. **Delete the plan**: move what stays true (decisions, phase 0 measurements, Dropbox facts, traps, "Left for
   later") into the notes, delete `connections-index-plan.md` and its index line, fix the links (`check-links.mjs`),
   mark row 6 of the epic board done.
6. **Finalize the epic**: per `AGENTS.md`, move what stays true from `external-access-epic-plan.md` (session log
   aside) into notes, delete it, its index line and this handover; then remove the status-report cron jobs if any
   are set again.

## Waiting on the owner

- **Real-Dropbox check**: the owner runs [dropbox-check](../../tools/dropbox-check/README.md#index-of-a-large-dropbox)
  by hand on a large account before a release with plan 6; the numbers replace the plan's estimates; anything far
  from them is a bug to fix first. Its last steps (search, tags, previews) are now possible.
- Nothing else is open: the owner's answers are recorded (connections' defaults in
  [connections](../features/connections.md#open-decisions-defaults-taken), tokens surviving a password change,
  the server-wide cache).

## Decisions taken while building (each can change; worth a look)

- Tag counts in the tag list and the sidebar stay **library files only** (a count over connections would need each
  person's access and could tell about an account's own connection); connection files are counted per connection.
- The connections' cache keys an entry by provider id and **content hash** (else size and date), not Dropbox's
  `rev` as the plan said: the index stores the hash, and it changes exactly when the bytes do.
- Indexed listings and searches sort names by a stored ICU sort key (`NameKey`); where ICU is off (AppImage on
  systems without it) the key is the upper-cased name, not natural order. Ties of a descending size/type/date sort
  go by name descending (the library: ascending).
- The sync lists **folder by folder** in pages of 500 (no recursive listing): more calls than phase 0's estimate.
- A full index listing goes on from where it stopped after a restart; changes made through SFE during a pass don't
  make it skip a folder (fixed in phase 2).

## Known gaps (not bugs to fix now unless wanted)

- Files moved or copied into a connection this library never indexed arrive **without their tags** (a log
  warning; a moved library file keeps them in SFE's trash). Folder and search tagging in a connection skip files
  not indexed yet.
- Two libraries indexing the same server connection each list it on their own (the plan's trap: share one pass).
- An index listing that stops being complete mid-scroll switches to the provider's order for later pages.
- The first conversion of a large connection video waits for the whole download (up to `maxFileMegabytes`).
- EPUB, archives and Flash don't open on connections; the editors don't save to one.
- "Everywhere" rows have no thumbnails; on phones the scope is under the search's ⋯ › Search in.
- External player and Flash links made with an access token carry the account, not the token
  ([access-tokens](../features/access-tokens.md)).
- The plan's "Left for later" list (trickplay, duplicates, covers, more providers…) stays for later plans.

## How the work was run (to resume the same way)

- One fresh agent per phase with a short self-contained brief: the plan file, the notes to read, the scope, "do
  not commit", the checks. The coordinator reviews the report, checks the diff (files in scope, no external URL,
  no line-ending-only changes, security-sensitive code read by hand), updates the epic board, and commits.
- Checks per commit: `dotnet build Sfe.slnx`; `dotnet csharpier format src tests` (never `src/Sfe.Client/
  Generated/`); `measure.mjs --check` after `git add -N` of new files (it reads tracked files only), then
  `--write-baseline` when files got better; `check-links.mjs`; the full `dotnet test tests/Sfe.Tests` (~16 min,
  in the background); `dotnet test tests/Sfe.Client.Tests`; `npm test` in `src/Sfe.Web` **through PowerShell** and
  not while the backend suite runs; for client changes the e2e quick set with
  `npx playwright test --grep "@smoke|@reported"` in `tests/e2e` (`--grep` doesn't pass through `npm run`).
- A change of the v1 document: rewrite the OpenAPI baseline (its test prints the command) **and** regenerate the
  C# client (`node agents/tools/client-generation/generate.mjs`) in the same commit, or `GenerationTests` fails.
- Traps met: the first build often fails with a StaticWebAssets "No file exists" or a locked file (rerun); e2e
  timing budgets are a little noisy (rerun the spec alone before blaming a change); the usage limit and API
  timeouts stop agents (resume the same agent, its work is kept); keep the pre-existing stash "WIP: External
  access epic" (`git stash list`).
