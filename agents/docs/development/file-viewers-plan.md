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
- **Bundled fonts** (1.65.0): Literata (serif), Atkinson Hyperlegible (sans-serif), JetBrains Mono (monospace), all
  OFL; regular, italic (not the mono) and bold. Prose defaults to Literata, code to the device's monospace.
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
- `formats/reading/` (1.65.0): `reading.ts`, `reading.service.ts`, the "Aa" panel; `formats/view-page.component.ts`
  (lazy route `view?path=`, like `player`). `formats/markdown/` (1.66.0): `markdown.ts`, `markdown-view.component.ts`.
- CSS: `styles/reader.css` (global, as the frontend rules want): `--reader-*` and `--tok-*` variables, layout, `tok-*`
  classes.

### Server

- `Core/Files/TextCodec` (pure): `Decode(bytes)` → text, encoding (`utf-8`, `utf-8-bom`, `utf-16le/be`,
  `windows-1252`), line ending (`lf`/`crlf`/`cr`/`mixed`), or null for binary (a NUL in the first 8 KB without a BOM).
  `Encode(text, encoding, lineEnding)` (1.67.0) → bytes, or what windows-1252 can't hold; `DominantLineEnding`.
- `Core/Files/FileContent`: the generic reader/writer for every editor, binary included. `ReadAsync(full, maxBytes)`
  → bytes + `FileVersion`. `OverwriteAsync(relative, bytes, ifMatch, session, keepPrevious, actor)` (1.67.0) —
  resolve, refuse links and read-only files; one lock; `scans.TryBeginOperation`; compare versions; trash copy once
  per session unless permanent; temp file `.{name}.{guid}.sfe-tmp` in the folder (`CreateNew`, flushed, Unix mode);
  `File.Replace` with in-use retries (on failure: delete the temp, purge that call's trash entry);
  `indexer.RefreshFileAsync`, `browser.Invalidate`, log with the actor. Answers an `OverwriteStatus` of its own. A new
  file with content is `FileOperations.CreateFileAsync(..., content)` (`CreateNew`, `Exists` on a clash).
- `Endpoints/TextEndpoints`: `GET api/files/text?path=` (`files.view`): `{ text, encoding, lineEnding, version, size,
  modified }`, ETag, `no-store`; 413 `too-large` (10 MB), 415 `not-text`; hidden or protected → 404, no right → 403.
  1.67.0: `access: { overwrite, saveCopy, download, permanent }` and `saveLineEnding` (from the server: `store.can`
  is optimistic in searches, the `view` page has no listing); `PUT` (`If-Match`, `*` = anyway; 412 `file-changed`,
  428, 422 `not-encodable`, 409 `scan-running` / `file-is-link` / `read-only`); `POST` `{ path, name, text, encoding,
  lineEnding }` (a copy; 409 `file-exists`). Details: [text-editor.md](../features/text-editor.md).

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

### Phase 2 — Reading comfort (built in 1.65.0)

- [x] (1.65.0) npm `@fontsource/literata`, `@fontsource/atkinson-hyperlegible`, `@fontsource/jetbrains-mono` (OFL);
      `src/reader-fonts.css` as an `inject: false` bundle (`reader-fonts.css`, not hashed: linked with `?v=<version>`),
      linked only when a profile shown uses a bundled font.
- [x] (1.65.0) `formats/reading/`: `reading.ts` (model, allowed values, `sanitizeReading`), `reading.service.ts`
      (defaults signal + this browser's overrides in `sfe.reading`, versioned; ready for account defaults in phase 5).
- [x] (1.65.0) Profiles: `PROSE_FORMAT` (priority 11: `.txt`, `.md`, `README`…) beside `TEXT_FORMAT` (code), same view.
- [x] (1.65.0) "Aa" panel: font (bundled, the device's serif / sans / mono, a custom local name), size, line height,
      width, margins, wrap, line numbers, colours (as the app / light / sepia / dark), Reset.
- [x] (1.65.0) Ctrl+= / Ctrl+− / Ctrl+0 inside the view.
- [x] (1.65.0) `view?path=` page (lazy); `Urls.view`; listing links (modified clicks) and the viewer's "Open in new
      tab" use it for formats with `page`. Its `canDeactivate` comes with editing (phase 4): nothing to lose before.
- [x] (1.65.0) Phones: the panel as a sheet at the bottom.
- [x] (1.65.0) Tests: `reading.service.spec.ts` (model, service, panel), `text-view.component.spec.ts`,
      `view-page.component.spec.ts`, `config.service.spec.ts` (`view` link).
- [x] (1.65.0) Feature note [reading-preferences.md](../features/reading-preferences.md); user docs with screenshots.

### Phase 3 — Markdown (built in 1.66.0)

- [x] (1.66.0) npm `markdown-it` (15, own types), `dompurify`.
- [x] (1.66.0) `formats/markdown/markdown.ts`: `html: false`, `fuzzyLink` off, DOMPurify fragment inserted with
      `replaceChildren`; library images through `urls.file`, other sites' images held back ("Show", no referrer);
      links: documents → viewer (`data-path`, `FILE_VIEW_HOST.open`) or `view?path=`, other files → raw, folders →
      listing, sites → new tab `noopener noreferrer nofollow`, other schemes and paths out of the library → no link;
      heading ids `md-<slug>`; fences highlighted with `highlightCode` + `classHighlighter` (`languageForName`).
- [x] (1.66.0) `markdown-view.component.ts`: Read / Source / Split (kept per browser), outline (h1–h3), prose
      profile and "Aa"; shared helpers `text-document.ts` with the text view.
- [x] (1.66.0) `MARKDOWN_FORMAT` (priority 20; `extensionOf` shared by the text formats); `FileViewHost.open`.
- [x] (1.66.0) Tests: `markdown.spec.ts` (raw HTML, `on*` attributes, `javascript:`/`data:`/`file:` links, paths
      out of the library, relative paths, file names not linked, external images, dotless hosts, headings, fences),
      `markdown-view.component.spec.ts`, `formats.spec.ts`.
- [x] (1.66.0) Feature note [markdown-viewer.md](../features/markdown-viewer.md); user docs with screenshots.

### Phase 4 — Editing and saving (built in 1.67.0)

Server
- [x] (1.67.0) `TextCodec.Encode` (reports what windows-1252 can't hold) and `DominantLineEnding` (a mixed file
      is saved with its most used ending).
- [x] (1.67.0) `FileContent.OverwriteAsync` in the order under [Architecture](#server), with an `OverwriteStatus` of
      its own rather than `FileOperationStatus.Changed`; a copy is `FileOperations.CreateFileAsync` with content
      (no `FileContent.CreateAsync`: the same checks as New file).
- [x] (1.67.0) `Trash.CopyInAsync`; nullable `TrashEntry.Kind` (`PreviousVersion`) + SQL Server migration
      `AddTrashEntryKind` (LF); the purge keeps the live file's caches for those entries.
- [x] (1.67.0) `DirectoryIndexer.RefreshFileAsync`.
- [x] (1.67.0) `access` and `saveLineEnding` in the GET answer; `PUT api/files/text` (412 `file-changed`, 428, 422
      `not-encodable`, 409 `scan-running` / `file-is-link` / `read-only`), `POST api/files/text` (409 `file-exists`);
      Replace's rule for overwrite, `files.createFiles` for a copy.

Client
- [x] (1.67.0) `formats/text/text-session.ts` (one per view) + `text-edit.component.ts` (buttons, bars), in the text
      and Markdown views: Edit (E / button); **Save** (Ctrl+S) when `access.overwrite`, **Save a copy** (name bar,
      Ctrl+Shift+S), **Download** the text as it is; Done gives up changes (asks).
- [x] (1.67.0) The 412 bar: Reload / Overwrite (`If-Match: *`) / Save a copy; 422: Save as UTF-8; 409 answers keep
      the edits.
- [x] (1.67.0) Unsaved-changes guards: `FileViewHost.guard`; the viewer's `close`, `cancel`, `go`, `open` of
      another file, `searchTag`; `Dialogs.closeAll` (false when refused); the listing route's `leaveViewer`
      (`runGuardsAndResolvers: 'paramsOrQueryParamsChange'`); the `view` page's `leaveViewPage`; `beforeunload`.
- [-] An in-memory draft per path: dropped. Every way out of a file with changes asks first, and a draft kept in
      memory dies with the tab, which is the one way out that already asks (`beforeunload`); restoring a draft over
      a file changed since would need its own conflict handling.
- [x] (1.67.0) **New file** opens in the viewer, ready to be written (text formats only); the trash page shows
      "previous version".
- [x] (1.67.0) Markdown: editing shows the source beside the document (Split, not remembered), which follows the
      source as typed (300 ms after the last change).

Tests
- [x] (1.67.0) `FileContentTests`: versions and `*`; one trash copy per session, the restore as "(2)"; index row,
      tags and Sha1 kept / refreshed; the purge keeps the live previews; read-only, links (inconclusive without the
      symlink privilege), the scan gate; a file in use left as it was with no temp file; a copy never over another;
      the SQLite column added to an existing trash.
- [x] (1.67.0) `TextCodecTests`: encode round trips per encoding, line endings, the 1252 refusal.
- [x] (1.67.0) `TextRouteTests`: the access matrix (read only → copy only; delete + create → overwrite; a permanent
      space), 428, 412, `*`, 403s, a bad session, 422, copies (409, 403, the work folder).
- [x] (1.67.0) `text-editing.spec.ts`; viewer guard specs; `Dialogs.closeAll`; New file; trash page.
- [x] (1.67.0) Feature note [text-editor.md](../features/text-editor.md); user doc
      [editing-text.md](../../../docs/user-guide/editing-text.md); rights and upgrading pages.

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
- (1.65.0) Phase 2 added +2.9 kB JS and +3.8 kB CSS to the initial bundle (735 kB): the themes, panel and page styles
  are global CSS, as the frontend rules want (about 0.6 kB over the wire).
- (1.65.0) A non-injected style bundle keeps its plain name (`reader-fonts.css`) while its fonts are hashed in
  `media/`: link it with the app's version as a query, or a browser may keep an old copy.
- (1.65.0) CodeMirror doesn't notice a change of CSS variables (font, size, padding): `requestMeasure` after one.
- (1.65.0) Width in `ch` with padding: `max-width: calc(var(--reader-width) + 2 * var(--reader-padding))`; `none`
  makes the calc invalid, which means no maximum: no special case needed.
- (1.66.0) `WebClientHasNoExternalResources` reads the specs too: test URLs use dotless hosts (`https://tracker/…`),
  and a regex like `/\/\//` reads as an address (`\/{2}` doesn't).
- (1.66.0) A width in `ch` on each block of a document counts in that block's characters (headings wider): put it
  on one inner element (`.markdown-content`).
- (1.66.0) Phase 3 added +4.6 kB CSS to the initial bundle (740 kB); markdown-it + DOMPurify are one lazy chunk
  (~144 kB, ~51 kB over the wire).
- (1.67.0) Phase 4 added +3.2 kB to the initial bundle (743 kB): the editing CSS and the viewer's guards; the
  session and its bars are in the lazy text chunk.
- (1.67.0) jsdom has no `Range.getClientRects`: CodeMirror calls it a frame after a change, so `setup.ts` stubs it.
- (1.67.0) `window.confirm` answers every guard: once the person agrees to lose the changes the session remembers it,
  so the route's question after the viewer's doesn't ask twice; a new change clears it.
- (1.67.0) A read-only CodeMirror still has `contenteditable`: E to edit can't skip "typing" targets by that
  attribute, only inputs.
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
