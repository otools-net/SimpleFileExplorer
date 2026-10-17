# Copy full paths

Code: `core/copy-path.service.ts` (`CopyPathService`); server `Browse:CopyPaths`, `/api/config` `rootPath`.

- `Browse:CopyPaths` (off by default); `/api/config` `rootPath` = `LibraryPaths.Root` (ends with the server's
  separator) only when on, null otherwise, so the server layout isn't leaked.
- `CopyPathService` joins it with the API path using that last character.
- `navigator.clipboard` only exists on secure pages, so plain-http LAN use falls back to a hidden `<textarea>` +
  `execCommand('copy')`, appended inside the open `<dialog>` (the rest of the page is inert under a modal).
- `CopyPathService.copied` (in memory only, gone on reload) gives tiles, rows and folders the class `copied` (dashed
  outline).
- Items have no copy button (1.63): **Copy full path** is an entry of the item menu (`ItemMenu.entries`, id
  `copyPath`); the location head and the viewer keep their buttons.
- Cards and rows keep only two buttons: tags and ⋮ (`items.spec.ts` checks it). A folder link sits in
  `sfe-folder-item` (`.folder-item`) next to them (no button inside `<a>`); list rows put them in `.row-actions`.
  Anything new for an item goes in the item menu, not on the card.
