# Data server

Code: `src/Sfe.Core/Options/DataServerUrl.cs`, client `core/config.service.ts` (`dataServerUrl`).

- `Sfe:DataServer` is a base (`/` + path appended, trailing slashes trimmed) or a template with `{url}` (path,
  segments encoded) / `{encodedUrl}` (`encodeURIComponent` of the path), kept as written.
- `DataServerUrl` (Core) normalises and validates (http/https once filled), `dataServerUrl` in
  `core/config.service.ts` fills it.
- Not the external player's `{url}`, which is the file's full address ([external-player.md](external-player.md)).
- The picture editor never loads through it (a data server's picture would taint the canvas):
  [picture-editor.md](picture-editor.md).
