# Viewing and editing files: plan and progress (epic)

Code: client `src/Sfe.Web/src/app/formats/` (registry `formats.ts`, one folder per format), the viewer
`library/viewer/viewer.component.ts`, `styles/reader.css`; server `src/Sfe.Core/Files/` (`TextCodec`, `FileContent`),
`src/Sfe.Api/Endpoints/TextEndpoints.cs`. Rules for writing library files: [file-operations.md](file-operations.md);
security: [security.md](security.md). What is built: [text-viewer.md](../features/text-viewer.md).

Goal: view and edit files in the browser, starting with text (`.txt`, `.log`, `.md`, `.cs`, `.js`…) with comfortable
reading settings (font, size, line height, width, padding, wrap, theme). **Formats are added one by one**: each is a
small, local addition (one registry entry, one lazily loaded component, at most a thin route over a shared
reader/writer).

## How to use this file

- Built over several sessions: **update it in the commit that does the work** (tick `[x]` with the version, move
  findings into the phase), so the next session starts here: take the first phase that isn't done. One phase = one
  session = one commit = one minor version.
- Gotchas found while building go in [Found while building](#found-while-building); questions are decided at the start
  of the phase that needs them and move to [Decisions](#decisions).
- When a phase is done, its lasting facts go into a feature note (`agents/docs/features/<topic>.md`, indexed in
  `AGENTS.md`); this file keeps the checklist.

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## Decisions

- **Editor: CodeMirror 6** (MIT, modular ESM), also the read-only viewer. Compared (2026-10-18):
  - [Monaco](https://microsoft.github.io/monaco-editor/): 2–5 MB, module workers and a codicon font to bundle with
    esbuild, poor on phones, awkward under Vitest.
  - [Ace](https://ace.c9.io/): older, not ESM, loads modes from a base path.
  - [CodeMirror 6](https://codemirror.net/): ~150–350 kB lazy, touch friendly, no workers, works under the app's CSP
    (`style-src 'unsafe-inline'` covers its injected styles); languages on demand (`@codemirror/language-data`).
  - filebrowser (`C:\workspace\3dparty\filebrowser`) uses Ace; inspiration only.
- **Markdown: [markdown-it](https://github.com/markdown-it/markdown-it)** (`html: false`) **+
  [DOMPurify](https://github.com/cure53/DOMPurify)**; code fences highlighted with `@lezer/highlight`
  `highlightCode` and CodeMirror's parsers: one highlighter, one set of `tok-*` colours.
- **No `ItemKind.Text`** on the server: it would change `type:other` in search and every switch on kinds. Formats
  match client side, on kind `file` only (`.ts` is a video by default), by name. Any other file: **Open as text**; the
  server refuses binary content (415 `not-text`).
- **Saving follows the user's rights, like Notepad** (phase 4): **Save** overwrites when the user may delete the file
  and create files in its folder (move/copy's **Replace** rule: `files.delete` at the file and its parent +
  `files.createFiles` at the parent; `files.deletePermanently` instead when the space has `Files:DeletePermanently`);
  otherwise **Save a copy** (`files.createFiles` where it goes) or **Download** the edited text (`files.download`). An
  overwrite copies the previous version into SFE's trash once per editing session; the live file keeps its tags.
- **Reading preferences: account defaults + per-browser overrides**, as the theme. Two profiles: *code* (monospace,
  no wrap, line numbers) and *prose* (txt/md: wrapped, width-limited).
- **The viewer's previous / next steps through all viewable files** (media and documents), in listing order.
- **Go to line is Ctrl+Alt+G** (1.64.0): Ctrl+G stays "find next", as in CodeMirror and browsers.

## Verified constraints

- **409:** `Api.send` resolves every 409 with its body (`core/api.service.ts`): a version conflict must be **412
  `file-changed`** (`If-Match`), a missing header 428; a name clash on Save a copy stays 409 `file-exists`.
- **The viewer would fight the editor:** its `keydown` takes arrows and `z/w/t/e`, `onWheel` always prevented the
  default, a horizontal swipe navigated. Fixed in 1.64.0: a view's root has `data-owns-keys`, the viewer leaves keys,
  wheel and swipes inside it alone, and Esc in it moves the focus back to the dialog first.
- **The viewer indexed `store.media()[index]`:** a refresh in place could swap the file under a dirty editor. Fixed in
  1.64.0: it tracks the shown file by path (`opened`), the index is derived.
- **Unsaved-changes guards** (phase 4): the viewer's `close()`, the dialog's `cancel`, `go()`, `searchTag`;
  `Dialogs.closeAll()` (bypasses every guard); a `canDeactivate` on the listing route (`runGuardsAndResolvers:
  'paramsOrQueryParamsChange'`) and on `view`; `beforeunload`.
- **The index is not refreshed on overwrite:** `DirectoryIndexer.IndexFilesAsync` skips indexed files; add
  `RefreshFileAsync` (Size, times, `Sha1` of the first MB when `Scan:ComputeHashes`). Duplicates group by `(Size, Sha1)`.
- **Trash:** `Trash.MoveInAsync` strips tags (add `CopyInAsync`); the purge deletes the original path's caches, which
  would remove the live file's previews (previous-version entries must skip that); restore onto a taken name gives
  `name (2).ext` (fine).
- **Overwriting on disk:** refuse links and junctions; copy the Unix mode before `File.Replace`; a Windows ReadOnly
  attribute means no overwrite; retry while in use (`FileOperations` `InUseAttempts`).
- **Version token** `size-ticks-sha256/16` of the bytes read (`FileVersion`), not the time alone (FAT keeps 2 s, copy
  tools and SMB keep times).
- **Encoding:** windows-1252 through `CodePagesEncodingProvider` (portable builds aren't trimmed), not Latin-1 (they
  differ at 0x80–0x9F).
- **Markdown and the CSP:** the app CSP allows `img-src http: https:` (data server), so external images in Markdown
  are blocked by default with a "show" button; relative images through `api/files/raw/…`; relative `.md` links open in
  the viewer; external links `noopener noreferrer`.
- **Build:** the initial bundle is over its 600 kB warning: everything new is lazy (`import()` chunks), fonts in a
  non-injected style bundle. `ApiTests.WebClientHasNoExternalResources` scans every chunk (CodeMirror passes).
- **Reading settings** (phase 5): prefix `reading.` → `Rights.InterfaceSettings` in `SettingCatalog.RightsByPrefix`
  (no new right; users edit their own through `account.preferences`); line height and width as `select` (number
  fields have no step); new keys nullable or with defaults; SQLite adds only nullable new columns by itself.

## Architecture

### Client: `src/Sfe.Web/src/app/formats/`

- `formats.ts` (initial bundle: keep it small): `FileFormat { id, label, priority, matches(item), view: { builtin:
  'image'|'video'|'audio' } | { load: () => import(...) }, profile? }`, `FORMATS`, `formatOf(item, { asText })`,
  `isViewable`, `TEXT_EXTENSIONS` / `TEXT_NAMES`. Media are `builtin`: the viewer keeps its own markup for them.
- `FILE_VIEW_HOST`: what a format component may ask of its host (`place`, `can(right)`; phase 4 adds
  `guard(canLeave)`, `saved(item)`, `added(item)`). The viewer (and later the `view` page) provide it: components never
  touch the listing.
- View components: inputs `item`, `format`; root `data-owns-keys`; their own shortcuts.
- `ListingStore.viewable` (was `media`): the files with a format, used by `ItemActions.openFile`, `show`/`reveal` and
  the viewer's steps. The viewer shows components with `NgComponentOutlet` and an injector holding the host.
- `formats/text/`: `codemirror.ts` (adapter: compartments for wrap, line numbers, language; `readerTheme` of CSS
  variables; `classHighlighter`; search; go to line), `languages.ts` (`language-data` + log and subtitle modes +
  aliases), `text-view.component.ts`.
- Later: `formats/markdown/`, `formats/reading/` (`reading.service.ts`, the "Aa" panel), `formats/view-page.component.ts`
  (lazy route `view?path=`, like `player`).
- CSS: `styles/reader.css` (global, as the frontend rules want): `--reader-*` and `--tok-*` variables, layout, `tok-*`
  classes.

### Server

- `Core/Files/TextCodec` (pure): `Decode(bytes)` → text, encoding (`utf-8`, `utf-8-bom`, `utf-16le/be`,
  `windows-1252`), line ending (`lf`/`crlf`/`cr`/`mixed`), or null for binary (a NUL in the first 8 KB without a BOM).
  Phase 4: `Encode(text, encoding, lineEnding)` reporting what windows-1252 can't hold.
- `Core/Files/FileContent`: the generic reader/writer for every editor, binary included. `ReadAsync(full, maxBytes)`
  → bytes + `FileVersion`. Phase 4: `OverwriteAsync(path, write, ifMatch, session, actor)` — resolve and refuse
  links; per-path lock; `scans.TryBeginOperation`; compare versions (`FileOperationStatus.Changed`, added last); trash
  copy once per session unless permanent; temp file `.{name}.{guid}.sfe-tmp` in the folder (`CreateNew`, flushed, Unix
  mode); `File.Replace` with in-use retries (on failure: delete the temp, forget that call's trash entry);
  `indexer.RefreshFileAsync`, `browser.Invalidate`, log with the actor. `CreateAsync(folder, name, write, actor)`
  (`CreateNew`, `Exists` on a clash).
- `Endpoints/TextEndpoints`: `GET api/files/text?path=` (`files.view`): `{ text, encoding, lineEnding, version, size,
  modified }`, ETag, `no-store`; 413 `too-large` (10 MB), 415 `not-text`; hidden or protected → 404, no right → 403.
  Phase 4: `access: { overwrite, saveCopy, download, permanent }` (from the server: `store.can` is optimistic in
  searches, the `view` page has no listing), `PUT` (`If-Match`, `*` = anyway; 412/428/409 `scan-running`,
  `file-is-link`), `POST` (a copy; 409 `file-exists`).

### Adding a format

1. A `FileFormat` entry in `formats.ts` (match, priority, profile, `load: () => import(...)`).
2. `formats/<id>/<id>-view.component.ts`: inputs `item` and `format`, `data-owns-keys` on its root, inject
   `FILE_VIEW_HOST`; a guard when it edits.
3. Read through `api/files/raw` or a format route; to write, a thin route checking the bytes and calling
   `FileContent.OverwriteAsync` / `CreateAsync` (rights rules, versions, trash copy, index refresh and change feed
   come with it).
4. Tests: a row in `formats.spec.ts`, a component spec, the route's rights matrix (the `AccessTests` route checks run
   by themselves).
5. The initial bundle doesn't grow beyond the entry, `WebClientHasNoExternalResources` passes; feature note, user
   docs, this file ticked.

## Phases

**Every phase ends with:** `dotnet test` and `npm test` pass; the initial bundle no larger than the registry entry,
`WebClientHasNoExternalResources` passes; version bump + `CHANGELOG.md`; a feature note with its `AGENTS.md` line; user
docs in `docs/` with screenshots, `check-links.mjs` passes; this file ticked; a commit `claude: …` (when asked).

### Phase 1 — Foundations and a plain text viewer (built in 1.64.0)

- [x] (1.64.0) This file; its `AGENTS.md` line; `file-operations.md` brought up to date.
- [x] (1.64.0) npm `@codemirror/{state,view,language,commands,search,language-data,legacy-modes}`, `@lezer/highlight`.
- [x] (1.64.0) `Sfe.Api.csproj`: `npm ci` also runs when `package-lock.json` is newer than
      `node_modules\.package-lock.json` (MSBuild `Inputs`/`Outputs`), so a pull adding packages installs them.
- [x] (1.64.0) `TextCodec.Decode`, `FileContent.ReadAsync` + `FileVersion`, `GET api/files/text`.
- [x] (1.64.0) `formats.ts`, `FILE_VIEW_HOST`, `ListingStore.viewable`; `ItemActions.openFile`, `show`, `reveal`.
- [x] (1.64.0) Viewer: the shown file by path, `@case ('audio')` + a component case (`NgComponentOutlet`),
      `data-owns-keys` (keys, wheel, swipes; Esc leaves the view first), steps over files without a viewer.
- [x] (1.64.0) Text view, read only: highlighting, line numbers, wrap, find (Ctrl+F, also from outside the text), go
      to line (Ctrl+Alt+G), status bar (language, encoding, line ending, size, cursor); too large / not text with
      Download; **Open as text** in the item menu; `styles/reader.css`.
- [x] (1.64.0) Tests: `TextCodecTests`, `TextRouteTests`, `formats.spec.ts`, `text-view.component.spec.ts`, viewer
      documents specs, item menu spec.
- [x] (1.64.0) Feature note [text-viewer.md](../features/text-viewer.md); user doc
      [text-files.md](../../../docs/user-guide/text-files.md).

### Phase 2 — Reading comfort (1.65.0)

- [ ] npm `@fontsource/*` (OFL, woff2): a reading serif, a legible sans, a code mono; `reader-fonts.css` as an
      `inject: false` bundle in `angular.json`, linked only when a bundled font is picked.
- [ ] `formats/reading/reading.service.ts`: profiles *code* and *prose*; per browser in `sfe.reading` (versioned JSON,
      `core/preferences.ts`); built to take account defaults later (phase 5). Replaces the text view's module-level
      `wrapLines` / `showLineNumbers` signals.
- [ ] `reading-panel.component.ts` ("Aa" popover): font (bundled, system stacks, a custom local name), size, line
      height, content width, padding; wrap, line numbers, reader theme (follow app / light / sepia / dark); reset. The
      `--reader-*` variables of `reader.css` are where they apply.
- [ ] Ctrl+= / Ctrl+- / Ctrl+0 for the font size, inside the view.
- [ ] `formats/view-page.component.ts`: lazy route `view?path=` with `canDeactivate`; listing links and the viewer's
      "Open in new tab" point there for formats with a page (`page: true` in `FileFormat`).
- [ ] Phone width: the panel as a bottom sheet.
- [ ] Tests: `reading.service.spec.ts`, `reading-panel.spec.ts`, `view-page.spec.ts`.
- [ ] Feature note `reading-preferences.md`; user docs with screenshots.

### Phase 3 — Markdown (1.66.0)

- [ ] npm `markdown-it`, `dompurify`.
- [ ] `formats/markdown/markdown.ts`: render with `html: false`, then sanitise (`RETURN_DOM_FRAGMENT`, inserted with
      `replaceChildren`); relative images → `api/files/raw/…`, external images blocked with a "show" button; relative
      `.md` links open in the viewer, external links `noopener noreferrer`; fences highlighted (`highlightCode`,
      `classHighlighter`).
- [ ] `markdown-view.component.ts`: Rendered / Source / Split, an outline from the headings, the *prose* profile.
- [ ] A `markdown` entry in `formats.ts` (priority above `text`).
- [ ] Tests: `markdown.spec.ts` (raw HTML, `javascript:` / `data:` links, SVG, relative paths, external images,
      dotless host names).
- [ ] Feature note `markdown-viewer.md`; user docs.

### Phase 4 — Editing and saving (1.67.0)

Server
- [ ] `TextCodec.Encode` (reports what windows-1252 can't hold).
- [ ] `FileContent.OverwriteAsync` and `CreateAsync`, in the order under [Architecture](#server);
      `FileOperationStatus.Changed` added last.
- [ ] `Trash.CopyInAsync`; nullable `TrashEntry.Kind` (`PreviousVersion`) + SQL Server migration `AddTrashEntryKind`
      (LF); the purge keeps the live file's caches for those entries.
- [ ] `DirectoryIndexer.RefreshFileAsync`.
- [ ] `access` in the GET answer; `PUT api/files/text` (412 `file-changed`, 428, 409 `scan-running` /
      `file-is-link`), `POST api/files/text` (409 `file-exists`); Replace's rule for overwrite, `files.createFiles` for
      a copy.

Client
- [ ] Edit mode (E / button); **Save** (Ctrl+S) when `access.overwrite`, **Save a copy** (name prompt), **Download**.
- [ ] The 412 prompt: Reload / Overwrite (`If-Match: *`) / Save a copy; 409 answers keep the edits.
- [ ] Unsaved-changes guards (see [Verified constraints](#verified-constraints)); an in-memory draft per path.
- [ ] **New file** opens in the editor; the trash page shows "previous version".

Tests
- [ ] `FileContentTests`: round trips of encodings and line endings, the 1252 refusal; version mismatch and `*`; link
      refused; scan gate; one trash copy per session, none in a permanent space, tags kept; Size / LWT / Sha1
      refreshed; a locked file on Windows; no temp file left after a failure.
- [ ] `TrashTests`: a restored version comes back as "(2)"; the purge keeps the live caches.
- [ ] `TextRouteTests`: read only → copy only; delete + create → overwrite; a permanent space; the space's read-only
      switch; 412, 428, 409.
- [ ] SQLite upgrade test for the new column, the migration on SQL Server.
- [ ] `text-editor.spec.ts`, the guard specs.
- [ ] Feature note `text-editor.md`; user doc `docs/user-guide/editing-files.md`; rights and upgrading pages.

### Phase 5 — Reading preferences per account (1.68.0)

- [ ] Server: `SfeOptions.Reading` (about 13 keys: `reading.theme`, `reading.customFont`, and per profile `font`,
      `size`, `lineHeight`, `width`, `padding`, `wrap`, `lineNumbers`); `EditableSettings`; `SettingCatalog`
      (UserKeys, `reading.` → `InterfaceSettings`); `SettingsService.Validate`; `ClientConfig.Reading` (optional, last).
- [ ] Client: a **Reading** group in `settings-schema.ts` (key, label and type first, on one line); `ReadingService`
      merging account defaults and the browser's override; **Save as my default** in the "Aa" panel.
- [ ] Tests: `SettingsTests`, `EverySettingOfTheClientsSettingsDialogExists`, `reading.service.spec.ts`.
- [ ] `docs/configuration.md`, the preferences user doc.

### Phase 6 (optional) — Comfort (1.69.0)

- [ ] `fileTypes.text`: a library setting for more text extensions.
- [ ] Large files and logs: paged reading by byte offset, jump to the end, follow / tail.
- [ ] Reopen with another encoding; choose the encoding and line ending on save.
- [ ] "Changed on disk, reload?" from the change feed.
- [ ] Remembered scroll position per file.

### Later formats (each follows [Adding a format](#adding-a-format); one session each)

- [ ] JSON tree view.
- [ ] CSV / TSV table.
- [ ] Subtitles (`.srt`, `.vtt`).
- [ ] PDF (pdf.js, bundled).
- [ ] EPUB.
- [ ] The picture editor saving in place through `FileContent`.

## Open questions

- Phase 2: which three fonts to bundle (licence OFL, woff2 size), and whether the "Aa" panel also shows in the
  picture viewer (no: reading only).
- Phase 4: does the editor get a right of its own, or only the Replace rule (decided: the Replace rule, like Notepad;
  revisit if spaces ask for "may edit but not delete")?

## Found while building

- (1.64.0) The real initial bundle before this epic was 722 kB (the 631 kB measured earlier was stale); phase 1 added
  +3.1 kB JS (registry, viewer) and +3.6 kB CSS (`reader.css`); CodeMirror is one lazy chunk (~364 kB, ~101 kB over the
  wire) plus one chunk per language.
- (1.64.0) CodeMirror's own theme outranks global CSS (generated, more specific selectors): colours go through
  `EditorView.theme` with CSS variables (`readerTheme` in `codemirror.ts`), defined in `reader.css`.
- (1.64.0) CodeMirror runs in jsdom well enough for specs (content, gutters, search panel, wrapping): the specs use the
  real one, no mock.
- (1.64.0) The test fake `item()` gives extensions with a dot (`.txt`), the server without (`txt`): `isTextFile` reads
  the name, not `extension`.
- (1.64.0) `.gitignore`'s `[Ll]ogs/` had kept the log feature's source folders (`Core/Logs`, `Data/Entities/Logs`,
  client `app/logs`) out of git: `!src/**/[Ll]ogs/` lets them in.
- (1.64.0) npm 11 skips install scripts it hasn't been told to run (esbuild, lmdb, msgpackr-extract warn): the build
  works without them.

## Security checklist (every phase)

- A library file is content of strangers: never run it on the app's origin. Text goes into CodeMirror as text;
  Markdown is rendered without HTML and sanitised; nothing uses `innerHTML`.
- Every route names its right, is `FolderScoped`, refuses server files and the work folder (`FileEndpoints.Protected`),
  answers 404 for what the caller doesn't see, and never puts a server path in an error.
- Writing routes (phase 4) follow [file-operations.md](file-operations.md): links refused, the Replace rule, versions,
  the scan gate, logged with the account.
- No external URL in the client: `WebClientHasNoExternalResources`.
