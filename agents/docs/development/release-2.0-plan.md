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
- [x] **2.4–2.8 Strings extracted and texts rewritten, area by area** (IDEA-010 + IDEA-011, M each). Each string is
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
  - [x] **2.4b sign-in and setup** (`app/account/`, `app/migration/`, local-only page; ~190 texts). Done
    2026-11-21: 7 files at 0 unmarked texts (180 gone); setup-code steps one `$localize` each (no `+`), where a
    generated code is kept now two whole sentences (`generatedCodeWhere`), no fragments glued in templates; texts
    chosen by a condition as `@if` around `<ng-container i18n>`; the recovery code's minutes an ICU plural; "as a
    guest" → "as a visitor"; My account's intro says "settings" (its tab is still "Preferences": 2.4c). The update
    page's confirm left for 2.8. `local-only.png` not retaken (one word changed; taken by hand).
  - [x] **2.4c administration pages and tokens** (`app/admin/`, `app/tokens/`; split from the rest on 2026-11-21:
    tasks build their sentences from fragments, a session of its own). Done 2026-11-21: 9 files at 0 unmarked texts
    (123 gone); section names and descriptions in `$localize` ("Colours", "space", "task"); groups keyed
    (`AdminGroup`, `ADMIN_GROUP_TITLES`: the overview's anchors stay `admin-group-<key>`, never translated); My
    account's "Preferences" page named **Settings** (id `preferences` kept); tokens say "space"; `plural()` out of
    the area; titles built with `+` now `title="… {{ }}" i18n-title` or `$localize` in code; confirms and toasts
    left for 2.8. Readability baselines raised by the added lines: `admin-page.component.ts` 500 → 521, its
    template 474 → 501, `admin-sections.ts` 435 → 499.
  - [x] **2.4d tasks, logs and connections** (`app/tasks/`, `app/logs/`, `app/connections/`). Done 2026-11-21:
    7 files at 0 unmarked texts (140 gone); `tasks.ts` helpers are whole ICU messages (`describeSchedule` one
    `select` per kind, no `capitalize`/`uncapitalize`, `formatDays` gone, days joined by `Intl.ListFormat`); task
    names never lower-cased into sentences (titles quote them: “Index scan”); `LEVEL_NAMES` (connections, in
    `core/`) marked; level-dependent buttons and titles as ICU `select`; `plural()` out of the area; the connect
    dialog's template re-indented by hand. Toasts, confirms and `connections-landing.ts` left for 2.8.
  Toasts and confirms of these areas stay for 2.8 (as in 2.3).
  **2.5 split** (2026-11-22: `app/library/` ~8k lines of TS and `menus/` 1k, ~160 unmarked template texts, many
  texts in code; tags, duplicates and trickplay had no area):
  - [x] **2.5a top bar, sidebar, location, quick links** (`library/topbar/`, `library/sidebar/`,
    `library/location/`, `library/quick-links/`). Done 2026-11-22: 9 files at 0 unmarked texts (86 gone); `plural()`
    out of these folders (the quick links' "added" toast an ICU plural); host `aria-label`s bound to `$localize`
    fields; search syntax rows as sentences, tracked by `$index`; ⋮ "Toggle fullscreen" → **Full screen**, "Guest"
    → **Visitor**; the folder buttons' hints "Go up to “…”", "This is folder 2 of 5."; the menu's "Remove from
    index…" kept (settings and docs name it). `default-tags-badge`'s "the top folder" left for 2.5d. Screenshots
    `main-menu.png`, `search-help.png` retaken; the docs-screenshots tool follows the new hints. `icu()` brought
    `intl-messageformat` into `main`: the initial error budget raised from 1 MB to 1.1 MB.
  - [x] **2.5b listing and menus** (`library/items/`, `library/everywhere/`, selection bar, `library/folder-menu.ts`,
    `files/item-menu.ts`, `menus/`, `library/*.ts`: connection problems and pages). 2.9 reorders the menus; the
    labels marked here move with them. Done 2026-11-22: 5 templates at 0 unmarked texts (34 gone) and the menus'
    labels and hints in code; hints full sentences with names quoted; `pasteTitle` names the target folder (the
    Ctrl+V key left to the menu's `<kbd>`); `extractEachTitle` shared (`extract.dialog.ts`); selection bar hints kept
    once in code (`hints`, bound in the template and the "⋯" menu); a file's or folder's hint one fact per line;
    exclusion hints by `Intl.ListFormat` ("Set on the folder “Media”."); HR/LR marks (`config.service.ts`) and the
    partial marks (`partial-files.ts`) marked with the listing; "Go to library root" → **Go to the top of the
    library**, connection heading **This connection needs signing in again**; `plural()` out of the area. Toasts
    left for 2.8. No screenshot retaken (menus show labels, unchanged); the docs-screenshots tool follows the new
    check box labels (`Select “…”`). Readability baselines raised by the added lines: `item-menu.ts` 527 → 530,
    `context-menu.ts` 778 → 785.
  - [x] **2.5c scan and the wording of searches** (`library/scan/`, `library/search-explain/`: sentences built from
    the query's parts, a session of its own). Done 2026-11-22: a refactoring first split `search-explain.ts` (520
    lines) into `search-query.ts` (parsing), `search-segments.ts` (pieces) and `search-explain.ts` (wording); the
    explanation's sentences are whole messages whose `slot()` placeholders take the styled pieces (`phrase()`,
    [translations](translations.md)); logical words in `OPS`; the notes on ignored terms in `search-notes.ts`;
    5 files at 0 unmarked texts (36 gone); `describeScan`, the panel's titles and the remove dialog's counts as ICU
    plurals (`plural()` out of the area); the scan service's toasts and confirms left for 2.8. No screenshot
    retaken (the explained query's words are unchanged); the screenshot tools find "Hide this panel" by its start.
  - [x] **2.5d tags** (`app/tags/`: dialogs of a file, of several, of a folder or search, the tag editor and manager,
    lists, picker, chips, default tags badge; ~78 template texts, 15 `plural()` calls). Split from 2.5e on
    2026-11-22 (the three folders held ~121 template texts and 36 `plural()` calls). Done 2026-11-22: 8 files at 0
    unmarked texts (78 gone); the scope dialog's wording moved to `scope-tags-texts.ts` (pure functions: the "where"
    phrase is one of six whole messages, used as a placeholder in the questions); `removedFrom` returns a sentence
    without its leading space; `libraryFiles`, `listed` (`Intl.ListFormat`) in `tags.ts`; "Disambiguate with" →
    **Tell apart with**, "Color" → **Colour** (glossary leftover, docs too); the library's own folder is "the
    library" (default tags) or "the top of the library" (the badge, as the location bar); `plural()` out of the
    area; toasts left for 2.8 except those that counted files. Tag screenshots retaken.
  - [x] **2.5e duplicates, missing files, similar videos, trickplay** (`app/tracking/`, `app/trickplay/`; ~43
    template texts, 21 `plural()` calls). Done 2026-11-22: 7 files at 0 unmarked texts (43 gone); the panels'
    summaries in `tracking-texts.ts` and the task's counts in `trickplay-texts.ts` (pure functions, specs); details
    are whole sentences joined by a space instead of " · " fragments (the trickplay counts keep " · ": numbers);
    "where" a placeholder message ("in the library" / "in “…”"), as in 2.5d; the removal question split into two
    messages ("Move this file to the trash?" + "Copy kept:" / "Its tags go to the copy kept:") around the select;
    conditional `[title]`s from code (`removeTitle`, `confirmTitle`); trickplay sections tracked by a `key`;
    "the library root" → "the top of the library"; the listing's trickplay mark says "previews along the timeline"
    (users' word); `plural()` out of the area, also in the relink toast and the "Forget all" confirm; other toasts,
    confirms and the "Choose file…" prompt left for 2.8. Admin screenshots of the area retaken.
  **2.6 split** (2026-11-22: `app/files/` held ~170 unmarked template texts in 12 files and 40 `plural()` calls):
  - [x] **2.6a dialogs putting things somewhere** (name, delete, transfer and its folder picker, batch rename,
    compress, extract; ~62 template texts, 19 `plural()` calls). Done 2026-11-22: 7 files at 0 unmarked texts (62
    gone); the sentences in `file-dialog-texts.ts` (pure functions, spec): `counted()` ("2 folders and 3 files", one
    ICU `select`), the delete question and its "what" phrase, move/copy prompts and questions by `kind` `select`, the
    access gained as whole sentences ("The group “Visitors” would see “Plans”, which they don't see now."), the zip's
    size line; a new item at the top says "At the top of the library"; batch rename's problems are sentences;
    `plural()` out of these dialogs (also their counted toasts); other toasts left for 2.8. Screenshots
    `batch-rename-dialog.png`, `extract-dialog.png` retaken; the docs-screenshots tool clicks `Open “Family”`.
  - [x] **2.6b properties and trash** (`properties.dialog`, `trash-section`, `place-properties`; ~56 template
    texts). Done 2026-11-22: 2 templates at 0 unmarked texts (56 gone); the sentences in `properties-texts.ts`
    (sizes, totals, the count on the disk, checksum progress, attributes by `Intl.ListFormat`, `itemKind` and
    `folderOf` moved from `place-properties.ts`: a kind with and without extension, one `select` each) and
    `trash-texts.ts` (intro, an entry's "Deleted … by …." sentence, folder hints), pure functions with specs; the
    count's "(stopped)" fragment a sentence of its own (the server's `Stopped.` word still compared, never shown);
    errors keep the reason, the template writes "Could not read the properties: …"; Restore's hint still starts
    "Put it back" (the docs-screenshots tool clicks it); `plural()` out of these files (also the empty-trash
    toast); copy toasts, restore toasts and confirms left for 2.8. Screenshots `properties-*.png`,
    `admin-files-trash.png` retaken.
  - [x] **2.6c operations and uploads** (operations panel, uploads panel and section, `file-transfers.ts`,
    `place-transfers.ts`, `uploads.ts`; ~52 template texts and many texts in code). Done 2026-11-22: 3 templates at
    0 unmarked texts (52 gone); the sentences in `operations-texts.ts` (titles by kind and state as ICU `select`s,
    the destination a phrase "to “…”" / "from … to …", status lines, `timeLeft` moved there, clash sides, clipboard,
    the toasts ending a move or copy, `couldNotStart` by kind: `pastOf` gone) and `upload-texts.ts` (titles, status,
    each file's state — "done", "skipped", "cancelled" no longer the raw code word —, toasts, the administration's
    line), pure functions with specs; the panel's host `aria-label` bound ("Operations and uploads"); Paste here's
    hint is `pasteTitle`; template ICU plurals over a call through `@let`; the operations' and uploads' toasts all
    marked (the uploads section's confirm and toasts left for 2.8); `plural()` out of `app/files/`. Screenshots
    `operations-panel.png`, `upload-panel.png`, `admin-files-uploads.png` retaken (scenes `files`, `uploads`).
    A 121-character line left by 2.6b in `properties-texts.ts` broken (same message).
  **2.7 split** (2026-11-22: ~334 unmarked template texts in `app/formats/`, `app/editor/`, `app/player/`,
  `library/viewer/`, `shared/`, and ~9k lines of TS with many texts in code; ~50 template texts a step):
  - [x] **2.7a viewer shell, player, picture editor, shared** (`library/viewer/`, `app/player/`, `app/editor/`,
    `app/shared/`, `formats/` top level: `view-page`, `formats.ts`, `toolbar-entries.ts`, `virtual-rows.ts`; ~61).
    Done 2026-11-22: 9 files at 0 unmarked texts (61 gone); the player's texts in `player-texts.ts` (a short name per
    control for `aria-label` and menus, a hint sentence with the key; on/off buttons keep one name), the editor's in
    `editor-texts.ts` (size line, where Save and Save a copy write as whole sentences, aspect and flip hints), pure
    functions with specs; the viewer's menu reuses the item menu's wordings; "Fullscreen" → **Full screen** (app,
    docs, e2e), the viewer's arrows **Previous file** / **Next file**, its dialog named "Viewer", the editor's
    "Picture editor"; format labels and the document toolbars' shared entries marked; `sfe-help`'s name and hint
    from code; `virtual-rows.ts` counted a comment as a template (reworded). Toasts and confirms left for 2.8.
    Screenshots `image-editor*.png` retaken (scene `viewer`). Viewer readability baseline 823 → 824 (an import).
  - [x] **2.7b text viewer and editor, reading settings** (`formats/text/`, `formats/reading/`; ~81, much code).
    Done 2026-11-23: 4 templates at 0 unmarked texts (81 gone); sentences in `text-texts.ts` (toolbar names and
    hints, the part range, encoding and line-ending names — codes such as "UTF-8" stay as they are —, failures by
    status, save toasts, CodeMirror's own panel words through `EditorState.phrases`, the editor's label) and
    `reading-texts.ts` (widths as ICU, margins, sizes), pure functions with specs; fonts and reader themes marked in
    `reading.ts`; line numbers through `lineNumbersEntry` (`toolbar-entries.ts`); this area's toasts and confirms
    marked here rather than in 2.8a (same files); "Aa" in `translate="no"`; the docs' find words follow CodeMirror's.
  - [x] **2.7c JSON and CSV** (`formats/json/`, `formats/csv/`; ~51). Done 2026-11-22: 2 templates at 0 unmarked
    texts (51 gone); sentences in `json-texts.ts` (modes, Format / Open all / Close all, the filter's matches, keys
    and items, Show … more, the parse problem with "(and # more)", jsonc-parser reasons by code instead of the
    `PROBLEMS` map) and `csv-texts.ts` (modes, Header, a whole message per separator by a stable key — `DELIMITERS`
    gets `key`, `delimiterName` gone, menu ids `separator-<key>` —, the status counts as one ICU, sort hints by state,
    papaparse's problems by code, its English as fallback), pure functions with specs; "Aa" in `translate="no"`.
  - [x] **2.7d Markdown and subtitles** (`formats/markdown/`, `formats/subtitles/`; ~42). Done 2026-11-22: 2
    templates at 0 unmarked texts (42 gone); sentences in `markdown-texts.ts` (modes, outline, reading hint, images
    of other sites held back as ICU, a held-back image's placeholder) and `subtitles-texts.ts` (modes, the counts line
    as ICU pieces, the selected cue, the first problem with "(and # more problems)", list label, overlap, empty, Play),
    pure functions with specs; mode types moved there; toolbar hints match `toolbar-entries.ts`; "Aa" in
    `translate="no"`. Its line in `docs/user-guide/text-files.md` is committed with 2.7b (same page).
  - [x] **2.7e PDF, EPUB, Flash** (`formats/pdf/`, `formats/epub/`, `formats/flash/`; ~50). Done 2026-11-22: 3 files
    at 0 unmarked texts (50 gone); texts built in code in `pdf-texts.ts` (zoom names and hints from `SCALE_VALUES`,
    page count and matches as ICU), `epub-texts.ts` (layouts, progress), `flash-texts.ts` (a short name and a hint
    per button), pure functions with specs; failures "Could not … : reason." without "It can only be downloaded."
    (wrong without the download right); "Not found" → **No matches**, "Sound on" → **Unmute**; **Aa** in
    `translate="no"`; `flash-player.ts`'s frame title and Ruffle's reasons marked. `messages.xlf` written with 2.8.
  - [x] **2.7f archive viewer** (`formats/archive/`; ~49). Done 2026-11-22: `archive-view.component.html` at 0
    unmarked texts (49 gone); its sentences in `archive-texts.ts` (crumb and Find hints, reading so far, entries left
    out, sizes and the folder's bottom line as one ICU message, the search's status by place as an ICU `select`,
    password and failure messages), pure functions with a spec; `fileWord()` and "entry is / entries are" pieces
    gone; the entry bar's arrows **Previous file** / **Next file** as in the viewer (docs); names quoted with
    `quoted()`; `i18n-*` kept on their attribute's line (template within its 403-line baseline); component 928 → 888.
  **2.8 split** (2026-11-22: ~200 toast calls and ~40 confirms in the client; `RightCatalog` 650 lines; ~470 sentence
  strings in `Sfe.Api`/`Sfe.Core`, most not shown to users):
  - [x] **2.8a toasts and confirms** (every `ToastService` call, toast action, `confirm`/`prompt` of the client).
    Done 2026-11-23: every toast, toast action, `confirm` and `prompt` marked (`$localize`, `icu()` for counts) and
    rewritten: success "… saved." / "… is in the trash.", errors "Could not *verb* *object*: reason" with the thing
    named, questions saying what happens; whole messages in the areas' texts files where they choose between
    sentences (`settings-texts.ts`, `scan-texts.ts`, `tracking-texts.ts`'s `removedText` / `notPutBack`, …), pure
    functions with specs; `CANVAS_REFUSED` marked. Readability baselines raised by the added lines:
    `image-editor.dialog.ts` 445 → 449, `file-transfers.ts` 467 → 469, `item-menu.ts` 530 → 534, `listing.store.ts`
    622 → 626, `viewer.component.ts` 824 → 827.
  - [x] **2.8b rights' names and descriptions** (`RightCatalog` and the other right lists; keys unchanged).
    Done 2026-11-23: names start with a verb ("Move to trash and restore", "Remove duplicates", "Change … settings",
    "Manage spaces"), descriptions are sentences in the second person; `ConnectionRights` and `AccessTokenRights`
    too; the docs' rights tables and the client's texts naming a right follow ("Remove duplicates").
  - [x] **2.8c problem details and other server texts users see** (checked against the glossary and style; strings
    the client or tests compare and problem `type` words unchanged). Done 2026-11-23: problem `detail`s and
    validation messages as sentences ("Invalid library path." → "This is not a folder of the library."), "job" →
    "operation", "provider" → "connection" / "storage service", "guests" → "visitors", "folder access rules" →
    "folder rules", "the library's root" → "the top of the library"; "You may not …" kept (the client's texts use it
    too); reasons that are a clause in a longer message ("“x” could not be read") left as they are. The tests'
    expected reasons follow. `messages.xlf` written for 2.7b–2.8c.
  Steps 2.7b–2.8c were done by subagents in parallel (one per folder; the coordinator wrote the shared files:
  `messages.xlf`, `hard-coded-texts.json`, readability baseline, changelog, this plan) and committed one by one.
- [x] **2.9 Menus grouped, one action registry** (IDEA-025, M). Done 2026-11-23: `MenuEntry.group` (a line between
  groups); `ItemCommands` (`files/item-commands.ts`, commands in four files by area, `ItemScope`) feeds the item
  menu, the selection bar (buttons and "⋯") and the folder menu (quick link, Copy full path); order as below, **Open
  as ▸** and **Archive ▸** flat when one entry is left; greyed by rights with the reason in the hint (under the label
  on touch screens), hidden by type / selection / server / admin right; the bar shows only allowed actions. With
  several selected the menu's Tags… tags the selection's files, Add to quick links adds all, Rename… batch only
  outside searches. Folder menu grouped (Refresh first). Note context-menu.md; screenshots `item-menu.png`,
  `background-menu.png`, `phone-item-menu.png`, `phone-folder-menu.png` retaken. Readability baselines raised by
  the added lines: `context-menu.ts` 785 → 787, `file-management.css` 543 → 559; `item-menu.ts` (534) now within the
  limits, `folder-menu.ts` without its long function. Was: today `MenuEntry` has no groups, Delete sits in
  the middle, items that don't apply are hidden, and `selection-bar.component.ts` redefines actions. Add `group`
  (dividers) to `MenuEntry`; one action registry used by the item menu, folder menu and selection bar; order
  Open (Open, new tab, containing folder, **Open as… ▸**) → Edit (Edit, Crop) → Clipboard (Cut, Copy, Paste) →
  Organise (Rename, Move to, Copy to, Tags, quick link) → Archive ▸ (Compress, Extract) → Download / Copy path →
  Delete → Properties (Windows Explorer-like). Shortcuts stay shown. Not-applicable items stay **hidden** for type
  and selection; items blocked by rights or mode are **greyed with a reason** (used by 3.3). Update
  context-menu.md, user guide, screenshots, e2e menu specs.

### Phase 3: getting around

- [x] **3.1 Shallower administration** (IDEA-002, S–M). Done 2026-11-23: a refactoring first split the space page's
  cards into `SpaceGroupsComponent`, `SpaceMembersComponent`, `SpaceVisitorsComponent` (fixtures in
  `src/testing/access.ts`); Spaces & accounts gained **Groups**, **Folder access**, **Visitors** sub-pages
  (`SpacePartComponent`: `space=` or the session's space, a **Space** picker for server administrators); Accounts with
  `space=` is that space's members card, without it every account as before; a space's page is its library and four
  links; `pageParams` keeps `space` between the section's sub-pages. Nothing moved (no `ADMIN_MOVED` entry: old
  `space=` addresses open the space's page). The admin search finds the sub-pages with no extra work. **Folder access…**
  (`OrganisingCommands`, item, folder and folder-link menus; `tenant.members`, library only) opens
  `page=folders&folder=` with a rule started there. Computers: 3 clicks (⋮, Administration, overview link); phones 4
  (the section list, then chips). Was: Tabs, per-page URLs, phone chips and the admin search
  already exist; the depth is in the space page (groups, accounts, folder access are cards behind the space list:
  4–5 clicks). Give Groups, Accounts, Visitors and Folder access their own sub-pages (`admin?section=&page=&space=`),
  index them in `admin-search.ts`, add "Folder access…" to a folder's menu for administrators. Target: any admin
  page in **at most 3 clicks** from the library. Old addresses kept through `ADMIN_MOVED`.
- [x] **3.2 Deep single folder opened directly** (IDEA-027 part 1, S–M). Done 2026-11-23: `FolderChecks.StartFolder`
  (path-only folders followed while one way leads down and exists), `access.start` on every restricted `browse`;
  `ListingStore.leadsToStart` replaces a folder above it (the top) by it in the history, `passesThrough` by depth
  greys those crumbs, disables up and Backspace; several ways: links as before (decision in folder-access.md). No
  screenshot (a greyed crumb). Was: A user whose rights reach only `/a/b/c/d`
  sees path-only parents today and clicks through them. On entering the library root, follow path-only folders with
  a single branch down to the first usable one (`access.pathOnly`, server-side so it costs one call). Breadcrumbs:
  path-only parents shown greyed, not clickable. Several branches: the root keeps listing the path-only branches.
- [x] **3.3 Feature × mode matrix and mode badge** (IDEA-023, S–M). Done 2026-11-23: the matrix is a table in
  `docs/administration/system.md#what-each-mode-allows` (source of truth; the note says where each line is enforced);
  `core/server-modes.ts` names the mode; a `.mode-badge` after the version in the ⋮ menu (summary as hint), a
  **Mode** line in System › About; `SettingField.onlyIn` greys a setting out in the other mode with "Only in Local
  mode: …" (only `fastSignIn`: visitors and paths were already greyed; no item-menu action depends on the mode). No
  server change, no new limit. Screenshots `main-menu.png`, `admin-system.png` retaken. Was: Modes are Local (this
  computer, administrators, no visitors) and External (`Sfe:Mode`, [server-modes](../features/server-modes.md));
  limits exist and are enforced server-side but are spread out. Write the matrix in docs/ and the note (source of
  truth), show the mode in the ⋮ menu / About, grey out with a reason (2.9) what the mode blocks.
- [x] **3.4 Used and free space** (IDEA-020, S–M). Done 2026-11-23: `GET api/storage` (`library.browse`; the
  library's disk by `DiskSpace`: `GetDiskFreeSpaceEx` on Windows — shares too —, `statvfs` of the folder elsewhere;
  numbers only, 5-second wait) and `GET api/connections/{id}/storage` (`SpaceAsync` when the capability says so),
  both kept two minutes (`StorageUsageCache`); `StorageBarComponent` in the sidebar's footer, read on a place change
  and every 5 minutes while shown, orange ≥ 85 %, red ≥ 95 %, "Free space not reported"; the trash's first page gains
  `size` (`SUM`, for those who see the whole trash), shown on the trash page. Glossary: **disk**. `Sfe.Client` leaves
  the routes to `Raw` (6.1). Note [storage-usage](../features/storage-usage.md); screenshots `sidebar-trash.png`,
  `admin-files-trash.png`. Readability baselines raised by the added lines: `FileEndpoints.cs` 841 → 850, `Trash.cs`
  885 → 894, `api.service.ts` 788 → 794. Was: Free space is read (`LibraryDisk`, uploads, jobs) and Dropbox
  reports space (`IRemoteStorage.SpaceAsync`, capability `FreeSpace`) but nothing shows it. Routes: the library's
  drive (no root path leaked to space administrators) and each connection; a bar "X used of Y, Z free" in the
  sidebar (library and connection place), orange past 85 %, red past 95 %; "not reported" when the provider can't.
  Refreshed every few minutes (cached server-side). The trash size shown in the trash page. No per-user quota.
- [x] **3.5 Changes made outside SFE shown live** (IDEA-007, M). SFE's own changes already reach open listings
  (SSE change feed, ~400 ms, selection/scroll kept). Add: a file watcher **only on folders someone has open**
  (reference counted, released with the feed; network shares included, with a polling fallback when the watcher
  fails), raising `LibraryBrowser.Invalidate`; for connections, poll the change cursor of open connection folders
  (capability-driven). Per-user off switch. Changes applied quietly (no highlight). **Split** (2026-11-23: the
  server didn't know which folder a tab shows; the connection part polls the providers, a session of its own):
  - [x] **3.5a library folders**. Done 2026-11-23: the feed opened with `?tab=`, `PUT files/changes/watch` names
    the folder a tab shows (each move, each time the feed opens); `FolderWatchers` (per library, reference counted,
    256 at most) watch it with `FileSystemWatcher` (that folder only) or poll it (`FolderSignature`, every 10 s at
    least, a tenth of the time at most) on a network mount outside Windows or when the watcher fails; settled 300 ms,
    at most every 2 s per folder; `LibraryBrowser.Forget` (cache without the event) then `changed` with
    `outside: true`; `.sfe-partial` items, the work folder and SFE's own changes (within 1 s) left out; user
    setting `browse.liveChanges`. Note in change-feed.md. Screenshot `admin-interface-browsing.png` retaken.
    Refactoring first: `editingSession()` out of `api.service.ts`.
  - [x] **3.5b connection folders**. Done 2026-11-23: `PUT files/changes/watch` takes `connection` (followed when
    the caller uses it, sees the folder and the provider has `ChangeCursor`); `ConnectionWatchers` (per library,
    reference counted, 256 folders at most) read a watched connection's changes every 30 s **through the index**
    (`ConnectionIndexes.FollowAsync`: a quiet run, not in the history nor shown as running, a sync waits for it;
    only when the index is `Following`, else as opening the connection; counts and purge only when changes came),
    so the index's listing is right when the client lists again; `changed` with `connection` and `outside` for
    the watched folders touched (parents of the changes, 1,000 at most), filtered as SFE's own; `RemoteListings`
    forgotten. SFE's own changes come back once (harmless refresh). Client names `{ path, connection }`. No
    screenshot (nothing visible). Lines over 120 characters left by 3.5a fixed; `api.service.ts` baseline 788 → 789.
- [x] **3.6 Scan as you go for library folders** (IDEA-008, M). Done 2026-11-23: `ListingIndexer` (per library)
  queues the folder of a browse's first page (after the page is built), at most once per 10 minutes, one folder at a
  time, `IndexAsync(recursive: false)`; `ScanCoordinator.TryBeginFolderIndex` keeps it off folders a scan or an
  operation works on (a scan waits for it); `Invalidate` only when the index changed; no relink pass (left to scans);
  library setting `scan.indexWhenListed` (on; off in `TestLibrary`). The indexer now removes rows by name and folder
  (a file renamed meanwhile kept). Remove from index's hint and docs say opening undoes it (`remove-from-index.png`
  not retaken: a few words of a hint). Screenshot `admin-scanning-indexing.png` retaken. Readability baselines
  raised by the added lines: `LibraryEndpoints.cs` 576 → 579, `DirectoryIndexer.cs` 712 → 713 (`Browse`'s index
  services in `BrowseIndex`). Was: Connections already sync when opened. For library
  folders: after a listing's first page, index **that folder only** (not recursive) in the background, at most
  once per 10 minutes per folder, never blocking the listing nor colliding with `ScanCoordinator`; removed tagged
  files go through `MissingFileRecorder` as scans do; marks refreshed through the change feed (3.5 finds the
  changes, this keeps the index right). Server setting, on by default. After 3.5.

### Phase 4: files

- [x] **4.1 Merge folders on a clash** (IDEA-016, M). Done 2026-11-24: a refactoring first moved the writing of copies
  out of `FileJobs` into `LibraryCopies`; `ConflictChoice.Merge`, `canMerge` on the conflict (two folders, no link,
  the one there not holding the one coming, the job's right in it), `merged` on the result; library moves and copies
  run each entry through `ItemAsync` (`MergeAsync`, `MergeTally`: one result per folder), a moved folder emptied is
  removed with its own rules and default tags; extraction places recursively (`PlaceAsync`); transfers keep the name
  (`CreateFolderAsync` gives the folder there); moves inside a connection don't merge; uploads take `merge` as `ask`.
  "For all" answers kept apart for folders that may merge and the rest (changelog: Changed); Undo leaves merged
  folders out. Note move-and-copy.md (Name clashes); screenshot `merge-folders.png` (scene `merge`).
  Was: Today a clashing folder is replaced whole (old one to the
  trash), skipped or kept as `folder1 (2)`; replaced files already go to the trash and picture "Save over" keeps
  the previous version there — part 2 of the idea is done. Add a **Merge** choice: go into the folder, add new
  items, ask per clashing file (replace → trash / skip / keep both, "for all"), recursively. Library jobs
  (`FileJobs`, `LibraryClashes`) and transfers (`TransferRun`). Tests with the scratchpad's case
  (`root/folder1/text1.txt` + incoming `folder1/text1.txt`, `folder1/text2.txt`) and nested folders. Document
  today's behaviour first in move-and-copy.md.
- [x] **4.2 Previous versions of a file** (IDEA-018, M). Done 2026-11-24: `PreviousVersions` (Core) and
  `api/files/versions` (list by `path`, `{id}/content`, `{id}/copy`, `{id}/restore`); seen by whoever may open the
  file; restore over with the rights of saving over (not in a space that deletes for good), the current content kept
  as a version first, a new modification date; versions follow renames and moves (`PathMoves`); no migration (the
  trash's `Kind` already existed). Client: **Previous versions…** (item menu, group `properties`), the sheet
  `VersionsDialog`, a line in Properties; the listing's dialogs gathered in `library/listing-dialogs.ts` and the query
  helpers of `api.service.ts` moved to `core/api-query.ts` to stay within the readability limits. Note
  [previous-versions](../features/previous-versions.md); screenshot `previous-versions.png` (scene `versions`).
  Sfe.Client leaves the routes to `Raw` (6.1). Was: Owner's choice: **keep "Trash"** for deleted items and add
  **Previous versions** on a file (menu + properties) listing its `TrashEntry.Kind = PreviousVersion` entries:
  open, restore as a copy, restore over (the current one becomes a version). Technical names (`api/files/trash`,
  `TrashEntries`, `SfeTrash`) unchanged. Version-friendly reading of the trash page ("saved over …" exists).
- [ ] **4.3 Uploads at scale** (IDEA-013, M). Resumable chunked uploads, folder trees, pause/resume/cancel, user
  and admin lists already exist. Missing: a load test (5,000 small files; one file of 100+ GB on a scratch disk,
  as a tool in `agents/tools/`), an end-to-end checksum (client sends a hash per chunk or for the whole file,
  server verifies before finishing), a background sweep of unfinished uploads (today only when listing/starting),
  connection uploads that survive a reload / server restart (records persisted). Fix what the load test finds.
  **Split** (2026-11-24: four independent parts, `Uploads.cs` already 629 lines):
  - [x] **4.3a background sweep and chunk checksums**. Done 2026-11-24: a refactoring first moved the records
    (`UploadRecords`) and the place checks (`UploadPlaces`) out of `Uploads` (629 → 483 lines; 523 after this step); `UploadSweepWorker`
    (per library, at start and every 10 minutes; an upload busy with a chunk or a finish skipped, read again under its
    gate); `ChunkDigest` (`Content-Digest: sha-256=:…:`, RFC 9530, optional: old clients unchecked), checked while
    the library writes (truncated back on a mismatch) and on the part in memory before a connection's provider gets
    it, 400 `chunk-digest` (`ProblemTypes`); the page (`chunk-digest.ts`: `crypto.subtle`, `@noble/hashes` on
    demand on plain http; `readChunk` sends the bytes it hashed) and Sfe.Client send it and retry a damaged chunk as
    a try. No whole-file hash (a browser can't stream a digest). No screenshot (nothing visible). `api.service.ts`
    kept at its baseline (`UploadChunk` and `chunkHeaders` in `core/api-query.ts`); `uploads.ts` 449 → 450 (an
    import).
  - [ ] **4.3b connection uploads kept across restarts**: `RemoteUploads` records written to the work folder (the
    provider's session id is a secret: stored as the connection's credentials are), listed after a reload, swept in
    the background.
  - [ ] **4.3c load test** (`agents/tools/upload-load/`: 5,000 small files through `Sfe.Client`, one file of 100+ GB
    on a scratch disk, timings) and fixes of what it finds.
- [ ] **4.4 Ctrl+Z** (IDEA-026, S–M). Toast Undo exists for rename, batch rename, move, delete, duplicate removal.
  Add a session undo stack (last 20, this tab only): Ctrl+Z / Cmd+Z undoes the latest, outside text fields; an
  Undo entry in the ⋮ menu; add undo for create folder (to trash) and copy (copies to trash). Refused with a reason
  when the item changed since. No redo, nothing across reloads or devices, no undo of other users' actions.
- [ ] **4.5 Notification centre** (IDEA-022, M). Every toast goes through `ToastService`: keep them (this browser,
  last 200, 30 days), a bell with an unread count, level (info / success / warning / error replacing the `error`
  flag), entries keep their actions and link to what they're about (folder, upload, task run — add a deep link to
  one run in the task history). Server events (task finished or failed while away) through the change feed,
  for the user who started them; administrators also get failed background tasks.
- [ ] **4.6 What I may do here: read-only or writable shown** (owner's request 2026-11-23, M; after 2.9). Show, for
  a folder and for a file, whether the user can only read it or can also change it, and **why**, from both sides:
  **SFE** (the space's rights, `Sfe:Rights:Disabled` and the space's **Files read-only** switch, folder rules;
  for connections the level, the connection's `ReadOnly` cap and its folder rules) and **the file system as it is
  now** (read at request time, never from the index: the read-only attribute on Windows, the mode bits and a
  read-only mount elsewhere, the server's account refused writing; providers that report a read-only folder, e.g.
  a Dropbox shared folder, by capability). Was: listings carry `FolderAccessView.Rights` (folder rules, `Children`
  for entries that differ) but nothing shows it; the disk's read-only flag is only a line in Properties
  (`ItemDetails.ReadOnly`); writes the disk refuses surface as an error after trying (text editor's 409
  `read-only`). Add:
  - **Listing:** a mark in the location bar for the open folder — **Read only** or nothing when writable (the usual
    case stays quiet) — whose hint gives the reason in one sentence ("Your rights in this folder let you open and
    download files only." / "The disk refuses changes here: it is mounted read-only."); a small lock on the tiles /
    rows whose access differs from the folder's (from `Children` and the attribute already read by the
    enumeration: no extra call per item).
  - **Properties:** an **Access** section: what the user may do with the item (open, download, change, rename,
    move to trash…) and, apart, what the disk allows, each with its source (right, folder rule and the folder it is
    set on, space switch, disk).
  - **Server:** one effective check of the **folder** per listing (`access(2)` `W_OK` outside Windows, a write
    test or `AccessCheck` on Windows; kept briefly per folder, no work per item, [scale](scale.md)); per item only
    the facts the enumeration already gives. Problem `type` words unchanged.
  - **Menus (2.9):** actions blocked by the disk are greyed with that reason too, as rights already are.
  Gotchas to settle in the note: on Windows a folder's read-only attribute means "customised folder" to Explorer,
  not "can't write" (don't show it as such); share permissions are only known by trying. Possibly split **a**
  library, **b** connections. Feature note (folder-access.md or a new `item-access.md`), user docs with a
  screenshot of the mark and of Properties.

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

- **Last session (2026-11-24):** 4.3 split in three; **4.3a** (upload sweep, chunk checksums) done. Next: **4.3b**
  (connection uploads kept across restarts). Step 4.6 (read-only or writable shown) was added by the owner on
  2026-11-23.
- 4.3a is committed (`bd57c98f`), its refactoring (`UploadRecords`, `UploadPlaces`) in the same commit: both touch
  `Uploads.cs`.
- `BackgroundService` (.NET 10) runs `ExecuteAsync` apart from `StartAsync`: a test of a worker waits for its effect.
- Client specs that drive `UploadQueue` call `instantDigests()` (`testing/fakes.ts`): Node's `crypto.subtle`
  answers after `settle()`.
- `FileJobs.cs` is at its baseline again after 4.1 (1,087 lines): the extraction part (`ExtractAsync`, `PlaceAsync`)
  or the compression (`CompressAsync`) is the next candidate to move out before adding job code.
- `measure.mjs` only measures files git tracks: write the baseline after `git add` of a new file, or its debt is
  missed (as `LibraryCopies.cs` was by the refactoring commit of 4.1).
- Don't run `npm test` and `dotnet test` at the same time: the client build rewrites `wwwroot` under the server's
  build ("No file exists for the asset").
- `npm test` from `/c/workspace/…` fails every suite with "Vitest failed to find the runner": always `cd
  C:/workspace/…` first.
- The docs-screenshots scene for `operations-panel.png` is `files` (it calls `operations()` at its end), not
  `operations`; it rewrites the other `files` images with no visible change: keep only those of the step.
  Texts marked and rewritten to
  [ui-texts](ui-texts.md), [translations](translations.md) says how; `app/access/` is a worked example.
- The docs-screenshots tool clicks elements by the start of their `title` (`clickTitle`): a hint rewritten updates
  `agents/tools/docs-screenshots/shots.mjs` too (grep it for the old words).
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
  - Scripted C# edits from `node -e` in Git Bash: write the script to a file with a quoted heredoc (`<<'EOF'`);
    `
` inside JS template strings becomes a real newline in the C# source otherwise.
  - Texts marked for translation: `node agents/tools/i18n/extract.mjs` after, `--check` before each commit (~20 s
    each); `SFE_TEXTS_BASELINE=write npm test -- --include src/app/core/i18n` when an area's unmarked texts drop.
  - Code generated through the Bash tool (`node -e`, heredocs) loses `\` line continuations and `\n` escapes (they
    become real breaks or vanish; a regex `\b` became an invisible backspace in a spec): write texts with the
    Write / Edit tools, and count lengths by characters
    (`[...line].length`; `awk length` counts bytes, and `“ ”` are 3).
  - Edit scripts (`rep(old, new)` over a file) are safest written to the scratchpad with the Write tool and run with
    `node <file>`: a long quoted heredoc failed in the Bash tool ("unexpected EOF while looking for matching `'`").
  - `npx prettier --write` on a whole folder (`src/app/files/*.ts`) reformats files the step didn't touch: name the
    files, or `git checkout` the others after.
  - `/tmp` in a Bash command is not `/tmp` for `node -e` (it reads `C:	mp`): pass such paths as arguments, or use
    `$TEMP`.
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
dashboard fixed columns; home folder never auto-created; menus (2.9): greyed only for the rights of file actions
(Scans and previews…, quick links, Copy full path hidden instead), the selection bar never greyed; scan-as-you-go not recursive, 10 minutes; storage bar
thresholds 85/95 %; sync jobs for administrators only.

## What the check found already done (don't redo)

Resumable uploads (folders, pause, admin list, Sfe.Client resume); trash for deletes, replaces and saved-over
versions with restore and purge; clash choices with "for all" and keep-both names; live refresh for SFE's own
changes; Local/External modes enforced server-side; admin tabs, per-page URLs and search; provider abstraction with
capabilities (Dropbox on it); SemVer, `CHANGELOG.md`, Sfe.Client packed; user docs with screenshots; toast Undo.
