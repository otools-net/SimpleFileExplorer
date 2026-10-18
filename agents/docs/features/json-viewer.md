# JSON viewer

Code: client `app/formats/json/` (`json-tree.ts` model, `json-view.component.ts`), `JSON_FORMAT` in
`formats/formats.ts`, the JSON part of `styles/reader.css`, icons `unfold` / `fold` / `format`. Shared with the text
view: `TextSession` (editing, saving, guards, changed elsewhere), `TextEditor` (source pane; `select`, `edit`),
`text-document.ts`, the reading preferences (code profile). No server part: `GET`/`PUT api/files/text`. Tests:
`json-tree.spec.ts`, `json-view.component.spec.ts`, a row in `formats.spec.ts`. User docs:
`docs/user-guide/text-files.md#json`. Plan: [file-viewers-plan.md](../development/file-viewers-plan.md).

## Parsing (`parseJson`)

- **`jsonc-parser`** (Microsoft, MIT, no dependencies, the VS Code one): `parseTree` gives nodes with `offset` /
  `length` in the source, members in the order written. Values are shown from the **source slice**
  (`sourceOf`), so numbers stay as written (`12345678901234567890`, `1.50`); `JSON.parse` would round and reorder
  integer-like keys.
- Strict first (`disallowComments`, no trailing commas); on failure again leniently: no errors then = "JSON with
  comments" (`strict: false`), else `problems` (message, line, column) and the partial tree.
- Chosen over ready-made viewers: `vanilla-jsoneditor` (~10 MB, Svelte, 20 dependencies, its own CodeMirror),
  `json-formatter-js` / `@andypf/json-viewer` (`JSON.parse` based, no large files).

## Tree (`rowsOf`)

- Rows are computed from the expanded set (ids = JSON Pointer paths, `''` the root): only open containers' members,
  **`CHUNK` (200)** at a time, then a `#more` row ("Show 200 more", `shown` map). Nothing proportional to what is
  closed; ids survive a re-parse, so what is open stays open while typing (new containers show closed).
- First open: `expandAll(root, 500)` breadth first (a small file whole). "Open all": `expandAll` from the selected
  container, up to `EXPAND_LIMIT` (5000) rows.
- Filter (`filterTree`, 200 ms after typing): keys and primitive values, any case, `MATCH_LIMIT` (2000);
  `keep` = matches + their containers, marked `id\0holds` when something under them matched (those open by
  themselves); below a match that holds none, everything shows when opened.
- Keyboard on the `role="tree"` (`aria-activedescendant`): ↑ ↓ Home End PageUp/Down, → open / first child, ← close /
  parent, Space, Enter = show in the source, `*` = open all under. Ctrl+F: the filter box with only the tree shown,
  the source's search otherwise.
- Status bar: `pathLabel` (`$.a[0]["b c"]`), Copy path, Copy value (the source slice, as written).

## Editing

- Edit switches to Split; the tree re-parses 300 ms after `session.revision` changes (as Markdown).
- **Format** (while editing, no problems): `jsonc-parser` `format` edits applied with `TextEditor.edit` (one undo,
  dirty), indentation from the source (`indentOf`), comments kept.

## Formats

- `json`, `jsonc`, `geojson`, `topojson`, `webmanifest`, `har`. Not `json5` (unquoted keys) nor `jsonl` / `ndjson`
  (logs: the text view's parts and Follow).
- `FileFormat.edits` (1.70.0): the item menu's Edit and New file's "open in the editor" check it, not `'load' in
  view` (a later read-only format, such as PDF, has a component but doesn't edit).

## Gotchas

- In a script, `String.replace(a, b)` reads `$'` in `b` as "the text after the match": a test expecting `['$', …]`
  pasted that way duplicates the file. Use `split(a).join(b)`.
- `jsonc-parser` was already in `package-lock.json` as a dev dependency (Angular's tooling): `npm install` only
  flips its `dev` flag. Don't hand-edit the lock (other packages pin `"3.3.1"` too).
