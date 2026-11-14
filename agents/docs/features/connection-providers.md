# Connection providers: adding a kind of storage

Code: `src/Sfe.Core/Connections/` `IConnectionProvider`, `IRemoteStorage`, `ProviderCapabilities`, `RemoteItem`,
`RemoteContent`, `RemoteStorageException`, `RemoteErrorKind`, `RemotePaths`, `OutboundPolicy`, `OutboundRule`,
`OutboundGuard`, `ConnectionProviders`, `IRegisteredApp`, `SignInModels`; registration in
`Sfe.Core/ServiceCollectionExtensions.cs` (`AddSfeAccess`). Tests: `FakeStorageTests` (the contract),
`OutboundPolicyTests`, `RemotePathsTests`; `tests/Sfe.Tests/Infrastructure/Connections/FakeProvider.cs` (with
`FakeAccount`, `FakeStorage`). The one built: [dropbox](dropbox.md). The model and rules: [connections](connections.md).

**Done when a kind is added by writing one folder of code** (`Connections/<Kind>/`) and registering it: no route,
client component or table changes.

## Steps

1. `Connections/<Kind>/<Kind>Provider.cs` implementing `IConnectionProvider`, registered with
   `services.AddSingleton<IConnectionProvider, …>()` in `AddSfeAccess`; `ConnectionProviders` (`Find(kind)`,
   `Offered(options)`, `IsAllowed(kind, options)`) picks it up. Its settings (an app key…) in `ConnectionSettings`
   ([settings-new-key](settings-new-key.md)); a secret through `IRegisteredApp` and `ProviderSecrets`, never in
   `EditableSettings` (the settings are sent back whole).
2. `<Kind>Storage` implementing `IRemoteStorage`; an error mapper turning every failure into one
   `RemoteStorageException` with a `RemoteErrorKind`.
3. Its tests: copy what `FakeStorageTests` asserts, against recorded answers given as the transport of
   `OutboundPolicy` (as `RecordedDropbox`); never the real service.
4. A manual check list with a real account under `agents/tools/<kind>-check/` (as [dropbox](dropbox.md)'s).
5. User docs: its setup in `docs/administration/connections.md`.

## `IConnectionProvider`

- `Kind` (stored with each connection, named in `Sfe:Connections:Providers`), `Name`, `Icon` (a client icon name),
  `Capabilities`.
- `SignInMethods(options)`: the ways that work with the server's settings, preferred first; **empty = not configured,
  so not offered**. Kinds: `OAuthRedirect`, `OAuthCode`, `Password`, `Key`, each with its fields described as data
  (label, type text / secret / address): the client's form needs no code per provider.
- Sign-in in two calls: `BeginSignInAsync` answers the grant at once (a password kind) or an address plus a `Pending`
  secret **the server keeps** (`PendingSignIns`; a PKCE verifier); `CompleteSignInAsync` takes it back with what the
  person brought (code, fields). A later second step (SFTP's host key to accept) fits the step's `next`.
- `Open(credentials, rootPath, options)` gives an `IRemoteStorage` seen from `rootPath`. Providers get
  `ConnectionOptions` as an argument, never from DI.
- `TestAsync(options)`: the administrator's **Test** button; never throws for an answer of the provider.

## `IRemoteStorage`

- **Path-addressed**, relative to the connection's root folder: SFE's routes, rules and transfers speak paths. A
  provider of ids (Google Drive) resolves paths itself (a bounded cache per connection) and shows duplicate names
  apart; `RemoteItem.Id` carries the provider's id (opaque) for folder rules
  ([connection-folder-rules](connection-folder-rules.md)) and the index plan. `IAsyncDisposable`: the routes dispose
  it with the response.
- `ListAsync(path, cursor)` in pages (unordered is fine: [connection-files](connection-files.md) sorts a snapshot),
  `GetAsync` (null = missing), `OpenReadAsync(path, range)` answers `RemoteContent` (stream, the range actually
  served, total length: a provider without ranges serves the whole file), upload session `BeginUploadAsync` /
  `AppendUploadAsync(upload, offset, part)` / `FinishUploadAsync(upload, target)` with a conflict mode,
  `CreateFolderAsync`, `MoveAsync` / `CopyAsync` (`RemoteConflict`), `DeleteAsync`, `ThumbnailAsync`,
  `ChangesAsync(cursor)`, `SpaceAsync` (null = doesn't tell).
- A provider with one `PUT` (WebDAV) keeps the parts in a temporary file of the server until `Finish`
  (`UploadsInParts` false: the server's disk is used, its free space checked).
- `RemoteItem.Downloadable` is false for files without content (Dropbox Paper, Google Docs): zips list them as left
  out.

## Capabilities (`ProviderCapabilities`)

`RangedReads`, `UploadsInParts`, `MaxUploadPart`, `ServerSideCopy`, `ServerSideMove` (two flags: SFTP moves but
cannot copy), `Thumbnails`, `ChangeCursor`, `ContentHash` (a name: `dropbox`, `md5`, `sha256`; null = none; the
transfers verify with it, `ContentHashes`), `OwnTrash`, `CaseInsensitiveNames` (`RemotePaths.ComparisonOf`: paths
and rules compare by it), `UniqueNames`, `FolderDates`, `FreeSpace`. The server sends them in `ClientConfig` and each
listing; the client hides what a place can't do from them (`atPlace`).

Checked on paper (2026-11-13) against WebDAV, SFTP and Google Drive:

| Need | Dropbox | WebDAV | SFTP | Google Drive |
|---|---|---|---|---|
| Address | path | path | path | id; names not unique in a folder |
| Listing | pages + cursor, unordered | whole folder (`PROPFIND` depth 1) | whole folder | pages, by parent id |
| Ranged read | through a temporary link | `Range` (most servers) | seek | `Range` |
| Upload | sessions, parts up to 150 MB | one `PUT` | write at offset | resumable session |
| Server-side copy / move | both | both | move only (rename) | both |
| Changes | cursor | none (`getetag` per folder) | none | page token |
| Hash | `content_hash` (its own) | sometimes | none | md5 / sha256 |
| Thumbnails | yes | no | no | yes |
| Own trash | yes | no | no | yes |
| Names | case-insensitive | server's | server's | anything, duplicates |
| Folder dates | none | yes | yes | yes |
| Files without content | Paper docs (`is_downloadable`) | none | none | Docs, Sheets |
| Sign-in | OAuth (redirect or code) | address + password | address + password or key, **host key to confirm** | OAuth |

## Errors (`RemoteErrorKind`)

`NotFound`, `Exists`, `Refused`, `CredentialsEnded` (the connection is marked needs reconnecting), `RateLimited`
(with `RetryAfter`: the gates wait it out), `TooLarge` (also storage full), `Unavailable` (any failure that may pass,
including an answer of an unexpected shape: a proxy's page), `InvalidName` (per item), `NotSupported`, `WrongOffset`
(with the provider's `Offset`, so an upload goes on from there). No provider exception crosses `IRemoteStorage`. The
routes' problem types: [connection-files](connection-files.md).

## Outbound calls (`OutboundPolicy`)

- `OutboundPolicy.CreateClient(new OutboundRule(hosts))` is the **only** maker of a provider's `HttpClient`: https
  only, hosts of the rule (`*.` wildcards) refused before any connection, redirects never followed by themselves (a
  provider following one asks again and is checked again), 10 s to connect, 60 s to start answering
  (`ConnectTimeout`, `AnswerTimeout`): a server without internet answers `Unavailable` and never holds a request.
- For hosts typed by people (WebDAV, FTP, SFTP later): a rule without `Hosts`; the address a name resolves to is
  checked when connecting: no loopback, link-local or private ranges unless the rule's `PrivateAddresses`
  (`AllowPrivateAddresses`, configuration only). `OutboundGuard` is the handler refusing what the rule doesn't allow
  and giving up after `AnswerTimeout`.
- Tests replace the transport: `new OutboundPolicy(() => handler)`. The client never names a provider's host
  (`ApiTests.WebClientHasNoExternalResources` scans the bundle and specs: dotless fake hosts in tests).

## Paths (`RemotePaths`)

- `Normalize` (null = refused) for everything a route receives: the rules of `LibraryPaths` for a remote tree (`/`
  separators, no `..`, never above the connection's root); `IsValidName` for new names; names refused by a provider
  reported per item (`InvalidName`).

## The fake

- `FakeProvider` (accounts by name; code `good:<name>` or password `secret` signs in), `FakeAccount` (`WithFile`,
  `Failing`, `Calls`, `Trash`), `FakeStorage`; `FakeStorageTests` is the behaviour a real provider must show. Suites
  use the fake; only a provider's own tests use recorded answers.
