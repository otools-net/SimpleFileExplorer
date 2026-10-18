# Text viewer (and the format registry)

Code: client `app/formats/formats.ts` (registry), `app/formats/text/` (`text-view.component.ts`, `codemirror.ts`,
`languages.ts`), `library/viewer/viewer.component.ts`, `ListingStore.viewable`, the `openAsText` entry of
`files/item-menu.ts`, `styles/reader.css`; server `Core/Files/TextCodec.cs`, `Core/Files/FileContent.cs`,
`Api/Endpoints/TextEndpoints.cs`. Tests: `TextCodecTests`, `TextRouteTests`, `formats.spec.ts`,
`text-view.component.spec.ts`, `viewer.component.spec.ts` (Documents), `files.spec.ts` (Open as text). User docs:
`docs/user-guide/text-files.md`. Plan and later phases: [file-viewers-plan.md](../development/file-viewers-plan.md).

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
  `FILE_VIEW_HOST` (`place`, `can(right)`); the stage gets `.document` (fills the stage).
- `[data-owns-keys]` on a view's root: the viewer ignores keys, wheel and swipes from inside it; Esc inside it (unless
  the view used it, e.g. to close its find bar) focuses the dialog (`tabindex="-1"`), the next Esc closes.

## Text view

- `GET api/files/text?path=` (`files.view`, `FolderScoped`; hidden, missing, folders, server files, work folder → 404;
  no right → 403): whole file up to 10 MB (`MaxBytes`; 413 `too-large`), `TextCodec.Decode` (BOM → UTF-8/16; else
  strict UTF-8, else windows-1252; a NUL in the first 8 KB without a BOM → 415 `not-text`); ETag = `FileVersion.Token`
  (`size-ticks-sha256/16`), `no-store`. Read with `FileShare.ReadWrite | Delete` (logs being written).
- CodeMirror 6, read only (`EditorState.readOnly`: cursor and selection work). Compartments: wrap, line numbers,
  language. Language: `languageFor(name)` — aliases (MSBuild files → XML, `.conf`/`.cfg`/`.env`… → Properties
  files), then our `Log` and `Subtitles` stream modes, then `@codemirror/language-data` (each language its own chunk).
- Colours: `readerTheme` (`EditorView.theme`) uses `--reader-*` variables; highlighting uses `classHighlighter`
  (`tok-*` classes) coloured by `--tok-*` in `reader.css`; both follow the app's light/dark theme.
- Font, size, spacing, wrap, line numbers and colours are the reading preferences of the format's profile (code or
  prose): [reading-preferences.md](reading-preferences.md), also for the page of its own (`view?path=`).
- Keys: CodeMirror's `searchKeymap` + `defaultKeymap` (Ctrl+F, F3/Ctrl+G, Ctrl+Alt+G go to line); the component also
  takes Ctrl+F and Ctrl+Alt+G from the window, so they work when the focus is on the viewer rather than in the text.
- Failures: 413/415/500 offer **Download** when `files.download`; 404/403 say so.

## Gotchas

- CodeMirror's base theme beats global CSS: style its parts through `readerTheme`, not `reader.css` selectors.
- A view that preventDefaults a key (CodeMirror does for the arrows it handles) is fine: the viewer returns early for
  anything inside `[data-owns-keys]`.
- The test fake `item()` gives extensions with a dot; real items don't.
