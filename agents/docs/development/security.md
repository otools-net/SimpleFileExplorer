# Security: rules, audit and attack vectors

Code: `src/Sfe.Api/Security/`, `Program.cs`, `Endpoints/LibraryEndpoints.cs`, `Endpoints/InstallationEndpoints.cs`,
`Core/Paths/LibraryPaths`, `Core/Access/`. Sessions and the pipeline: [sessions-and-pipeline.md](../architecture/sessions-and-pipeline.md);
rights: [rights.md](../features/rights.md).

The app is exposed to hostile visitors (guests, users of other spaces, anyone on the network, files dropped in the
library by others). Keep these rules when changing it.

## Rules

- A file of the library is **content of strangers**: never serve it so that the browser runs it on the app's origin
  (HTML, SVG, XML). `LibraryEndpoints.ServeFile` sandboxes every file and forces a download for types outside the
  media allow-list.
- Nor so that a page of the app loads it as its own script or style (`script-src 'self'` would accept it): a route
  serving library content takes its type from `LibraryEndpoints.ContentTypeOf` (`LibraryContent.SafeType`) and is
  marked `.NeverRunByPages()` (`.NeverRunByPagesButStyled()` for a book's files).
- ffmpeg gets a library file only through `Ffmpeg.LibraryInputAsync` (disk only, real media formats only), never a
  bare `-i`.
- A limit per network address uses `AddressKeys.Of` (an IPv6 visitor is its /64), never `RemoteIpAddress` whole.
- A file holding a secret is written with `SecretFiles.WriteAllText`, its folder made with
  `SecretFiles.CreatePrivateDirectory`.
- An anonymous answer never carries an exception's message, a path or a log line: a fixed sentence, the detail in
  the log (or behind the setup code, as `GET api/migration`).
- Everything that runs a program or decides where data lives (ffmpeg, root, database) is set by
  whoever runs the server, never by a right alone.
- Secrets (setup code, Data Protection keys, link tokens, passwords) never go to the log store, error messages, or a
  folder served by the app.
- Every route names its right; every check of a path goes through `LibraryPaths` (lexical) **and** the served file
  must not be one of the server's own files (links can lead out of the root).
- Every route taking a library path applies the folder rules (`FolderChecks`, [folder-access.md](../features/folder-access.md)):
  answer 404 for what the subject doesn't see (never tell it apart from missing), check the canonical path and where it
  really leads, filter index queries with `Visible()` and re-check shown items with `Seen()`; mark the route
  `.FolderScoped(...)` (a test fails otherwise).
- A route that **writes** to the library also refuses (404) a place whose real path is among the server's files or in
  SFE's work folder (`FileEndpoints.Protected`), refuses names the system reads as another one, never replaces
  anything, and needs its right throughout a folder it changes as a whole (`AllowsThroughout`)
  ([new-folder-and-rename.md](../features/new-folder-and-rename.md)). `.sfe` at the root is never reachable
  ([work-folder.md](../features/work-folder.md)).
- New expensive routes (ffmpeg, full scans of the index) get a limit per caller.
- What only "this computer" may do (setup without code, fast sign-in) goes through `LocalVisitor.IsOnThisComputer`
  (loopback, not forwarded, loopback `Host`), never a bare loopback check ([local-start.md](../features/local-start.md)).
  It is never enough by itself to skip a secret (tunnels and other users of the computer are loopback visitors too):
  the visitor also shows a token only who started SFE has (`LocalSetupLink`, `FastSignIn`).
- A route that writes to the library asks `FileEndpoints.ProtectedFromWrites` (jobs: `IFileJobAccess.IsReadOnly`),
  not `Protected`: it also refuses a place outside the library reached through a link when
  `Sfe:Security:LinksOutsideLibrary` is `ReadOnly` (`LibraryLinks`). Reading routes keep `Protected`.
- A folder compared with a configured one (allowed roots, link targets) is compared where it really is
  (`LibraryPaths.RealPath`), never by spelling.
- A route that lets someone into an account or the server, or handles tokens, is marked `.SessionOnly()`: an access
  token may not call it ([access-tokens.md](../features/access-tokens.md); `AccessTokenRouteTests` checks the list).
- Security events (failed or refused sign-ins, lockouts, wrong codes, recoveries, changes of what runs) are logged
  with `LogSecurityWarning` ([logs.md](../features/logs.md)), with the name and address, never a secret.

## Audit (2026-10-16)

A second audit (2026-11-11, findings A1–A13; fixed but A13 and what each finding lists as left) is in
[security-audit-2026-11.md](security-audit-2026-11.md).

Already sound: path normalisation, PBKDF2-SHA512 210k, constant-time code compare, `X-SFE-Request` + SameSite=Lax
(no CORS), rights test on every route, tenant consistency between rights and services, space administrators can't
touch server administrators, web settings exclude executables/paths, ffmpeg arguments as a list, no raw SQL from
input, LIKE escaped, no `innerHTML` in the client.

| # | Severity | Finding | Status |
|---|---|---|---|
| 1 | High | Library files (`.html`, `.svg`, `.xml`) served inline on the app origin through `files/raw` and `files/shared` → stored XSS → admin takeover | fixed 1.47.0: `ServeFile` sandbox CSP + attachment outside media/text (`OpensInline`) |
| 2 | High | Setup code: installation routes accept it forever, set ffmpeg to any file (RCE) / root to any folder; code printed into the log store | fixed 1.47.0: server admin session + code after setup; ffmpeg name and not in a library; code to console only, renewed by `RenewSetupCode` |
| 3 | High | Windows service runs as Local System; systemd example without hardening | docs only (1.47.0): `NT SERVICE\SFE` steps, systemd hardening. Installer default unchanged (changing it would break existing installs' file access) |
| 4 | Medium | Links in the library (symlink/junction) or a root holding the data folder expose `data/keys` (forged cookies), setup code, databases | fixed 1.47.0: `ServerFiles` on raw, shared, links (real paths, 8.3 names). Hard links to those files not caught |
| 5 | Medium | No forwarded headers: behind a proxy the cookie is never Secure and all sign-ins share one rate-limit bucket | fixed 1.47.0: forwarded headers (loopback + `TrustedProxies`), `RequireHttps` |
| 6 | Medium | Lockout per account only: anyone locks the administrator out; lockout message tells which names exist | fixed 1.47.0: `SignInThrottle` per name + address, account lock at `AccountLockoutFailures` |
| 7 | Medium | No security headers (CSP, nosniff, frame-ancestors, Referrer-Policy) | fixed 1.47.0: `SecurityHeaders` (checked in headless Edge: no violations) |
| 8 | Medium | `AllowedHosts: *` → DNS rebinding | documented (`AllowedHosts` in installation.md); default unchanged (a fixed list would break existing installs' host names) |
| 9 | Low–Med | No per-caller limit on thumbnails/search/probe; editor buffers 200 MB in memory | partly 1.47.0: `RequestLimits` per account/address; the editor still buffers (1 upload at a time per caller) |
| 10 | Low | Anonymous `api/setup` tells the code file path; errors carry server paths | partly 1.47.0: code file path hidden after setup; `ex.Message` in editor/duplicates answers still open |
| 11 | Low | Shared links last up to 7 days, can't be revoked one by one | open (lowering the maximum would invalidate saved settings: needs a migration) |
| 13 | Medium | A distributed guesser can keep the administrator's account locked; recovery needed a config edit + restart | fixed 1.47.0: `AccountRecovery`, one-time console code (not the setup code: no standing secret) |
| 12 | Info | Library files that aren't media (PDF…) now download instead of opening in a tab | accepted trade-off of #1 |

## Attack vectors to test

- **Session:** brute force (direct, via proxy, spoofed `X-Forwarded-For`, a new address of one IPv6 /64 per try),
  locking another's account, a flood of names filling `SignInThrottle`, name enumeration
  (timing, lockout, disabled messages, a locked account with a wrong password), tampered cookies, stamp change ends sessions and links.
- **Local only:** setup without code from this computer without the setup link, with a wrong one, and with the
  right one from another address; setup without code and `api/session/start` from another address, through a local proxy
  (`X-Forwarded-For`), with a foreign `Host` (DNS rebinding), after an account exists; a start link reused, late, or
  after a second server administrator.
- **Local mode** ([server-modes.md](../features/server-modes.md)): any route but setup/session/installation/migration
  from another computer (also via a local proxy or a foreign `Host`); a non-administrator signing in, or keeping a
  session made before the switch or before losing the Administrators group; guest access with `AnonymousGuests` on in
  the configuration; switching External → Local from the web. Known gaps: an external player's file link made before
  the switch works until it expires (`LibraryEndpoints` checks its claims, not the session), and a file job started
  before keeps running (`FileJobAccess`).
- **External mode:** `PUT api/installation` with a root or ffmpeg (alone, with `Mode: External`, with a forged lock
  state), a space created outside `AllowedRoots`, the setup without a code on a server configured External, a fast
  sign-in link on External.
- **Setup/installation/migration:** every route after setup with and without code and session; ffmpeg path to a
  non-ffmpeg program or a file of a library; root at `C:\` or `/` or around the data folder; old-code tokens; Recover left on.
- **Authorization:** each route × anonymous/guest/user/space admin/server admin; `X-SFE-Tenant` and cookie mismatches;
  space admin vs server admin and shared accounts; guest-forbidden rights.
- **Paths:** `..`, encoded separators, `\`, `C:`, `::$DATA`, `CON`, trailing dots/spaces, NUL, long names; links and
  junctions out of the root and onto the data folder (also to write there: `FileRouteTests`); with
  `LinksOutsideLibrary: ReadOnly`, every writing route through a junction leaving the root (`LibraryLinkTests`); a
  space rooted through a link in an allowed root (`TenantTests`); hard links in duplicate
  removal; `.sfe`, `.SFE.`, `ChildDir/../.sfe` and its 8.3 name.
- **Folder rules:** a hidden folder through case variants, `Private.`, `PRIVAT~1`, a junction or link from a visible
  folder (raw, listing, search after a scan), a path-only folder's own files, counts (tags, duplicates, listings),
  covers, siblings, quick links, scan status, a shared link made before the rule, the editor's output folder,
  duplicate removal keeping a hidden copy; a user in two groups (rights stay with the group granting them).
- **Content:** `.html`/`.svg`/`.xml`/polyglot files via raw, shared and download; a library `.js`, `.css` or `.wasm`
  loaded as `<script>`, worker or style sheet (`Sec-Fetch-Dest`, `LibraryContentTests`); a playlist, concat script or
  `.avs` named as a video given to ffmpeg (`FfmpegInputTests`); pictures of another site under the app's policy; `<script>` in file, folder and tag
  names; external player and data server URLs (`javascript:` variants).
- **Archives** ([archive-viewer.md](../features/archive-viewer.md), [archive-reading.md](../features/archive-reading.md)):
  names leaving the archive (`../`, `C:\`, `\\server`, `...`), links in tar/7z/rar, a directory claiming more entries
  than it holds, an entry inflating past its declared size, overlapping or outside offsets, a ratio past the limit, an
  `.html`/`.svg` entry opened (attachment, sandboxed), a token with `..` or for a hidden or protected folder, an entry
  deep in a solid block or a compressed tar opened again and again (`MaxSkippedMegabytes`, `ArchiveReaderTests`), reads
  of many archives started by one caller (30 a minute, `CatalogBuildLimits`), a 7z whose list is huge (held whole by
  SharpCompress: refused past `MaxSevenZipListMegabytes` from its declared size, before reading); passwords:
  guessing through `unlock` (10 a minute per caller), another account's or space's unlock id, an unlock after sign-out
  or a change of the archive, the password in logs, errors, answers or URLs (only the unlock id is), encrypted names
  written under the cache (`ExploreRouteTests` scans it); extracting: every name above written outside the target,
  a link followed by a later entry (`escape` then `escape/x`), a file in the way of a folder, a lying size or CRC left
  as a whole-looking file, the disk filled past the margin, extracting into `.sfe` or a server-files junction, an
  archive holding `.sfe/` or `SFE~1/` extracted at the root (never placed nor replacing the work folder), a
  password in the job's view or the log (`ArchiveExtractorTests`, `ExtractJobTests`); compressing: a hidden folder
  reached through its parent, a link followed, the zip put into itself, the server's files or a work folder in the zip,
  the password in the job's view or the log (`CompressJobTests`, `FileJobRouteTests`).
- **Flash movies** ([flash-viewer.md](../features/flash-viewer.md)): a movie (Ruffle runs it) calling `api/` with
  `URLLoader`, custom headers (`X-SFE-Request`) or `getURL("javascript:…")`; `navigateToURL`, `_blank`, `_top`;
  `ExternalInterface`; the frame opened alone (`flash/player.html`: still sandboxed by its CSP) or framed by another
  site; a flash link's entry with `..`, encoded separators, a hidden subfolder, `.sfe`; an external player's token on
  the flash route and back; links of a disabled account, after a rule, with `Player:Flash` off. Checked 2026-10-25: the
  frame sends no cookie (`Origin: null`), `javascript:`/`_blank` do nothing (`FlashRouteTests`, spike in headless Edge).
- **Connections:** see [Connections](#connections) below.
- **Access tokens:** see [Access tokens](#access-tokens) below.
- **CSRF/framing/rebinding:** cross-site POST without the header, framing the app, foreign `Host`.
- **DoS:** floods of thumbnails/probes as a guest, `*` searches, big covers requests, parallel editor uploads, streams.
- **Secrets at rest:** backup content, log store and log files, permissions of `data/`.

## Connections

External storage linked to an account, a library or the server ([connections.md](../features/connections.md)). Rules
kept by the code: a connection one may not use answers 404 (never told apart from missing), never opened by an
administrator for another account; credentials only protected (Data Protection, purpose `SFE.Connections`); every
outbound call through `OutboundPolicy` ([connection-providers.md](../features/connection-providers.md)); folder rules
checked before any provider call ([connection-folder-rules.md](../features/connection-folder-rules.md)); remote
content served under the library's sandbox (`RemoteFileResult`).

| Vector | Tested by |
|---|---|
| Another account's private connection by id (browse, raw, properties, write routes, uploads, transfers, retry): 404; only removed by the server's administrators, never opened | `ConnectionFileRouteTests.NothingOutsideTheRootNorAnotherAccountsConnectionIsReached`, `ConnectionWriteRouteTests`, `ConnectionUploadRouteTests.AnUploadIsItsOwnersOnly`, `TransferRouteTests`, `TransferRetryTests.AnotherAccountFindsNothingToRetry`, `ConnectionRouteTests.AnotherAccountsConnectionIsNotFound…`, `ConnectionAccessTests` |
| A library-level connection from another library; a server-level one in a library it isn't offered to | `ConnectionAccessTests`, `ConnectionRouteTests.AServerConnectionIsOfferedThenGivenToTheLibrarysGroups` |
| Connections turned off, a kind or level taken out of the settings | `ConnectionAccessTests.TheServerSettingsTurnConnectionsOff`, `ALevelTakenOutOfTheSettingsIsNotUsed` |
| OAuth: another account's state, a state reused (spent by any attempt), late (10 minutes), forged; a code completed twice; the callback signed out or refused; an authorize at a level the account may not create; a right lost mid sign-in | `ConnectionSignInFlowTests`, `ConnectionSignInRouteTests` |
| Signing in again by someone who may not manage it; another provider account told | `ConnectionSignInFlowTests.OnlyWhoMayManageItSignsAConnectionInAgain`, `SigningInAgainKeepsTheConnectionAndTellsAnotherAccount` |
| Paths: `..`, encoded separators, names the provider folds (case, trailing dots), above the connection's root, too long | `RemotePathsTests`, `ConnectionFileRouteTests`, `ConnectionWriteRouteTests.TransfersRefuseFoldersIntoThemselvesPathsOutsideAndOtherWords`, `ConnectionUploadRouteTests.BadNamesAndTargetsLeavingTheRootAreRefused` |
| A read-only connection or a Read group level on every writing route and on both sides of a transfer | `ConnectionWriteRouteTests.AConnectionOnlyReadIsNotChanged…`, `ConnectionUploadRouteTests.AConnectionOnlyRead…`, `TransferRouteTests.…OneOnlyReadIsNotWritten`, `ConnectionFolderAccessRouteTests.AReadOnlyConnectionCapsEveryRule` |
| A hidden folder through listing (totals, later pages), raw, properties, write routes, uploads (also hidden meanwhile), zips (at both steps), transfers; a hidden name taken; a job after a rule changed | `ConnectionFolderAccessRouteTests`, `ConnectionFolderAccessJobTests`, `RemoteListingFilterTests`, `RemoteTransferEndpointTests` |
| A user in two groups: a right of one group added to the level of another | `ConnectionFolderAccessRouteTests.ARightCountsOnlyAtTheLevelOfTheGroupGrantingIt`, `ConnectionPoliciesTests` |
| A folder rule escaped by a move (more people let in, another library's rules, moved out, deleted, renamed outside SFE) | `ConnectionRuleFollowingRouteTests`, `ConnectionPoliciesTests` (id match) |
| Secrets (refresh tokens, app secret, PKCE verifier) in answers, settings, logs, backups, URLs | `ConnectionProviderRouteTests.TheSecretIsWrittenButNeverReadBack`, `ConnectionCredentialsTests` (other keys read nothing); logs, backups and the job view: by review only |
| Remote content: `.html` / `.svg` served from a connection (sandboxed, attachment); a script loaded by a page | `ConnectionFileRouteTests.AFileIsServedWholeOrInPartSandboxed` (`.html`; the rest shares `FilePolicy` with the library) |
| Floods: listings and reads as one caller; a provider asking to wait; a transfer of millions of items (bounded memory: pages, depth first) | `ConnectionGatesTests`, `ConnectionFileRouteTests.WhileTheProviderAsksToWaitNobodyCallsIt`, `TransferCallsTests`; per-caller limit `RequestLimits.Connections` and the transfer's memory: by review only |
| Outbound: a host outside the provider's rule, http, a redirect; later providers: an address typed by a person pointing at loopback or the LAN, a redirect to one | `OutboundPolicyTests` |
| The client naming a provider's host (fully local) | `ApiTests.WebClientHasNoExternalResources` |
| Local mode: connections for the administrators on this computer, nothing new answering other computers | not automated (the routes sit behind `RequestGuard` like every other) |
| An access token limited in rights or library using connections | by review only: `ConnectionJobAccess` checks each item with the token's rights (`ConnectionJobSubject.Token`); routes go through `CurrentAccess` like every other |

## Access tokens

Personal access tokens (`Authorization: Bearer`, [access-tokens.md](../features/access-tokens.md)). Tests:
`AccessTokenAuthenticationTests` (Auth), `AccessTokenRouteTests` (Route), `AccessTokenServiceTests` (Service),
`AccessTokenJobTests` (Job).

| Vector | Tested by |
|---|---|
| A token of a disabled or deleted account | Auth `ATokenOfADisabledOrDeletedAccountStopsWorking` |
| An expired or revoked token, a wrong secret, a malformed token: all the same 401, no `Set-Cookie` | Auth `WrongExpiredAndRevokedTokensAreRefusedAlike`, Service `WrongMalformedExpiredAndRevokedTokensFindNothing` |
| A right the account lost since the token was made; a right beyond the token's list | Auth `ATokenHasItsOwnRightsAmongWhatItsAccountHoldsNow` |
| A token limited to a library sent with another library's `X-SFE-Tenant`; an unknown library; an account removed from the token's library | Auth `ATokenStaysInItsSpaceAndTheSpacesItsAccountReaches` |
| A token on a session-only route: making or listing tokens, revoke all, password, sign-in, installation, setup | Auth `RoutesForSessionsOnlyRefuseTokens`; Route `EveryRouteLettingSomeoneInOrHandlingTokensIsForSessionsOnly` (the list both ways) |
| Guessing: wrong tokens from one address, from one IPv6 /64 (the address is then refused every token) | Auth `WrongTokensAreThrottledPerAddressAndTheSecretIsNeverLogged` |
| A known lookup with a wrong secret: same answer as an unknown lookup | Auth `WrongExpiredAndRevokedTokensAreRefusedAlike` (same 401); same timing by review only (`FixedTimeEquals`, an unknown lookup compared with a zero hash) |
| Local mode: a token from another computer, a non-administrator's token | Auth `ALocalServerTakesTokensOfItsAdministratorsOnItsOwnComputerOnly` |
| A request with a cookie and a bearer (the bearer decides; a wrong bearer never falls back to the cookie) | Auth `WithACookieAndABearerTheBearerDecides` |
| A bearer on `<img>`-style GET routes (raw, thumbnails) | by review only: browsers never send a bearer by themselves; GET routes take it like `config` (Auth `ABearerActsAsItsAccountWithoutCookieNorRequestHeader`) |
| The token in the query (`?access_token=`) | Auth `ABearerActsAsItsAccountWithoutCookieNorRequestHeader` (refused) |
| The secret in the log (wrong tokens, audit lines) | Auth `WrongTokensAreThrottled…`, `ChangesMadeWithATokenAreLoggedWithTheAccountAndTheToken` |
| The secret in the database or backups (only its SHA-256 kept) | Service `TheSecretIsShownOnceAndOnlyItsHashIsKept`; backups by review only (they copy the database) |
| The secret in problem answers | by review only (`ProblemLog` names the token by `BearerTokens.Describe`, never its secret) |
| A space administrator revoking a server administrator's token, or another space's | Route `AdministratorsSeeAndRevokeTheTokensOfTheirSpacesOnly` |
| A job started with a token: an item beyond the token's rights; the token revoked while it runs | Job `ATokenWithoutTheRightToDeleteReplacesNothing`, `RevokingTheTokenStopsTheItemsLeftButNotASessionsJob` |
| Tokens switched off: none made, none accepted | Route `TokensTurnedOffAreNeitherMadeNorUsed` |
| More tokens than allowed; a longer life; rights or a library the account doesn't hold | Service `AnAccountHoldsAtMostTheTokensAllowed`, `ATokenLivesNoLongerThanAllowed`, `ATokensRightsAndLibraryAreAmongTheAccounts` |
| An access token limited in rights or library using connections | see [Connections](#connections) (by review only) |
| External player and Flash links made with a token | known gap: they carry the account, not the token (see [access-tokens.md](../features/access-tokens.md#gotchas)) |
