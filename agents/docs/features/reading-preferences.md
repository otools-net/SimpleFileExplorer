# Reading preferences of text files (and the view page)

Code: client `app/formats/reading/` (`reading.ts` model and limits, `reading.service.ts`, `reading-panel.component.ts`),
`app/formats/text/text-view.component.ts` (applies them), `formats/text/codemirror.ts` (`readerTheme`),
`app/formats/view-page.component.ts`, `styles/reader.css`, `src/reader-fonts.css` (+ `angular.json`). Tests:
`reading.service.spec.ts` (also the panel), `text-view.component.spec.ts`, `view-page.component.spec.ts`. User docs:
`docs/user-guide/text-files.md#reading-settings`. Plan: [file-viewers-plan.md](../development/file-viewers-plan.md).

## Model

- Two profiles, picked by the format (`FileFormat.profile`): **code** (`TEXT_FORMAT`: monospace, no wrap, line
  numbers) and **prose** (`PROSE_FORMAT`, priority 11: `.txt`, `.md`, `.rst`, `README`, `LICENSE`… — Literata 17 px,
  wrapped, 72 characters, 32 px margins). Both load the same text view.
- Per profile: `font` (id of `READER_FONTS`), `size` (10–32), `lineHeight`, `width` (characters, 0 = whole width),
  `padding`, `wrap`, `lineNumbers`; shared: `theme` (`app`/`light`/`sepia`/`dark`) and `customFont` (a font installed
  on the device, font `custom`). Lists of allowed values in `reading.ts` (selects only: no free numbers).
- `sanitizeReading` takes anything (stored JSON, later the account's defaults) and keeps only valid values.

## Storage

- `ReadingService`: `defaults` (a signal: `DEFAULT_READING` now, the account's own in phase 5) + this browser's
  **overrides** in `localStorage` `sfe.reading` = `{ version: 1, theme?, customFont?, code?, prose? }`: only what was
  changed, a changed profile as a whole. Effective = defaults with overrides over them; **Reset** drops the profile's
  override and the theme's.
- Read once per page load (no `storage` event: another tab's change shows after a reload).

## Applying

- The text view's host gets `--reader-font`, `--reader-size`, `--reader-line-height`, `--reader-width` (`72ch` /
  `none`), `--reader-padding` (`ReadingService.style`), `data-reader-theme` and `.prose`. `readerTheme` uses them on
  `.cm-content` (padding all round; `max-width: calc(width + 2 × padding)` centred — with `none` the calc is invalid,
  so no maximum). Wrap and line numbers go through compartments (`TextEditor.configure`, only what changed); a style
  change calls `requestMeasure` (CodeMirror doesn't see CSS variable changes).
- Colours: `reader.css` defines `--reader-*` and `--tok-*` per theme: dark by default, light for `app` under
  `:root[data-theme="light"]` and for `light`, sepia. `--reader-current-line` is transparent for prose.
- Keys inside the view: Ctrl+= / Ctrl++ / Ctrl+− / Ctrl+0 change the size (prevented: not the browser's zoom).

## Fonts

- `src/reader-fonts.css` imports `@fontsource` Literata, Atkinson Hyperlegible (400, 400 italic, 700) and JetBrains
  Mono (400, 700): a style bundle with `inject: false`, built as `reader-fonts.css` (**not hashed**) and its faces in
  `media/` (hashed). `ReadingService.linkFonts` adds `<link href="reader-fonts.css?v=<version>">` once, when the
  profile shown uses a bundled font. Each face has a `unicode-range`: only the subsets a page shows download.

## Panel ("Aa")

- `sfe-reading-panel` inside the text view (absolute, top right; a fixed bottom sheet at ≤ 640 px), for the profile of
  the file shown. Esc (prevented, so the viewer stays open) and a press outside close it; the "Aa" button is
  `data-reading-toggle` (outside presses on it don't count, it toggles).

## View page

- `view?path=…[&asText=1]` (lazy route): `config.load()`, `api.properties(path)` for the real item (404/403 → "not
  there"), `formatOf`; a builtin (media) format `location.replace`s to the file itself; otherwise the format's
  component with `FILE_VIEW_HOST` `{ place: 'page', can: config.can }`. Bar: back to the folder (`./?path=parent`),
  name, size · date · folder, Download.
- Links: `Urls.view(path)`; `FileTileComponent.url` and the viewer's "Open in new tab" use it for formats with `page`
  (a modified click on a text file opens its page; a plain click still opens the viewer).
