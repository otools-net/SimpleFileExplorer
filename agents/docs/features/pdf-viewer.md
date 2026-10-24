# PDF viewer

Code: client `app/formats/pdf/` (`pdf-document.ts`: pdf.js behind `PDF_LOADER`; `pdf-view.component.ts`),
`PDF_FORMAT` in `formats/formats.ts`, `angular.json` (assets `pdfjs/`, style bundle `pdf-viewer`), the PDF part of
`styles/reader.css`, `formats/text/positions.ts` (pages remembered). No server part: the file comes from
`api/files/raw` (Range requests). Tests: `pdf-view.component.spec.ts` (a fake loader: jsdom has no canvas), a row in
`formats.spec.ts`; the real pdf.js runs in the docs screenshots (`agents/tools/docs-screenshots`: `make-pdf.mjs`, the
`pdf pages drawn` check). User docs: `docs/user-guide/text-files.md#pdf-documents`.

## pdf.js, bundled and local

- **pdfjs-dist** (Apache-2.0, Mozilla): the core (`pdfjs-dist`) and the viewer's components
  (`pdfjs-dist/web/pdf_viewer.mjs`: `PDFViewer`, `EventBus`, `PDFLinkService`, `PDFFindController`), both in lazy
  chunks. The components read the core from `globalThis.pdfjsLib`: `openPdf` imports the core, sets the global, then
  imports the components.
- Served by the app, never a CDN (fully local): the worker (`pdfjs/pdf.worker.min.mjs`, `GlobalWorkerOptions.workerSrc`),
  the character maps (`pdfjs/cmaps/`, CJK documents) and the standard fonts (`pdfjs/standard_fonts/`, the 14 fonts
  documents may use without embedding them), copied by `angular.json` assets from `node_modules/pdfjs-dist`. URLs are
  made from `document.baseURI` (a virtual directory works).
- pdf.js' CSS (168 kB, its images in `media/`) is the non-injected bundle `pdf-viewer.css`, linked once by the view
  (`linkViewerStyles`, with the app's version as a query, as `reader-fonts.css`).
- The app's CSP holds: the worker is `'self'` (`script-src`); **`useWasm: false`** (no `'wasm-unsafe-eval'`: pdf.js
  falls back to its JavaScript decoders); pdf.js 6 has no eval (`isEvalSupported` is gone); pages are canvases, fonts
  `FontFace` from data. The file comes from `urls.original` (`api/files/raw`), never the data server (`connect-src
  'self'`).
- Safety: no `PDFScriptingManager` (documents' JavaScript never runs), `annotationMode: ENABLE` (links, forms shown not
  filled), XFA off, links of other sites `target=_blank rel="noopener noreferrer nofollow"`.
- No language given: pdf.js' `GenericL10n` uses its built-in English strings (fetches nothing).

## The view

- Toolbar: previous / page box / next, zoom (− list +: `auto`, `page-width`, `page-fit`, 50–300 %; Ctrl+= / Ctrl+− /
  Ctrl+0), outline toggle (`sfe.pdf.outline`), find (Ctrl+F; Enter, Shift+Enter; `updatefindmatchescount`), dark pages
  (`sfe.pdf.dark`: `filter: invert(.9) hue-rotate(180deg)` on the canvases).
- ← / → turn the pages outside inputs (`data-owns-keys`: the viewer leaves them). A zoom that fits (`auto`,
  `page-width`, `page-fit`) is applied again when the frame is resized (`ResizeObserver`).
- The page read last: `writePosition(path, page, PDF_PAGES_KEY)` 500 ms after it changes, passed as `start` to the
  loader (applied at `pagesinit`).
- Password: pdf.js' `onPassword` asks the view (`events.password(wrong)`), which shows a form; giving up destroys the
  task (`PdfLockedError`). Not a PDF: `InvalidPDFException` → `NotPdfError` → Download.
- The outline is flattened with depths (`outlineRows`): a recursive `ng-template` whose context variable has the
  template's name shadows it (`createEmbeddedViewImpl is not a function`).

## Gotchas

- pdf.js names its pages `.page`, as the app names its main column (`location.css`: padding): `.pdf-view .pdfViewer
  .page { padding: 0 }`. Don't override its width there (pdf.js sets it in its CSS, then inline).
- `PDFViewer` needs its container absolutely placed (`.pdf-frame` relative, `.pdf-container` `position: absolute`).
- `pdf_viewer.css` defines `--bg` only inside its signature dialog: no clash with the app's.
- The `.text-view` grid has four rows (toolbar, editing bar, content, status): the PDF view has no editing bar, so its
  messages sit in a `.pdf-messages` that is always there (row two), or the pages fall in a row as tall as its content.
- Toolbar ([overflow-menus](overflow-menus.md)): pages and Smaller / Larger stay; the zoom
  list, Outline and Dark pages may leave into "⋯" (zoom as a submenu of sizes). On phones (≤ 640 px) find is a
  magnifier (`.pdf-find-toggle`, `findOpen`): the field shows absolutely under the bar, so the item stays the
  button's width; Ctrl+F opens it (focus after a tick), Esc on an empty field hides it.
