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
- Security events (failed or refused sign-ins, lockouts, wrong codes, recoveries, changes of what runs) are logged
  with `LogSecurityWarning` ([logs.md](../features/logs.md)), with the name and address, never a secret.

## Audit (2026-10-16)

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

- **Session:** brute force (direct, via proxy, spoofed `X-Forwarded-For`), locking another's account, name enumeration
  (timing, lockout, disabled messages), tampered cookies, stamp change ends sessions and links.
- **Local only:** setup without code and `api/session/start` from another address, through a local proxy
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
  junctions out of the root and onto the data folder (also to write there: `FileRouteTests`); hard links in duplicate
  removal; `.sfe`, `.SFE.`, `ChildDir/../.sfe` and its 8.3 name.
- **Folder rules:** a hidden folder through case variants, `Private.`, `PRIVAT~1`, a junction or link from a visible
  folder (raw, listing, search after a scan), a path-only folder's own files, counts (tags, duplicates, listings),
  covers, siblings, quick links, scan status, a shared link made before the rule, the editor's output folder,
  duplicate removal keeping a hidden copy; a user in two groups (rights stay with the group granting them).
- **Content:** `.html`/`.svg`/`.xml`/polyglot files via raw, shared and download; `<script>` in file, folder and tag
  names; external player and data server URLs (`javascript:` variants).
- **Archives** ([archive-viewer.md](../features/archive-viewer.md), [archive-reading.md](../features/archive-reading.md)):
  names leaving the archive (`../`, `C:\`, `\\server`, `...`), links in tar/7z/rar, a directory claiming more entries
  than it holds, an entry inflating past its declared size, overlapping or outside offsets, a ratio past the limit, an
  `.html`/`.svg` entry opened (attachment, sandboxed), a token with `..` or for a hidden or protected folder; passwords:
  guessing through `unlock` (10 a minute per caller), another account's or space's unlock id, an unlock after sign-out
  or a change of the archive, the password in logs, errors, answers or URLs (only the unlock id is), encrypted names
  written under the cache (`ExploreRouteTests` scans it).
- **Flash movies** ([flash-viewer.md](../features/flash-viewer.md)): a movie (Ruffle runs it) calling `api/` with
  `URLLoader`, custom headers (`X-SFE-Request`) or `getURL("javascript:…")`; `navigateToURL`, `_blank`, `_top`;
  `ExternalInterface`; the frame opened alone (`flash/player.html`: still sandboxed by its CSP) or framed by another
  site; a flash link's entry with `..`, encoded separators, a hidden subfolder, `.sfe`; an external player's token on
  the flash route and back; links of a disabled account, after a rule, with `Player:Flash` off. Checked 2026-10-25: the
  frame sends no cookie (`Origin: null`), `javascript:`/`_blank` do nothing (`FlashRouteTests`, spike in headless Edge).
- **CSRF/framing/rebinding:** cross-site POST without the header, framing the app, foreign `Host`.
- **DoS:** floods of thumbnails/probes as a guest, `*` searches, big covers requests, parallel editor uploads, streams.
- **Secrets at rest:** backup content, log store and log files, permissions of `data/`.
