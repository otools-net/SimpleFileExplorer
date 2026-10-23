# Markdown viewer

Code: client `app/formats/markdown/` (`markdown.ts` renderer, `markdown-view.component.ts`), `MARKDOWN_FORMAT` in
`formats/formats.ts`, `languageForName` in `formats/text/languages.ts`, the Markdown part of `styles/reader.css`.
Shared with the text view: `formats/text/text-document.ts` (`textDetails`, `textFailure`), `TextEditor` (source pane),
the reading preferences (prose profile). Tests: `markdown.spec.ts` (security, links, images, headings, fences),
`markdown-view.component.spec.ts`. User docs: `docs/user-guide/text-files.md#markdown`.

## Rendering (`renderMarkdown`)

- `markdown-it` (default preset: tables, strikethrough) with **`html: false`** (HTML in a document is text),
  `linkify` on but `fuzzyLink`/`fuzzyEmail` off (`notes.md`, `photo.jpg` are names, not sites: `.md` is a TLD).
  markdown-it's own `validateLink` already refuses `javascript:`, `vbscript:`, `file:`, non-image `data:`.
- Then **DOMPurify** (`RETURN_DOM_FRAGMENT`; forbidden: `style`, forms, iframes, objects; attributes `style`, `id`,
  `name`), inserted with `replaceChildren`: never `innerHTML`.
- Headings get ids `md-<GitHub slug>` (duplicates `-1`, `-2`); `#section` links become `#md-section` with
  `data-anchor` (the view scrolls inside itself).
- Links: `http(s)://`, `//host` and `mailto:` → new tab, `rel="noopener noreferrer nofollow"`, class `external`;
  other schemes and paths leaving the library → no `href`; relative (or `/…` = the library root) →
  `resolveLibraryPath` (decoded, `..` checked, query/fragment dropped): ending in `/` → the folder's listing
  (`./?path=`), a file with a viewer (`formatOf` of a `kind: 'file'` item made from the path) → `view?path=` +
  `data-path`, anything else → the raw file in a new tab.
- Images: library paths → `urls.file` (raw: sandboxed CSP, data server if any); `data:image/…` kept; other sites
  (the app CSP allows `img-src http: https:` for data servers) → a `.md-blocked-image` placeholder ("alt — image from
  host") until `showExternalImages` (then `referrerpolicy="no-referrer"`); anything else loses its `src`.
- Code fences: `highlightFences` after insertion — `languageForName` (our log/subtitle modes, `language-data` by
  name or alias, our aliases), `support.language.parser.parse` + `highlightCode(… classHighlighter …)`: the same
  `tok-*` classes and colours as the text view.

## View

- Modes **Read** / **Source** / **Split** (`sfe.markdown.mode`), outline toggle (`sfe.markdown.outline`, h1–h3).
  The source pane is a `TextEditor` made the first time it is shown (prose wrap / line numbers apply).
- The document is rendered into `article.markdown-body > .markdown-content`: the width (`--reader-width`, in `ch`)
  is on the inner element, so headings don't count it in their larger characters.
- Clicks: `data-anchor` → scroll; `data-path` with a plain click → `FILE_VIEW_HOST.open(item)` (the viewer shows that
  document, outside the listing's sequence); modified clicks follow the `view?path=` link (new tab). On the `view`
  page `open` is absent: links are followed.
- Ctrl+F: the browser's own find in Read mode; CodeMirror's when the source is shown. Ctrl+= / − / 0: prose size.
- Status bar: "Markdown · encoding · line ending · size", and "N images of other sites not shown · Show".

## Gotchas

- `ApiTests.WebClientHasNoExternalResources` scans the specs too: use dotless hosts (`https://tracker/x.png`) in
  tests; and avoid regex literals like `/\/\//` (they read as `//host`): `\/{2}`.
- markdown-it 15 ships its own types (no `@types/markdown-it`); its default export is a factory, the class is the
  `MarkdownIt` type.
