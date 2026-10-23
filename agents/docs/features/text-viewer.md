# Text viewer (and the format registry)

Code: client `app/formats/formats.ts` (registry), `app/formats/text/` (`text-view.component.ts`, `codemirror.ts`,
`languages.ts`), `library/viewer/viewer.component.ts`, `ListingStore.viewable`, the `openAsText` entry of
`files/item-menu.ts`, `styles/reader.css`; server `Core/Files/TextCodec.cs`, `Core/Files/FileContent.cs`,
`Api/Endpoints/TextEndpoints.cs`. Tests: `TextCodecTests`, `TextRouteTests`, `formats.spec.ts`,
`text-view.component.spec.ts`, `viewer.component.spec.ts` (Documents), `files.spec.ts` (Open as text). User docs:
`docs/user-guide/text-files.md`.

## Formats

- `formatOf(item, { asText })` picks the matching `FileFormat` of highest priority; `null` = no viewer (the link opens
  the file in a tab). Media formats are `builtin` (the viewer's own markup); others `load()` a component, lazily.
- Text matches **kind `file` only** (never what the server classified as media: `.ts` stays a video) by name:
  `TEXT_EXTENSIONS` (lower case, no dot) or a whole name in `TEXT_NAMES` (`README`, `Dockerfile`, `.gitignore`…).
  `isTextFile` reads the name, not `item.extension`.
- `ListingStore.viewable` = the loaded items with a format: what a click opens in the viewer, what `reveal` opens.

## Viewer

- Tracks the shown file by path (`opened`; `item` is the fresh listing copy, else the opened one): a refresh in place
  keeps it. `index` (for `n / m`) is derived; −1 for a file opened as text (no position shown).
- ‹ / › (`neighbours`) step to the nearest viewable item in listing order, loading more pages past the last one.
- A format with a component: `NgComponentOutlet` with inputs `item`, `format` and an injector holding
  `FILE_VIEW_HOST` (`place`, `can(right)`, `open(item)` for links between documents); the stage gets `.document`
  (fills the stage).
- `[data-owns-keys]` on a view's root: the viewer ignores keys, wheel and swipes from inside it; Esc inside it (unless
  the view used it, e.g. to close its find bar) focuses the dialog (`tabindex="-1"`), the next Esc closes.
- The bar holds only Tags, ⋮ and ×: the rest (zoom, crop, quick link, copy path, new window, external player, new tab,
  download) is `menuEntries` in the viewer's own ⋮ menu (`.item-menu.viewer-menu`, inside the modal dialog: the page's
  item menu would sit under its top layer). Esc with it open closes only the menu.

## Text view

- `GET api/files/text?path=` (`files.view`, `FolderScoped`; hidden, missing, folders, server files, work folder → 404;
  no right → 403): whole file up to 10 MB (`MaxBytes`; 413 `too-large`), `TextCodec.Decode` (BOM → UTF-8/16; else
  strict UTF-8, else windows-1252; a NUL in the first 8 KB without a BOM → 415 `not-text`); ETag = `FileVersion.Token`
  (`size-ticks-sha256/16`), `no-store`. Read with `FileShare.ReadWrite | Delete` (logs being written).
- CodeMirror 6, read only until **Edit** (`EditorState.readOnly`: cursor and selection work; editing and saving:
  [text-editor.md](text-editor.md)). Compartments: wrap, line numbers,
  language. Language: `languageFor(name)` — aliases (MSBuild files → XML, `.conf`/`.cfg`/`.env`… → Properties
  files), then our `Log` and `Subtitles` stream modes, then `@codemirror/language-data` (each language its own chunk).
- Colours: `readerTheme` (`EditorView.theme`) uses `--reader-*` variables; highlighting uses `classHighlighter`
  (`tok-*` classes) coloured by `--tok-*` in `reader.css`; both follow the app's light/dark theme.
- Font, size, spacing, wrap, line numbers and colours are the reading preferences of the format's profile (code or
  prose): [reading-preferences.md](reading-preferences.md), also for the page of its own (`view?path=`).
- Keys: CodeMirror's `searchKeymap` + `defaultKeymap` (Ctrl+F, F3/Ctrl+G, Ctrl+Alt+G go to line); the component also
  takes Ctrl+F and Ctrl+Alt+G from the window, so they work when the focus is on the viewer rather than in the text.
- Failures: 413/415/500 offer **Download** when `files.download`; 404/403 say so.

## Large files, Follow, encodings, positions (1.69.0)

- `GET api/files/text?encoding=` (`utf-8`, `windows-1252`, `utf-16le`, `utf-16be`): `TextCodec.Decode(bytes, encoding)`
  (mark skipped, invalid bytes replaced; a UTF-8 mark reported as `utf-8-bom` so a save keeps it). The status bar's
  encoding is a `select` (`data-read-encoding`) calling `TextSession.reopen`.
- `GET api/files/text/part?path=&offset=&length=&encoding=` (`files.view`): `FileContent.ReadPartAsync` → `TextPart`
  (`text, offset, end, size, encoding, modified`); offset −1 = the last `length` bytes; length 1 B–1 MB (default 256 KB).
  `TextParts.Cut`: after the first line break unless the part starts a line (the byte before is one) and up to after
  the last unless at the end; without a break, whole UTF-8 characters only; UTF-16 by 2-byte units at even offsets.
  Encoding: the given one, the file's mark, else UTF-8 when the part is valid UTF-8, else windows-1252. A NUL in the
  first 8 KB without a UTF-16 mark → 415.
- Text view: a 413 from the whole read opens **parts** (`part` signal: offset, end, size, encoding; `.log` from the
  end): Start / Earlier (`offset − 256 KB` up to the shown offset) / Later (`end`) / End; read only, no
  `TextSession.attach` (no edit, no checks); the status bar shows the byte range.
- **Follow** (`following`; `TextSession.following` blocks editing and the disk checks): every `FOLLOW_MS` (2 s) a part
  from `followFrom` (the whole file's `size`, or the part's `end`) is appended (`TextEditor.append`, kept at the end
  when it was there; parts mode keeps at most 4 M characters); `size < followFrom` = rotated: read again. Stopping in
  whole mode reloads the file (text and version for a save).
- Positions: `positions.ts`, `localStorage` `sfe.text.positions` = `{ path: first line shown }`, the latest 100; saved
  500 ms after scrolling stops and on destroy (whole files, not followed); restored after the load unless `edit`.
- `Sfe:FileTypes:Text` (library setting, `fileTypes.` → `settings.scanning`, never a media extension) →
  `ClientConfig.TextExtensions` → `setTextExtensions` (a signal in `formats.ts`, set by `ConfigService.load`), read by
  `isTextFile`.

## Gotchas

- CodeMirror's base theme beats global CSS: style its parts through `readerTheme`, not `reader.css` selectors.
- A view that preventDefaults a key (CodeMirror does for the arrows it handles) is fine: the viewer returns early for
  anything inside `[data-owns-keys]`.
- The test fake `item()` gives extensions with a dot; real items don't.
- Toolbars (every format but EPUB) are overflow rows ([overflow-menus-plan](../development/overflow-menus-plan.md)):
  each viewer has a `more` record of `MenuEntry` factories (shared ones in `formats/toolbar-entries.ts`); the editing
  actions and the fields stay (fields 110 px on phones, Save a copy / Done as icons). No margins between toolbar items
  (padding instead): margins aren't measured. `.text-view` has `grid-template-columns: minmax(0, 1fr)`: without it a
  long line widened the whole viewer.
