# API

Everything the web page does goes through a JSON API, which other programs can use too.

- **Base address:** `/api/v1/…` after the server's address (and its virtual directory, if it has one), e.g.
  `http://localhost:5080/api/v1/tags`.
- **API reference:** `/api/docs` (Swagger UI), for accounts with the right `system.apiDocs`; also **API reference** in
  [Administration › System](../administration/system.md).
- **OpenAPI document:** `/openapi/v1.json` (same right): every route of v1, its parameters, bodies and answers. Two
  additions of SFE's own: `x-sfe-rights` on each route (the [rights](rights.md) it needs: one list per requirement,
  any one right of a list is enough, every list must be met; no list means no right needed) and
  `x-sfe-problem-types` (the [problem types](#problem-types) below).

## Signing in

- Today a program calls the API as a signed-in session: the `sfe.session` cookie, from
  `POST /api/v1/session/login` (`userName`, `password`), kept and sent back with each request.
- Every request other than `GET` needs the header `X-SFE-Request: 1`, or it is refused (400).
- Access tokens for programs (no cookie, no password stored in a script) are planned for a later version.

## Versions and compatibility

The version is part of the address (`/api/v1`), so that pictures, videos and download links, which can't send
headers, carry it too. Inside v1, SFE only adds:

- new routes, new optional parameters and body properties, new properties in answers, new statuses and new problem
  types for new cases;
- never removes or renames a route, parameter, property or enum value, never makes an input required that wasn't,
  never changes a default, never answers the same case with another status or problem type, and never asks for a
  right a route didn't need.

**Programs must ignore properties they don't know**: answers grow from one version of SFE to the next. A change that
can't keep these promises comes as `/api/v2` for the routes it concerns, while v1 keeps answering as before until a
major version of SFE removes it. Each version has its own OpenAPI document.

## The old addresses (`/api/…`)

Before v1 the routes had no version: `/api/tags`. These addresses still answer, exactly as `/api/v1/…`, **until SFE
2.0**, so that links handed out before (external players, downloads) and programs written for them keep working.
Their answers carry `Deprecation` (RFC 9745) and `Link: <…/api/v1/…>; rel="successor-version"`, and the server's log
names each program or account using them once a day. Use `/api/v1` in anything new: routes added since exist only
there.

## Conventions

- Paths are relative to the library root and use `/`; a path leaving the root (`..`, drive letters) is rejected. On
  Windows `\` is accepted as a separator too; on Linux `\` and `:` are ordinary file name characters.
- Lists are paged: an answer gives the cursor of the next page (`next`); no list returns everything at once.
- Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457) (JSON with `type`, `title`, `detail`,
  `status`). Programs test `type`; `detail` is for people and may change.

## Problem types

The `type` of an error that has a case of its own is one of these words. Other errors carry the standard link of
their status.

| `type` | Status | When |
|---|---|---|
| `access-gains` | 409 | moving a folder of a connection would let more people see or change it: confirm first |
| `archive-reading` | 409 | the archive's list is still being read: try again in a moment |
| `archive-unreadable` | 422 | the archive, or one of its files, can't be read |
| `connection-needs-reconnecting` | 409 | the connection's provider no longer accepts its sign-in: sign in again |
| `connection-not-supported` | 409 | the connection's provider can't do that |
| `connection-rate-limited` | 429 | the provider asks to wait (`Retry-After` says how long) |
| `connection-refused` | 403 | the provider doesn't let this account do that |
| `connection-unavailable` | 502 | the provider can't be reached or failed: try later |
| `connection-unusable` | 409 | the connection is turned off, or no longer allowed |
| `file-changed` | 412 | the text file changed since it was read (its current version in `version`) |
| `file-exists` | 409 | something of that name is already there |
| `file-is-link` | 409 | the file is a link: it isn't saved over |
| `folder-rules` | 409 | the folder's access rules can't follow it there |
| `job-running` | 409 | the job still runs: retry once it ends |
| `library-root-missing` | 404 | the library's folder doesn't exist on the server |
| `local-administrators-only` | 400 | a Local server lets only administrators sign in |
| `local-only` | 403 | a Local server answers its own computer only |
| `migration-required` | 503 | the server waits for its update (open the app) |
| `not-encodable` | 422 | some characters can't be written in the file's encoding |
| `not-explorable` | 409 | the file can't be explored as an archive |
| `not-protected` | 409 | nothing in the archive has a password |
| `not-text` | 415 | the file is not text |
| `nothing-to-retry` | 409 | nothing of the job is left to start again |
| `read-only` | 409 | the file is read-only: it isn't saved over |
| `scan-running` | 409 | a scan runs there: try again once it ends |
| `session-only` | 403 | the route needs a signed-in session: an access token may not call it |
| `setup-required` | 503 | the server has no administrator yet (open the app) |
| `tenant-changed` | 409 | the session is in another space now: reload |
| `too-large` | 413 | too large to be shown or saved as text, or read from an archive |
| `version-required` | 428 | saving over a file needs the version it replaces (`If-Match`) |
| `wrong-password` | 403, 422 | the archive needs a password, or the one given doesn't open it |
