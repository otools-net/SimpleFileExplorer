# CSV viewer

Code: client `app/formats/csv/` (`csv-table.ts` model, `csv-view.component.ts`, `papaparse.d.ts`),
`formats/virtual-rows.ts` (`VirtualRows`, reusable), `CSV_FORMAT` in `formats/formats.ts`, the CSV part of
`styles/reader.css` (it reuses the JSON view's `.json-pane`, `.json-filter`, `.json-problem`, `.json-selection`).
Shared with the text view: `TextSession`, `TextEditor` (source pane), `text-document.ts`, the code reading profile.
No server part. Tests: `csv-table.spec.ts`, `csv-view.component.spec.ts`, a row in `formats.spec.ts`. User docs:
`docs/user-guide/text-files.md#csv-and-tsv`. Plan: [file-viewers-plan.md](../development/file-viewers-plan.md).

## Parsing (`parseCsv`)

- **papaparse** (MIT, no dependencies): quotes, separators and line breaks inside quotes, any line ending,
  `skipEmptyLines: true`. Separator: `delimiterFor(extension)` (`tsv` tab, `psv` pipe), else guessed among `, ; \t |`
  (the toolbar's select overrides it, for this view only). `UndetectableDelimiter` (one column) is not a problem.
- Problems (an unclosed quote…) say the **record** (papaparse's `row` + 1, the header included): papaparse gives no
  offsets, so there is no "show in the source".
- CommonJS only: listed in `angular.json` `allowedCommonJsDependencies`, imported by name (`import { parse }`; no
  `esModuleInterop`).
- **Not `@types/papaparse`**: it references `@types/node`, whose `setTimeout` returns `Timeout` and breaks every
  `number` timer of the app. `papaparse.d.ts` declares the part used.

## Table

- `columnsOf`: names from the header row (empty or repeated → letters `A`, `AA`…), widths in `ch` from the longest
  cell of the first 500 rows (3–40, +1; the template adds the padding: `calc(Nch + 17px)`), `numeric` when nine in
  ten non-empty sampled cells are numbers (`isNumber`: `1,234.5`, `1.234,5`, `1 234`, `12%`). Header on by default, `sfe.csv.header` = `0` turns it off.
- `order` = `filterRecords` (any cell, any case, 200 ms after typing) then `sortRecords` (header click: ascending,
  descending, none; `numberOf` for numeric columns, `Intl.Collator` numeric otherwise, empty cells last, ties in file
  order). Positions are indexes into `rows` (`data-record`).
- **`VirtualRows`** (`@tanstack/virtual-core`, headless, MIT): only the rows on screen (+12) are in the DOM, absolutely
  placed (`translateY`) in a `.csv-body` as tall as all; the header row is sticky, the row numbers sticky left. Rows
  are of equal height: the header's `offsetHeight`, measured again when the reading style changes (`setRowHeight`).
  Its rect observer falls back to 600 px when the element measures 0 (jsdom, hidden), so specs see rows.
- Columns are one CSS grid template (`template`) on each row; columns aren't virtualised (a wide table draws all its
  columns of the rows shown).
- Keyboard on the `role="table"` scroller: arrows, Page Up / Down (a screen), Home / End (row ends; with Ctrl the
  table's), Ctrl+C copies the selected cell when no text is selected. Cells show at most 500 characters, line breaks
  as ↵ (`shownCell`).

## Editing

- As JSON: Edit switches to Split; the table re-parses 300 ms after `session.revision` changes. No editing in the
  cells (it would have to rewrite the source keeping its quoting).

## Gotchas

- jsdom doesn't scroll: `scrollToIndex` sets nothing. A spec simulating a scroll defines `scrollTop` on the scroller
  and dispatches `scroll` (`observeElementOffset` reads it).
