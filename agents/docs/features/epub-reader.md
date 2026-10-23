# EPUB reader

Code: client `app/formats/epub/` (`epub-document.ts`: epub.js behind `EPUB_LOADER`; `epub-view.component.ts`),
`EPUB_FORMAT` in `formats/formats.ts`, the EPUB part of `styles/reader.css`. Server: `Core/Files/EpubEntries.cs`,
`Sfe.Api/Endpoints/EpubEndpoints.cs` (`GET api/files/epub/{book}/{**entry}`). Tests: `epub-view.component.spec.ts`
(a fake loader: jsdom lays nothing out), a row in `formats.spec.ts`, `EpubEntriesTests`,
`TextRouteTests.TheFilesOfABookAreServedAsTheRightsAllow`; the real epub.js runs in the docs screenshots
(`agents/tools/docs-screenshots/make-epub.mjs`, the `epub frames` check). User docs:
`docs/user-guide/text-files.md#epub-books`.

## Why the server serves the book's files

- The readers that open the zip in the browser (foliate-js, epub.js with an `ArrayBuffer`) show chapters from `blob:`
  URLs (frames, styles, fonts): the app's CSP (`default-src 'self'`) blocks them, and loosening it for the whole app
  isn't worth it.
- So the book is a folder of the app: `api/files/epub/{token}/{entry}`, `token` = base64url of the book's path (one
  segment: the links inside the book resolve under it), `entry` read from the zip as asked (`ZipArchive`, never
  unpacked; as written, else any case; folders and entries over 64 MB answer 404). `files.view` and the folder rules
  at the book, `FileEndpoints.Protected`, `FilePolicy` (sandbox) on every answer, `private, no-cache` + an ETag of the
  book's size and time (304 while unchanged). `.js` entries are served as `text/plain`.

## epub.js

- **`@likecoin/epub-ts`** (BSD-2, a maintained TypeScript fork of epub.js; jszip comes along, CommonJS, allowed in
  `angular.json`, unused for served books). `ePub(url)` with the folder URL (ending in `/`).
- Chapters go into frames through `srcdoc` (epub.js' default), sandboxed `allow-same-origin` only
  (`allowScriptedContent: false`): a `srcdoc` frame takes the app's CSP, so no script of a book runs, and its
  styles, pictures and fonts load from `'self'`.
- Layouts (`EpubMode`): `pages` = `flow: paginated, spread: none`; `spread` = `paginated, spread: auto,
  minSpreadWidth: 800` (two pages when wide); `scroll` = `flow: scrolled, manager: continuous`. Changing layout
  destroys the rendition and makes a new one at the same CFI.
- The reading preferences (prose profile) reach the pages as a theme `sfe` (`themes.register({ sfe })` +
  `select`): font family, size, line height on the body, `inherit !important` in the text's elements; with sepia or a
  dark background (`forceColors`) the book's colours give way. Values, not variables (the frames don't have the app's
  CSS): the host's computed `background-color` / `color` (rgb), `--reader-font` resolved. A bundled font links
  `reader-fonts.css` into each chapter (content hook).
- Progress: `book.locations.generate(1600)` in the background; until then `…`. The place read last:
  `sfe.epub.places` `{ path: CFI }`, the latest 100, saved 500 ms after a move, passed as `start`.
- Keys: the view's own and those epub.js forwards from the frames (`keydown`); swipes from `touchstart` / `touchend`.
  Scroll layout: ↑ ↓ scroll 48 px, Page Up / Down and Space a screen (`manager.container.scrollBy`).
- The `.text-view` grid's second row is the messages slot (`.pdf-messages`), as in the PDF view.

## Gotchas

- `themes.register(name, rules)` works but its types say otherwise: use `register({ name: rules })`.
- The book opens before the host's `[style]` binding is applied: its computed `--reader-font` is still reader.css'
  monospace default. The font comes from `ReadingService.style('prose')` (its `var()`s resolved on the host), and the
  style is set again once the book is open.
- An EPUB made by hand for tests: `mimetype` first and stored (`make-epub.mjs` stores everything).
