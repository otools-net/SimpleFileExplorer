# Plan: release 2.0 (the ideas scratchpad)

Source: the owner's ideas scratchpad (claude.ai doc "SimpleFileExplorer — Ideas Scratchpad", IDEA-001 to IDEA-027,
2026-11-16). Each idea was checked against `edge` on 2026-11-20 (many were already partly done), the remaining
questions were answered by the owner, and the ideas were cut here into **steps of one session each**, in order.
Delete this plan once 2.0 is released, moving what stays true into the feature notes (see
[AGENTS.md](../../../AGENTS.md), "Keeping these notes current").

**2.0 = every idea below** (owner's choice). It is the first release without the "alpha" label.

## How to work this plan

- **A session takes the first unticked step** whose dependencies are ticked; steps in the same phase may be taken in
  any order unless "after" says otherwise. Tick it **in the commit that does it**, with a few words worth knowing.
- A step found too big is split into sub-steps here before starting. A decision taken on the way goes under
  "Decisions" below, with its date.
- Each step follows the usual rules: tests ([testing](testing.md)), changelog line, user docs with screenshots
  ([user-docs](user-docs.md)), migration path ([production-upgrades](production-upgrades.md)), a right for every new
  route ([rights](../features/rights.md)), no work proportional to a folder per request ([scale](scale.md)), feature
  note in `agents/docs/features/`.
- **Versions:** merges into `main` before the end keep the 1.x rules (minor for features). The only breaking step
  (8.1, hash v2) is done last, so `2.0.0` is the first version carrying it.

## Progress

### Phase 1: bugs and quick fixes

- [x] **1.1 Connection problem answers read as listings** (IDEA-003, S). Done 2026-11-20: the connection's reads
  (`connectionBrowse`, `connectionSearch`, `connectionGet`, `connectionFolders`) go through `Api.getOrRefused` (a 409
  rejects); the listing says "sign in again" with a link by level and rights (`library/connection-problems.ts`), the
  folder picker shows the reason; Everywhere, Properties and folder rules already showed rejections. POSTs keep
  409-as-data (`PlaceFiles.answered`). Before it, a refactoring moved `ApiError` and its helpers to `core/api-error.ts`
  (re-exported) to keep `api.service.ts` within its readability baseline. No screenshot (an error state).
- [x] **1.2 Canvas permission asked before the work** (IDEA-015, S). Done 2026-11-20: `core/canvas-probe.ts`
  (`canvasCheck()`: 32×32 canvas, known colour, `getImageData` read back synchronously in the click — same Firefox
  prompt as `toBlob`, no PNG to decode; ±16 per channel) at the start of `ImageEditorDialog.open` and
  `saveFrame`; refused → error toast, editor not opened, nothing saved. Manual Firefox check in picture-editor.md.
  e2e `picture-editor.spec.ts` (the probe passes in Chromium and WebKit). Refactoring first: `CropDrag` to
  `crop.ts`, frame download to `player/frame-capture.ts`. No screenshot (a toast).
- [x] **1.3 Partial files marked** (IDEA-013 part, S). Done 2026-11-20: transfers into the library, compression and
  extraction write `name.sfe-partial` (library copies write the final name: unchanged). Scans skip that ending
  (`LibraryPaths.IsPartial`, files and folders), so search and duplicates never see them; the client marks them by
  name (`files/partial-files.ts`): faded, "being written" chip, no href, no preview, clicks open nothing, menu cut to
  Delete / Copy full path / Properties. Selection bar not covered (left to 2.9). Note
  [partial-files](../features/partial-files.md), screenshot `partial-file.png` (docs-screenshots scene `partial`).
- [x] **1.4 Copy progress on the item, as in macOS Finder** (owner's request 2026-11-20, M; after 1.3). Done
  2026-11-20: copies write `name.sfe-partial` then rename (keep-both if taken meanwhile; partial over real name: the
  suffix machinery already covers scans and leftovers, and a cut-off copy doesn't look whole); `ItemsBeingWritten`
  (per library, memory, `Interlocked` counts, a 300 ms timer only while writing, 3 s heartbeat) fed through
  `JobRun.Writing` by copies, compressions, extractions, and by `LibraryTransferWrite`; feed event `writing`
  (rights per folder and item); client `WritingProgress`, `<progress>` in the mark, `shownName`, refresh when an item
  comes or ends. Uploads left to 4.3 (they write in the work folder). Refactorings first: `PartialItems`,
  `JobMessages` out of `FileJobs`. Screenshot: scene `partial` copies a folder of 8,000 small files. Was: today only a
  static "being written" mark, and library copies (`FileJobs.CopyFileAsync`) write the final name unmarked. Jobs
  already know their progress (`FileJobView.BytesDone`/`BytesTotal`, `Current`, `JobRun.Partials`) but only the
  operations panel shows it. Add: library copies registered as "in progress" on their destination while they run
  (real name kept, as Finder does — or a partial then rename; pick one and note why); the items being written in a
  folder and their progress told to its open listings through the change feed (throttled to a few updates a second
  per folder, nothing per write; other people's copies too, within their rights); a progress ring or bar on the
  tile / row (percentage in the hint), real name shown without `.sfe-partial`, not openable until done, refreshed in
  place when finished; a folder being copied shows the progress on its card (from the job's byte totals). Reuse for
  uploads in progress (4.3) where it fits. Tests, user docs with screenshot, partial-files.md updated.

### Phase 2: foundations (texts, menus) — before any new screen

- [x] **2.1 Glossary and writing style** (IDEA-011, S). Done 2026-11-20: [ui-texts](ui-texts.md) (spelling,
  punctuation, tone, a table of text kinds, `sfe-help` past ~2 lines, glossary with the words not used). The survey
  found strings already mostly UK, `…` and curly quotes everywhere, errors as "Could not …"; leftovers to fix in
  2.3–2.8: "Colors"/"Color" (theme hint, tag docs), "guest" for the person (→ visitor), "login" (→ sign in),
  "job" in user texts (→ operation / task), terse noun-phrase hints. No code, no changelog line (agents/ only).
- [x] **2.2 Translation-ready strings: infrastructure** (IDEA-010, M). Done 2026-11-21: `@angular/localize`
  (`i18n` / `$localize`, polyfill ~0.5 KB, `sourceLocale` en-GB) + `intl-messageformat` (~10 KB) behind
  `icu()` (`core/i18n/icu.ts`: plural rules of the text's language, numbers of the browser's region); size units
  through `$localize`; `plural()` deprecated (its 42 files go in 2.3–2.8); source file `src/locale/messages.xlf`
  (XLIFF 2.0) by `agents/tools/i18n/extract.mjs` (`--check`); `hard-coded-texts.spec.ts` counts unmarked template
  texts per file against `testing/hard-coded-texts.json` (1,523 in 99 files). Note [translations](translations.md).
  No language setting (none until a second language). Fixed on the way: a quote in `comfort.spec.ts` (1.4) that
  broke the client test build.
- [x] **2.3 Settings schema** (part of 2.3–2.8 below). Done 2026-11-21: a refactoring first split
  `settings-schema.ts` (1,496 lines) into `settings/tabs/` (one file per admin section; data compared identical).
  Every text of the tabs (and connections', tokens') is `$localize`d and rewritten; long ones broken over lines with
  `\` (no `+`: [translations](translations.md)); `help` on `SettingField`, a "?" next to the label, searched by the
  admin search; panel template marked (0 left), lock notes and form errors in code marked (its toasts and confirms
  left to 2.8). Corrected on the way: duplicate removal goes to the trash and needs the right "Delete duplicates"; the
  picture editor can save over. `template-texts.ts` counted plain texts inside `i18n` elements: fixed, with specs.
  `settings.spec.ts` checks the texts' style (no `...`, no "Colors", hints ≤ 200 characters, ending with a stop).
- [ ] **2.4–2.8 Strings extracted and texts rewritten, area by area** (IDEA-010 + IDEA-011, M each). Each string is
  touched once: marked (`i18n`, `$localize`, `icu()` for counts, [translations](translations.md)) **and** rewritten
  to the glossary in the same commit; the area's `plural()` calls go, its counts in `hard-coded-texts.json` drop
  (write it again), `extract.mjs` writes the source file. Settings get an
  optional `help` field in `SettingField` rendered with `shared/help.component.ts` (`sfe-help`, already click/tap
  and keyboard accessible) for long explanations. Areas: 2.3 settings schema (`settings-schema.ts`, ~105 keys);
  2.4 administration and access; 2.5 library, listing and menus; 2.6 files dialogs, uploads, operations;
  2.7 viewers, player, editors, formats; 2.8 toasts (~200 calls) and server texts that reach users (right names
  in `RightCatalog`, problem `detail`; problem `type` words never change — [api-versioning](api-versioning.md)).
  Note: no "its/it's" error was found in UI strings; the real issue is terse noun-phrase hints.
  **2.4 split** (2026-11-21: 722 unmarked template texts in 31 files, ~11k lines of TS; 2.3 alone was ~4k lines):
  - [x] **2.4a access** (`app/access/`: spaces, space page, space editor, accounts, account editor, groups, folder
    access; ~280 texts). Done 2026-11-21: 8 files at 0 unmarked texts (299 gone); `plural()` out of the area
    (template ICU plurals, `icu()` in code); errors keep the reason, the template writes the sentence; editors titled
    "Edit the space “…”"; "Library root" → "Top of the library" (glossary gained it and "guest account"); confirms
    and toasts left for 2.8. Gotchas in [translations](translations.md) (aria-label binding, `sheetFooter`).
  - [ ] **2.4b sign-in and setup** (`app/account/`, `app/migration/`, local-only page; ~190 texts).
  - [ ] **2.4c the rest of administration** (`app/admin/` with `admin-sections.ts` "Colors", `app/connections/`,
    `app/tokens/`, `app/tasks/`, `app/logs/`; ~250 texts).
  Toasts and confirms of these areas stay for 2.8 (as in 2.3).
- [ ] **2.9 Menus grouped, one action registry** (IDEA-025, M). Today `MenuEntry` has no groups, Delete sits in
  the middle, items that don't apply are hidden, and `selection-bar.component.ts` redefines actions. Add `group`
  (dividers) to `MenuEntry`; one action registry used by the item menu, folder menu and selection bar; order
  Open (Open, new tab, containing folder, **Open as… ▸**) → Edit (Edit, Crop) → Clipboard (Cut, Copy, Paste) →
  Organise (Rename, Move to, Copy to, Tags, quick link) → Archive ▸ (Compress, Extract) → Download / Copy path →
  Delete → Properties (Windows Explorer-like). Shortcuts stay shown. Not-applicable items stay **hidden** for type
  and selection; items blocked by rights or mode are **greyed with a reason** (used by 3.3). Update
  context-menu.md, user guide, screenshots, e2e menu specs.

### Phase 3: getting around

- [ ] **3.1 Shallower administration** (IDEA-002, S–M). Tabs, per-page URLs, phone chips and the admin search
  already exist; the depth is in the space page (groups, accounts, folder access are cards behind the space list:
  4–5 clicks). Give Groups, Accounts, Visitors and Folder access their own sub-pages (`admin?section=&page=&space=`),
  index them in `admin-search.ts`, add "Folder access…" to a folder's menu for administrators. Target: any admin
  page in **at most 3 clicks** from the library. Old addresses kept through `ADMIN_MOVED`.
- [ ] **3.2 Deep single folder opened directly** (IDEA-027 part 1, S–M). A user whose rights reach only `/a/b/c/d`
  sees path-only parents today and clicks through them. On entering the library root, follow path-only folders with
  a single branch down to the first usable one (`access.pathOnly`, server-side so it costs one call). Breadcrumbs:
  path-only parents shown greyed, not clickable. Several branches: the root keeps listing the path-only branches.
- [ ] **3.3 Feature × mode matrix and mode badge** (IDEA-023, S–M). Modes are Local (this computer, administrators,
  no visitors) and External (`Sfe:Mode`, [server-modes](../features/server-modes.md)); limits exist and are
  enforced server-side but are spread out. Write the matrix in docs/ and the note (source of truth), show the mode
  in the ⋮ menu / About, grey out with a reason (2.9) what the mode blocks. No new limit without the owner.
- [ ] **3.4 Used and free space** (IDEA-020, S–M). Free space is read (`LibraryDisk`, uploads, jobs) and Dropbox
  reports space (`IRemoteStorage.SpaceAsync`, capability `FreeSpace`) but nothing shows it. Routes: the library's
  drive (no root path leaked to space administrators) and each connection; a bar "X used of Y, Z free" in the
  sidebar (library and connection place), orange past 85 %, red past 95 %; "not reported" when the provider can't.
  Refreshed every few minutes (cached server-side). The trash size shown in the trash page. No per-user quota.
- [ ] **3.5 Changes made outside SFE shown live** (IDEA-007, M). SFE's own changes already reach open listings
  (SSE change feed, ~400 ms, selection/scroll kept). Add: a file watcher **only on folders someone has open**
  (reference counted, released with the feed; network shares included, with a polling fallback when the watcher
  fails), raising `LibraryBrowser.Invalidate`; for connections, poll the change cursor of open connection folders
  (capability-driven). Per-user off switch. Changes applied quietly (no highlight).
- [ ] **3.6 Scan as you go for library folders** (IDEA-008, M). Connections already sync when opened. For library
  folders: after a listing's first page, index **that folder only** (not recursive) in the background, at most
  once per 10 minutes per folder, never blocking the listing nor colliding with `ScanCoordinator`; removed tagged
  files go through `MissingFileRecorder` as scans do; marks refreshed through the change feed (3.5 finds the
  changes, this keeps the index right). Server setting, on by default. After 3.5.

### Phase 4: files

- [ ] **4.1 Merge folders on a clash** (IDEA-016, M). Today a clashing folder is replaced whole (old one to the
  trash), skipped or kept as `folder1 (2)`; replaced files already go to the trash and picture "Save over" keeps
  the previous version there — part 2 of the idea is done. Add a **Merge** choice: go into the folder, add new
  items, ask per clashing file (replace → trash / skip / keep both, "for all"), recursively. Library jobs
  (`FileJobs`, `LibraryClashes`) and transfers (`TransferRun`). Tests with the scratchpad's case
  (`root/folder1/text1.txt` + incoming `folder1/text1.txt`, `folder1/text2.txt`) and nested folders. Document
  today's behaviour first in move-and-copy.md.
- [ ] **4.2 Previous versions of a file** (IDEA-018, M). Owner's choice: **keep "Trash"** for deleted items and add
  **Previous versions** on a file (menu + properties) listing its `TrashEntry.Kind = PreviousVersion` entries:
  open, restore as a copy, restore over (the current one becomes a version). Technical names (`api/files/trash`,
  `TrashEntries`, `SfeTrash`) unchanged. Version-friendly reading of the trash page ("saved over …" exists).
- [ ] **4.3 Uploads at scale** (IDEA-013, M). Resumable chunked uploads, folder trees, pause/resume/cancel, user
  and admin lists already exist. Missing: a load test (5,000 small files; one file of 100+ GB on a scratch disk,
  as a tool in `agents/tools/`), an end-to-end checksum (client sends a hash per chunk or for the whole file,
  server verifies before finishing), a background sweep of unfinished uploads (today only when listing/starting),
  connection uploads that survive a reload / server restart (records persisted). Fix what the load test finds.
- [ ] **4.4 Ctrl+Z** (IDEA-026, S–M). Toast Undo exists for rename, batch rename, move, delete, duplicate removal.
  Add a session undo stack (last 20, this tab only): Ctrl+Z / Cmd+Z undoes the latest, outside text fields; an
  Undo entry in the ⋮ menu; add undo for create folder (to trash) and copy (copies to trash). Refused with a reason
  when the item changed since. No redo, nothing across reloads or devices, no undo of other users' actions.
- [ ] **4.5 Notification centre** (IDEA-022, M). Every toast goes through `ToastService`: keep them (this browser,
  last 200, 30 days), a bell with an unread count, level (info / success / warning / error replacing the `error`
  flag), entries keep their actions and link to what they're about (folder, upload, task run — add a deep link to
  one run in the task history). Server events (task finished or failed while away) through the change feed,
  for the user who started them; administrators also get failed background tasks.

### Phase 5: guidance

- [ ] **5.1 Help inside the app** (IDEA-012, M). The user docs exist (`docs/`, ~34 pages, screenshots kept by the
  docs-screenshots tool) but aren't shipped or linked. Ship `docs/` in every package (zip, AppImage, Docker), serve
  it under a read-only route (no right needed beyond being signed in; setup pages too), a `help` route rendering it
  with the Markdown viewer (links and images rewritten to the help route), Help in the ⋮ menu, a "?" on each admin
  sub-page opening its page, search over titles and headings (index built at build time). English only (2.2).
- [ ] **5.2 Getting-started checklist** (IDEA-001 part 1, S–M). Setup is already a 3-step wizard. After it, the
  administrator's start shows a checklist: library folder, a group and its rights, folder access, an account,
  a connection; each item opens the wizard or the page, ticks itself when done, can be dismissed.
- [ ] **5.3 Wizards** (IDEA-001 part 2, M each, one per session): **a** new space (folder picker, name, visitors);
  **b** share a folder with someone (account or group → Hidden/Read/Write rule, shows "who has access" at the end);
  **c** add a connection (app key for the provider if missing, then sign in, test, who may use it). Each step has
  an explanation (`sfe-help`) and a link to its help page (5.1), Back is allowed, nothing saved before the last
  step, connections tested before saving. The wizards are found by the admin search; the normal pages stay.
- [ ] **5.4 Home folder** (IDEA-027 part 2, S–M). A home folder per account: default per group set by an
  administrator, changeable by the user (user setting, [settings-levels](../features/settings-levels.md)); never
  created automatically. Shown on the dashboard (5.5) and as "Home" in the sidebar. Library folders only.
- [ ] **5.5 Dashboard as the start page** (IDEA-019, L, split in two). Owner's choice: **dashboard first**.
  **a:** route, widget registry, default layout, start page setting (user can choose dashboard / home / library
  root). **b:** widgets from what exists: home and quick links, recent files (new per-user store, last 50),
  storage (3.4), uploads in progress (4.3), notifications (4.5), connections and their state, tag history;
  administrators: scan status, failed tasks, keep-alive. Layout: fixed columns with add / remove / reorder (no free
  grid), saved per user; administrators set the default layout. Works on phones (one column). After 3.4, 4.5, 5.4.

### Phase 6: connections

- [ ] **6.1 One file API in Sfe.Client** (IDEA-006, M). Owner's choice: **one path-agnostic API**. A common
  interface (e.g. `ISfeFileStore`) implemented by `sfe.Files` and `sfe.Connections[id].Files`: same signatures,
  results and exceptions (move/copy as jobs on both; new methods where today's differ, old ones kept); a parity
  matrix in client-library.md; capabilities say what a store can't do (`SfeNotSupportedException`). One shared test
  suite run against the library and `FakeProvider`, reused by every new provider. Public API snapshot updated.
- [ ] **6.2 Trash on connections: analysis** (IDEA-017, S, document). Today: library trash in the work folder
  (`.sfe/trash` or the server's), SMB shares are library folders (not connections), Dropbox uses its own trash
  (`OwnTrash`). Write the decision for providers without a trash (WebDAV, FTP/SFTP) — e.g. an SFE trash folder on the
  same storage vs. "can't be undone" — per provider table, who sees entries, retention. Owner reviews before 6.5.
- [ ] **6.3 Generic provider setup** (IDEA-009 prep, S–M). Remove the Dropbox-only places outside its folder:
  `DropboxOptions` in `ConnectionOptions`, the Dropbox settings group, `<sfe-provider-setup kind="dropbox">`, the
  admin entry — each provider declares its app settings and setup page. A route listing all known kinds.
- [ ] **6.4 Kinds and levels picked, not typed** (IDEA-005, S–M). `connections.providers` and `connections.levels`
  are `list` textareas. New `multi-select` settings field type (checkboxes); kinds from 6.3's route (empty = all,
  shown as "All kinds"), levels fixed (account, library, server). Kinds validated against registered providers;
  unknown saved values shown flagged, not dropped. After 6.3.
- [ ] **6.5 Google Drive** (IDEA-009, L, split). Owner's order: **Google Drive first**. a: OAuth sign-in,
  browse by id (duplicate names), raw/download, space; b: write (create, rename, move, copy, delete → Drive's
  trash), uploads (resumable sessions); c: thumbnails, index sync with the changes API, Google Docs shown as
  not downloadable (export later). My Drive and shared drives the account sees. Passes 6.1's shared suite.
- [ ] **6.6 WebDAV** (M), then **6.7 Nextcloud** (S–M: WebDAV with app passwords, Nextcloud previews and trash if
  6.2 says so), then **6.8 FTP / FTPS / SFTP** (M–L). Each passes the shared suite; recorded answers in tests, no
  live service in CI (owner's choice: no live tests).

### Phase 7: sync

- [ ] **7.1 Research note** (IDEA-021, S). Owner's choice: **build on SFE transfers**. A short comparison of
  existing tools (rclone + web UIs, Syncthing, rsync web UIs) for robustness and integration, to confirm or
  correct the choice; kept in `agents/docs/features/`.
- [ ] **7.2 Saved sync jobs** (L, split): a: stored job (source and destination: any SFE place), modes **copy
  changes only** and **mirror** (deletions go to the trash), dry run listing what would change; b: schedulable
  (`BackgroundTasks.CanSchedule`, today excludes `Transfer`), run history and failures in the notification centre
  (4.5). Administrators only at first.

### Phase 8: release 2.0

- [ ] **8.1 Hash v2 (breaking)** (IDEA-014, M). Today: SHA-1 of the first 1 MB (`FileFingerprint`), stored in
  `Files.Sha1` (40 chars), used by duplicates (with size), relink, editors' refresh. Owner's choice: **first 1 MB +
  last 1 MB + the file size, new algorithm**: XxHash128 (`System.IO.Hashing`, Microsoft) with a version prefix
  (`2:` + 32 hex = 34 chars, fits the column, name kept); migration clears v1 values (both providers), scans
  recompute (rows with an empty hash are already re-hashed); relink and duplicates ignore v1 values meanwhile and
  say "hashes being recomputed". Sfe.Client `DuplicateGroup.Fingerprint` changes value: breaking, in the changelog.
- [ ] **8.2 Sfe.Client on nuget.org** (IDEA-024, S–M). Package id `Sfe.Client` if free on nuget.org (else
  `Otools.Net.Sfe.Client`, the namespace); SourceLink and `RepositoryUrl`; a workflow job packing and pushing on
  `v*` tags with `NUGET_API_KEY` (owner adds the secret). App and client keep one version.
- [ ] **8.3 README and 2.0.0** (IDEA-024, S). README without the alpha warning: one-line pitch, highlights,
  Docker quick start, badges (build, version, NuGet, licence), fresh screenshots, link to the in-app help.
  `CHANGELOG.md` `## 2.0.0` with breaking changes called out; version 2.0.0 at the merge into `main`
  ([versioning-and-changelog](versioning-and-changelog.md)). Pre-releases (`-rc.1`) allowed by the build scripts.

## Handover (read first when resuming)

Where the work stands, for a session that picks it up. Update it at the end of every session (or when stopping).

- **Last session (2026-11-21):** steps 1.1 to 2.3 and 2.4a done (see their ticks; 2.4 was split in three). Next:
  **2.4b** (sign-in and setup), then **2.4c** (rest of administration: `admin-sections.ts` descriptions still say
  "Colors"), then 2.5–2.8. Texts marked and rewritten to [ui-texts](ui-texts.md), [translations](translations.md)
  says how; `app/access/` is a worked example.
- **Resume:** `git status` on `edge`; anything uncommitted belongs to the step named here. Run `npm test` (from
  `C:/workspace/…` in Git Bash, not `/c/…`: [testing-client](testing-client.md)), the step's e2e specs
  (`cd tests/e2e; npm run e2e -- specs/<file> --project=android --project=iphone`) and
  `node agents/tools/readability/measure.mjs --check` before each commit.
- **Commits per step:** a refactoring needed to make room goes first in its own commit (with `--write-baseline` when
  it improved a file), then the step's commit (code, tests, changelog, notes, user docs, its tick here).
- **Traps met:**
  - `core/api.service.ts` sits exactly at its readability baseline (file length): a new route there needs lines freed
    elsewhere in it (move helpers or models out in a refactoring commit first).
  - The e2e web server's first build can fail with "No file exists for the asset … wwwroot\chunk-….js" (stale
    wwwroot after a client build); the run's own second build succeeds.
  - No Python on this machine: scripted edits with `node -e` or the edit tools.
  - A refactoring that frees lines: write the baseline, but the step after it may raise it back a little (as 1.1 and
    1.2 did); `measure.mjs --write-baseline` alone writes it (with `--check` it doesn't).
  - Client specs run with `npm test -- --include <path>` (not `npx vitest`).
  - `FileJobs.cs` is at its baseline again after 1.4: new job code needs lines moved out first (the copy part —
    `CopyAsync`, `CopyFolderAsync`, `CopyFileAsync`, `AfterCopyAsync`, `CopyCache` — is the next candidate).
  - Scripted C# edits from `node -e` in Git Bash: write the script to a file with a quoted heredoc (`<<'EOF'`);
    `
` inside JS template strings becomes a real newline in the C# source otherwise.
  - Texts marked for translation: `node agents/tools/i18n/extract.mjs` after, `--check` before each commit (~20 s
    each); `SFE_TEXTS_BASELINE=write npm test -- --include src/app/core/i18n` when an area's unmarked texts drop.
  - Code generated through the Bash tool (`node -e`, heredocs) loses `\` line continuations and `\n` escapes (they
    become real breaks or vanish; a regex `\b` became an invisible backspace in a spec): write texts with the
    Write / Edit tools, and count lengths by characters
    (`[...line].length`; `awk length` counts bytes, and `“ ”` are 3).
  - Templates are not Prettier-formatted: never run `prettier --write` on an `.html` (it re-indents everything).
  - The first `dotnet test` after a client build (`npm test` builds too) fails on a stale `wwwroot` chunk ("No file
    exists for the asset"): run it again, the second build succeeds.

## Decisions

Owner's answers (2026-11-20): 2.0 = everything; connectors Google Drive first; languages: prepare only; sync on SFE
transfers; Trash kept + per-file previous versions; Merge added; hash with size and a new algorithm; dashboard first;
nuget.org public; one path-agnostic client API; watch open folders (disk and connections); **no live Dropbox tests**
(IDEA-004 dropped: recorded/fake tests and e2e mocks stay, testing rules already require tests).

Defaults taken (change here if the owner disagrees): UK English; Windows-Explorer-like menu order; admin pages in
≤3 clicks; XxHash128; notifications kept in the browser (server events pushed, not stored); undo per tab, 20 steps;
dashboard fixed columns; home folder never auto-created; scan-as-you-go not recursive, 10 minutes; storage bar
thresholds 85/95 %; sync jobs for administrators only.

## What the check found already done (don't redo)

Resumable uploads (folders, pause, admin list, Sfe.Client resume); trash for deletes, replaces and saved-over
versions with restore and purge; clash choices with "for all" and keep-both names; live refresh for SFE's own
changes; Local/External modes enforced server-side; admin tabs, per-page URLs and search; provider abstraction with
capabilities (Dropbox on it); SemVer, `CHANGELOG.md`, Sfe.Client packed; user docs with screenshots; toast Undo.
