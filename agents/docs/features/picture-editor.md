# Picture editor

Code: client `src/app/editor/` (`ImageEditorDialog`, `edits.ts`, `crop.ts`); server
`src/Sfe.Core/Editing/EditedImages.cs`, route `POST api/editor/images`.

- The browser edits, the server stores.
- `editor/edits.ts`: an edit is an `EditOperation` (kind + parameters) with an `EDIT_KINDS` handler (`size`, `draw` on
  a 2D canvas, `describe`) and a tool in `EDITOR_TOOLS`; `renderEdits` applies the list in turn from the original (each
  step a new canvas), `encode` = `canvas.toBlob` in the original's type if JPEG/PNG/WebP, else PNG, at
  `config.editor.quality`.
- A new edit (rotate, flip, resize) = a new operation + handler + tool (+ its panel in `ImageEditorDialog`); nothing
  changes on the server.
- The dialog loads the original through `urls.original` (always `api/files/raw`: a data server's picture would taint
  the canvas) via `PICTURE_LOADER` (tests provide fake pictures; `fakeCanvases()` stubs `getContext`/`toBlob`).
- Crop coordinates are in pixels of the current result (`crop.ts`, whole pixels; `ASPECTS` lists each ratio with its
  portrait twin, which `flipAspect` relies on; picking one = `largestAround` the frame's centre); drags scale screen →
  picture pixels by the `.crop-layer` box (`.crop-clip` around it reaches past the picture so edge handles aren't cut).
- Save: `POST api/editor/images?path=` with the raw bytes (no JSON, any Content-Type) → `EditedImages.SaveAsync`:
  buffers ≤ 200 MB (`RequestSizeLimitAttribute` lifts Kestrel's 30 MB), sniffs JPEG/PNG/WebP and needs a readable size
  (`MediaDimensions.ReadImageHeader`), writes `FileMode.CreateNew` to `Editor:OutputFolder` + the original's folder
  (`name (2).ext`...; empty setting or already inside it → beside the original, `name (edited)`), then
  `DirectoryIndexer.IndexFilesAsync`.
- Errors are 404/400/413/500, never 409 (the client's `Api` resolves 409s).
- `editor.outputFolder` is per library (`PerLibraryKeys`); `/api/config` `editor` is null without `files.edit`.
