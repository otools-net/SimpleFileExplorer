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

A program calls the API in one of two ways:

- **With an access token** (recommended): see [Calling with a token](#calling-with-a-token) below.
- **As a signed-in session**: the `sfe.session` cookie, from `POST /api/v1/session/login` (`userName`, `password`),
  kept and sent back with each request. Every request other than `GET` then needs the header `X-SFE-Request: 1`, or
  it is refused (400).

## Calling with a token

An access token is made on [My account › Access tokens](../administration/my-account.md#access-tokens) and shown
once, as `sfe_` followed by two parts (`sfe_XXXX_...` below stands for yours). Keep it like a password.

- **Header:** send `Authorization: Bearer sfe_XXXX_...` with every request. No cookie and no `X-SFE-Request` are
  needed; a request with a token ignores any cookie. A token in the address (`?access_token=`) is never accepted.
- **What it may do:** what its account may do now, or only the rights chosen when it was made. It never does more
  than the account: a right the account loses, the token loses too.
- **Library:** a token made for one library works only there. Otherwise it works in the library named by the header
  `X-SFE-Tenant: <id>`, among those the account reaches, else in the account's first. `GET /api/v1/config` lists the
  account's libraries (`tenants`: `id`, `slug`, `name`) and tells which one the request works in (`tenant`).
- **Routes refused to tokens** (403, problem type `session-only`): signing in and out, the password, account
  recovery, switching space, the setup, installation and update pages, and the tokens themselves
  (`/api/v1/me/tokens`, `/api/v1/access/tokens`). A token never makes another token or changes a password.
- **Jobs:** a move, copy, zip or extraction started with a token keeps the token's limits for each item, and stops
  at the next item once the token is revoked or expires.
- **Log:** every change made with a token (anything but `GET`) is written in the server's
  [log](../administration/logs.md) with the account and the token's name.

| Answer | When |
|---|---|
| 401 | the token is wrong, expired or revoked; its account is disabled or deleted; tokens are switched off; or too many wrong tokens came from this address (it is refused every token for a while). `WWW-Authenticate: Bearer error="invalid_token"` |
| 403 | the token or its account lacks the route's right; the library named isn't the token's or the account's; the route is session-only (`session-only`); a Local server answered another computer (`local-only`) |
| 429 | too many requests of one kind (thumbnails, searches…) from the account in a short time: wait and try again. A token shares its account's budget |

Listing a folder (`path` relative to the library root; `offset` and `limit` page through it) and downloading a file
(right `files.download`) with curl:

```sh
curl -H "Authorization: Bearer sfe_XXXX_..." \
  "http://localhost:5080/api/v1/browse?path=Photos/2024&limit=100"
curl -H "Authorization: Bearer sfe_XXXX_..." -H "X-SFE-Tenant: 2" \
  -o beach.jpg "http://localhost:5080/api/v1/files/raw/Photos/2024/beach.jpg?download=true"
```

The same with PowerShell:

```powershell
$headers = @{ Authorization = "Bearer sfe_XXXX_..." }
$listing = Invoke-RestMethod -Headers $headers "http://localhost:5080/api/v1/browse?path=Photos/2024&limit=100"
$listing.items | Select-Object name, size
Invoke-RestMethod -Headers ($headers + @{ "X-SFE-Tenant" = "2" }) -OutFile beach.jpg `
  "http://localhost:5080/api/v1/files/raw/Photos/2024/beach.jpg?download=true"
```

A listing gives `items`, `total`, `offset` and `limit`: ask again with `offset` moved on until it reaches `total`.
Swagger UI (`/api/docs`) takes a token too: **Authorize**, then paste it.

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
