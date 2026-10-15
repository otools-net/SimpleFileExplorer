# External player links

Code: `src/Sfe.Core/Options/SfeOptions.cs` (`PlayerOptions.IsAllowedExternalUrl`), `src/Sfe.Api/Security/FileLinks.cs`, routes
`POST api/files/links`, `GET api/files/shared/{token}/{name}`; client `ConfigService.externalLink(path)`.

- `Player:ExternalUrl` template (`{url}`, `{encodedUrl}`, else appended) + `ExternalName`, sent as `/api/config`
  `externalPlayer` (null when empty or without `files.external`).
- `ConfigService.externalLink(path)` asks `POST api/files/links` for a token link (`FileLinks`:
  `ITimeLimitedDataProtector` over tenant, user, stamp, path; `Player:LinkLifetimeMinutes`) unless a data server is
  set, then fills the template (`urls.external(fileUrl)`).
- `GET api/files/shared/{token}/{name}` (anonymous) checks the token, the account's stamp and rights, and serves the
  file from the token's library (`TenantRegistry`).
- `PlayerOptions.IsAllowedExternalUrl` refuses `javascript:`/`data:`… schemes (the link runs in every user's page);
  the config endpoint checks it again for values from appsettings.
- The external player's `{url}` is the file's full address, not the data server's ([data-server.md](data-server.md)).
