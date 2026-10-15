# Copy full paths

Code: `core/copy-path.service.ts` (`CopyPathService`); server `Browse:CopyPaths`, `/api/config` `rootPath`.

- `Browse:CopyPaths` (off by default); `/api/config` `rootPath` = `LibraryPaths.Root` (ends with the server's
  separator) only when on, null otherwise, so the server layout isn't leaked.
- `CopyPathService` joins it with the API path using that last character.
- `navigator.clipboard` only exists on secure pages, so plain-http LAN use falls back to a hidden `<textarea>` +
  `execCommand('copy')`, appended inside the open `<dialog>` (the rest of the page is inert under a modal).
- `CopyPathService.copied` (in memory only, gone on reload) gives tiles, rows and folders the class `copied` (dashed
  outline).
- A folder link sits in `sfe-folder-item` (`.folder-item`) next to its button (no button inside `<a>`); list rows put
  buttons in `.row-actions` (last grid column `auto`).
