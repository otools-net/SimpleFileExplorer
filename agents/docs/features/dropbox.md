# Dropbox

Code: `src/Sfe.Core/Connections/Dropbox/` (`DropboxProvider`, `DropboxStorage`, `DropboxErrors`),
`Settings/ConnectionSettings.cs` (`DropboxSettings`), `Connections/ProviderSecrets.cs`, `IRegisteredApp`; Api
`ConnectionProviderEndpoints.cs`, `ProviderSecretServices.cs`; client `connections/provider-setup.component.ts`.
Tests: `DropboxProviderTests`, `DropboxStorageTests`, `ConnectionProviderRouteTests`, transport
`tests/Sfe.Tests/Infrastructure/Connections/RecordedDropbox.cs`. Manual check list:
[agents/tools/dropbox-check](../../tools/dropbox-check/README.md). The provider contract:
[connection-providers](connection-providers.md). User docs: [Setting up
Dropbox](../../../docs/administration/connections.md#setting-up-dropbox-server-administrators).

## The SDK

- **The official `Dropbox.Api` 7.4.0** (chosen 2026-11-13: maintained, .NET Standard 2.0, one dependency,
  `Newtonsoft.Json`). Every call goes through the `HttpClient` given in `DropboxClientConfig` (`HttpClient`,
  `LongPollHttpClient`) and the `client` argument of `DropboxOAuth2Helper.ProcessCodeFlowAsync`: that client comes
  from `OutboundPolicy` ([connection-providers](connection-providers.md)). `MaxRetriesOnError = 0`: SFE's gates
  decide the waits.
- Rule hosts: `api.dropbox.com` (token), `api.dropboxapi.com` (RPC), `content.dropboxapi.com` (download, upload),
  `*.dl.dropboxusercontent.com` (temporary links). The browser alone goes to `www.dropbox.com/oauth2/authorize`.
  `notify.dropboxapi.com` (long poll) is not used.
- Capabilities: ranged reads, uploads in parts of at most 150 MB, server-side copy and move, thumbnails, change
  cursor, content hash `dropbox`, own trash, case-insensitive names, free space; no folder dates.

## App key, secret and sign-in

- The server administrator registers their own Dropbox app (Scoped access; scopes `account_info.read`,
  `files.metadata.read`, `files.content.read`, `files.content.write`).
- App key: server setting `connections.dropbox.appKey`; without it `SignInMethods` is empty, so Dropbox is not offered.
- App secret: **optional** (PKCE signs in without it; the token request carries `client_secret` only when given).
  Owner's choice (2026-11-13): settable on the page, write-only: `PUT`/`DELETE api/connections/providers/{kind}/secret`
  writes `ProviderSecrets` (one `SecretFiles` file per kind in `connections/` beside the setup code, cached; not in
  backups). `Sfe:Connections:Dropbox:AppSecret` in the configuration wins. Only kinds implementing `IRegisteredApp`
  have a secret (404 for others).
- `GET api/connections/providers/{kind}`: offered, the return address to register
  (`ConnectionProviderEndpoints.ReturnAddress`: scheme, host, path base of the request, then
  `/api/connections/oauth/callback`), where the secret comes from (`none`, `page`, `configuration`). `POST …/test`:
  `check/app` with key and secret (nothing sent without a secret). All need `server.settings`.
- The return address must match what is registered at Dropbox exactly: behind a proxy it comes from the forwarded
  headers ([sessions-and-pipeline](../architecture/sessions-and-pipeline.md)), under a virtual directory it includes
  it. When it can't match, people use the code flow.
- PKCE: `GeneratePKCECodeVerifier` (the step's `Pending`, kept by the server), `GeneratePKCECodeChallenge` (S256),
  `GetAuthorizeUri(Code, key, redirect or null, state, tokenAccessType: Offline, scopes, challenge)`. Without a
  redirect address Dropbox shows the code to copy: the two flows differ only by that argument.
- The grant: the refresh token, the e-mail and `account_id` (`users/get_current_account`). A client made from the
  refresh token fetches access tokens by itself; the refresh token does not change, so nothing is written back.

## Errors (`DropboxErrors.CallAsync` wraps every SDK call)

- `ApiException<T>`: read from its `error_summary` (`not_found`, `not_folder`, `conflict`, `incorrect_offset`…), not
  the typed unions; path not found = `NotFound`.
- `RateLimitException` = `RateLimited` with `RetryAfter` seconds; `AuthException` (401, a refused refresh
  `invalid_grant`) and `OAuth2Exception` (a wrong code) = `CredentialsEnded`; `RetryException` (5xx) and
  `HttpRequestException` = `Unavailable`; `BadInputException` (400) = `Refused`.
- The SDK throws `InvalidOperationException` (source `Dropbox.Api`) when an answer has another shape (an empty 404, a
  proxy's page): `Unavailable`.

## Storage (`DropboxStorage`)

- Listing: `ListFolderAsync(path, limit)` then `ListFolderContinueAsync(cursor)`; `""` is the root; entries carry
  `PathLower` (to compare), `PathDisplay` (to show), files `Size`, `ServerModified`, `ContentHash`,
  `IsDownloadable`; the provider's `id` goes to `RemoteItem.Id`. The last page's cursor is the change cursor.
  Listings are eventually consistent; folders have no date.
- **No ranged download in the SDK**: a ranged read asks `GetTemporaryLinkAsync` (4 hours) and makes a `GET` with
  `Range` on it through the same policy-bound client. The link is never given to the browser.
- Upload sessions (sequential): `upload_session/start`, `append_v2` (cursor with offset), `finish` (`CommitInfo`,
  `WriteMode.Add`); the SDK adds each part's `content_hash`. A request carries at most 150 MB.
  `incorrect_offset` → `WrongOffset` with Dropbox's offset. **The SDK closes the part's stream once sent**: callers
  read its length before the call (every upload into a real Dropbox failed with a 500 until 2026-11-19; the
  `FakeStorage` now closes it too, so the suites catch it).
- Create, move, copy, delete server side; Replace deletes a file at the target first, never a folder; a case-only
  rename goes through a temporary name. Delete goes to Dropbox's deleted files.
- Content hash: Dropbox's own (`dropbox`: SHA-256 of 4 MB blocks' SHA-256s), computed by `ContentHashes` while a
  transfer streams ([transfers](transfers.md)).
- `too_many_write_operations` on parallel writes in one namespace: writes are serialised per connection
  (`TransferCalls`).
- Team spaces and shared folders have their own roots (not handled specially).

## Metadata, thumbnails, cursors, limits (SDK 7.4.0 docs, `files.stone`, the performance page)

- Listing gives per file `id`, `path_lower`, `path_display`, `size`, `server_modified`, `client_modified`, `rev`,
  `content_hash`, `is_downloadable`; folders have no date.
- `media_info` (dimensions, `time_taken`, GPS, video `duration` in ms) is **not set by `list_folder` /
  `continue` / `get_thumbnail_batch` since 2019-12-02** (`include_media_info` has no effect there); still given by
  `get_metadata(include_media_info)` and `get_thumbnail(_v2)`, possibly `pending`. So the index never has sizes or
  durations from the sync; never a `get_metadata` per file ([connection-previews](connection-previews.md)).
- Thumbnails: `get_thumbnail(_v2)`, `get_thumbnail_batch` (**at most 25** a call, base64 in JSON); documented for
  images only (jpg, jpeg, png, tiff, tif, gif, webp, ppm, bmp), none over **20 MB** (`unsupported_image`). **Seen on a
  real account (2026-11-19)**: videos (WebM, MP4, MKV, AVI) and a 25 MB JPEG got thumbnails too; `get_thumbnail`
  gave no `media_info` for any file
  ([connection-index](connection-index.md#measured-on-a-real-dropbox-2026-11-19)). Formats jpeg,
  png, webp; sizes 32×32 to 2048×1536; modes strict, bestfit, fitone_bestfit, original.
- `list_folder`: `limit` 1–2000, approximate; `recursive` lists a whole tree (the SDK warns it may fail on very
  large ones); pages chained by cursor (one listing is sequential).
- Cursor: no documented lifetime; can be reset at any time (`reset`: maintenance, a moved app folder): then a full
  pass matched by `id` ([connection-index](connection-index.md)). `list_folder/longpoll` (host
  `notify.dropboxapi.com`, not in the rule hosts) is not used: the scheduled task polls `continue`.
- Rate limits are not published (per linked user, per team for team apps): 429 `too_many_requests` with
  `Retry-After` (→ `RateLimited`); `too_many_write_operations` for parallel writes in one namespace.

## Tests

- Tests never call Dropbox: `RecordedDropbox` is the transport given to `OutboundPolicy` (answers by path, keeps the
  requests). The client and specs never name a Dropbox host (dotless fake hosts).
- Before a release with connections, a person with an account runs
  [agents/tools/dropbox-check](../../tools/dropbox-check/README.md) on a scratch install.
