# Text editor (saving text files)

Code: client `app/formats/text/text-session.ts` (one per view: edit, save, copy, download, guard),
`text-edit.component.ts` (toolbar buttons, bars), `codemirror.ts` (`setEditable`, `dirty`, `markSaved`, `replace`),
`text-view.component.ts` and `markdown/markdown-view.component.ts` (wired), `library/viewer/viewer.component.ts`
(guards), `library/viewer/leave-viewer.ts` (route guards), `Dialogs.closeAll`, `files/name.dialog.ts` (New file opens
it); server `Core/Files/FileContent.cs` (`OverwriteAsync`), `TextCodec.Encode`, `Trash.CopyInAsync`,
`DirectoryIndexer.RefreshFileAsync`, `FileOperations.CreateFileAsync(..., content)`, `Api/Endpoints/TextEndpoints.cs`.
Tests: `FileContentTests`, `TextCodecTests`, `TextRouteTests`, `text-editing.spec.ts`, `viewer.component.spec.ts`
(guards), `comfort.spec.ts` (New file), `delete.spec.ts` (trash). User docs: `docs/user-guide/editing-text.md`. Reading
side: [text-viewer.md](text-viewer.md).

## Rights (from the server)

- `GET api/files/text` answers `access { overwrite, saveCopy, download, permanent }` and `saveLineEnding`: the client
  never decides from `store.can` (optimistic in searches; the `view` page has no listing).
- `overwrite` = move/copy's **Replace** rule: `files.delete` (or `files.deletePermanently` when `Files:DeletePermanently`)
  at the file and its parent + `files.createFiles` at the parent. `saveCopy` = `files.createFiles` at the parent.
  `permanent` = the space never keeps a previous version.
- No right of its own (decided: like Notepad; revisit if spaces ask for "may edit but not delete").

## Routes

- `PUT api/files/text?path=` names `files.delete`/`files.deletePermanently` (`RequireRight`) and checks the full rule
  inside (403 problem "save a copy instead"). Body `{ text, encoding, lineEnding, session }`; `If-Match` required
  (428 `version-required`; quotes and `W/` stripped; `*` = over whatever is there). Answers 200 `{ version, size,
  modified, trashId }` + ETag, 412 `file-changed` (extension `version`), 422 `not-encodable` (extension `characters`),
  413, 409 `scan-running` / `file-is-link` / `read-only`, 404 for hidden or protected.
- `POST api/files/text` `{ path (folder), name, text, encoding, lineEnding }` (`files.createFiles`): same checks as New
  file (`FileOperations.CreateFileAsync` with content: `CreateNew`, 409 `file-exists`, a half-written file deleted).
- `session`: `^[A-Za-z0-9-]{1,64}$`, made client side with `crypto.getRandomValues` (`randomUUID` needs a secure
  context: plain-HTTP installs on the LAN have none).

## Overwrite (`FileContent.OverwriteAsync`)

Order: resolve (NotFound, links refused, ReadOnly attribute refused) → one `SemaphoreSlim` → `scans.TryBeginOperation`
→ version compare (`FileVersion.OfFileAsync`, streamed SHA-256) → `Trash.CopyInAsync` once per (account, session,
path), remembered 12 h → temp `.{name}.{guid}.sfe-tmp` (`CreateNew`, flushed, Unix mode copied) → `File.Replace` (3
tries, 250 ms) → on failure: temp deleted, that call's trash entry purged → `RefreshFileAsync` (size, times, Sha1 of
the first MB when hashes are on) → `browser.Invalidate(parent)` (change feed) → log with the actor.

- Previous versions: `TrashEntry.Kind = PreviousVersion` (nullable `varchar(24)`, migration `AddTrashEntryKind`;
  SQLite through `AddMissingSqliteColumnsAsync`). Their purge does **not** delete the path's caches (they belong to
  the live file). A restore lands as `name (2).ext`. `TrashItem.previousVersion` marks them on the trash page.
- The file keeps its index row, so tags stay; `RefreshFileAsync` skips files not in the index.

## Encoding

- `TextCodec.Encode(text, encoding, lineEnding)`: the line endings normalised then made `lf`/`crlf`/`cr`; BOM for
  `utf-8-bom` and UTF-16; windows-1252 strict (`EncoderFallback.ExceptionFallback`) listing up to 10 runes it can't
  hold (`Bytes = null` then).
- CodeMirror holds `\n` only: the client sends `saveLineEnding` (the file's; a mixed file's most used, a tie → CRLF).
- 422 → the bar offers **Save as UTF-8** (`utf-8`, or `utf-8-bom` for a file that had a BOM).

## Client

- `TextSession` is `providers: [TextSession]` on each view; the bars and buttons inject it. `attach(item, file)` after
  the read, `setEditor(editor)` once the editor exists (Markdown makes its source editor lazily), with
  `editorOptions()` spread into the editor's options (`editable`, `onChange`).
- `dirty` comes from the editor (`doc.eq(saved)`): undoing back to the saved text is clean.
- Keys (`handleKey`, from the view's `window:keydown`): Ctrl+S always `preventDefault` (never "save page"); saves when
  editing and dirty; Ctrl+Shift+S the copy bar; E (not in an input) starts editing.
- Save a copy: the bar's name (`copyName`: "name (copy).ext"), then `host.open(copy, { edit: true })` in the viewer, or
  `location.assign(view?path=…&edit=1)` on the page. `discarded` is set first so the guards don't ask.
- Markdown: editing switches the mode to Split (not written to the preference) and re-renders the document 300 ms after
  the last change (`revision`).
- Download: a Blob of the text (UTF-8, the file's line endings), named as the file.
- The item menu's **Edit** (`editText`: a text format, `files.view`, and `files.createFiles` or `files.download`)
  opens the viewer with `{ edit: true }`; the server's `access` then decides what saving offers. Pictures get
  **Crop…** (`editPicture`) there too.

## Changed elsewhere, encodings on save (1.69.0)

- `GET api/files/text/version?path=&known=` (`files.view`): `FileContent.VersionAsync` returns `known` as is when the
  size and time in it (`size-ticks-` prefix) are the file's, else hashes it again; over `MaxBytes`, `version` null.
- `TextSession.checkDisk`: on a change-feed event for the file's folder (`ChangeFeed.listen`, other tabs only), on
  `visibilitychange` to visible and every `DISK_CHECK_MS` (30 s, only while visible); not while saving, following or a
  bar is shown. A new version → the `disk` bar (Reload / Keep); a kept version is not asked about again (`ignored`).
- `encoding` and `lineEnding` signals are what a save writes (the status bar's selects while editing); `readEncoding`
  is what `reload` reads with (Reopen with encoding).
- Markdown re-renders on `revision` from the source editor, or from `session.file().text` without it (a reload).

## Guards

- `FileViewHost.guard(canLeave)` registers; `TextSession` releases it on destroy. `canLeave` asks `confirm` once and
  remembers a yes (`discarded`) until the next change, so the route's question after the viewer's passes.
- Viewer: `confirmLeave()` in `close()` (returns false when refused), the dialog's `cancel` (Esc), `go()`, `open()` of
  another file, `searchTag`. `Dialogs.closeAll()` returns false when the viewer refuses (the tag editor's Find stops).
- Routes: `''` has `canDeactivate: [leaveViewer]` with `runGuardsAndResolvers: 'paramsOrQueryParamsChange'` (folder
  and search changes are query changes); `view` has `leaveViewPage` (`ViewPageComponent.canLeave`).
- `beforeunload` while dirty (the session's own listener).
- No in-memory draft (dropped: every way out asks; see the plan).

## Gotchas

- A read-only CodeMirror keeps `contenteditable="true"`: E skips only real inputs (the find panel).
- jsdom: `Range.getClientRects` stubbed in `setup.ts`; spy on `window.confirm`.
- `Api.send` resolves 409 with its body: conflicts the client must tell apart from success are 412/422/428 (rejections).
