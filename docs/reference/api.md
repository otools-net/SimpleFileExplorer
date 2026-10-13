# API

Everything the web page does goes through a JSON API, which other programs can use too.

- **API reference:** `/api/docs` (Swagger UI), for accounts with the right `system.apiDocs`; also **API reference** in
  [Administration › System](../administration/system.md).
- **OpenAPI document:** `/openapi/v1.json`.

## Conventions

- Requests are made by a signed-in session (the `sfe.session` cookie, from `POST api/session/login`).
- Every request other than `GET` under `/api` needs the header `X-SFE-Request: 1`, or it is refused (400).
- Paths are relative to the library root and use `/`; a path leaving the root (`..`, drive letters) is rejected. On
  Windows `\` is accepted as a separator too; on Linux `\` and `:` are ordinary file name characters.
- Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457) (JSON with `title`, `detail`, `status`).
