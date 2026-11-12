# Security audit (2026-11-11): findings and possible fixes

Code: `src/Sfe.Api/Security/`, `src/Sfe.Api/Endpoints/`, `src/Sfe.Api/Program.cs`, `src/Sfe.Core/Access/`,
`src/Sfe.Core/Paths/LibraryPaths.cs`, `src/Sfe.Core/Thumbnails/Ffmpeg.cs`, `build/`, `Dockerfile`.
Rules and the first audit (2026-10-16, findings #1–#13): [security.md](security.md).

Audited at SFE 1.85.1 (`edge`, commit `2263bbb`). Status on 2026-11-11, in the working tree (not committed, not
released; changelog under `## Unreleased`): **fixed** A2 (the installer), A3–A12; **partly** A1 (an opt-in read-only
mode; the default still follows links) and A2 (the service account is unchanged); **open** A13. Each finding below
starts with its status and what is left.

## Summary

- No finding lets a visitor without an account, or a signed-in user, take over the server or read another space
  through the web alone. The findings need something more: access to the file system or the computer, an IPv6
  network, a second weakness, or a particular way of exposing the server.
- The two that matter most (A1, A2) are about what the server's process may touch: links in a library lead it
  anywhere, and the Windows service is Local System.
- Dependencies: NuGet has no known vulnerable package; npm reports one (A12).

| # | Severity | Finding | Needs | Status |
|---|---|---|---|---|
| A1 | Medium | A link (symlink, junction) in a library is followed out of it, for reads **and writes**; only SFE's own files are excluded | someone able to put a link in a library folder | opt-in `ReadOnly` mode; default unchanged |
| A2 | Medium | Windows service: Local System, and `--install-service` accepts a folder other users can write → local user becomes SYSTEM | an account on the computer | installer check done; account unchanged |
| A3 | Medium | Library files are valid scripts for the app's CSP (`script-src 'self'` + `api/files/raw/x.js`) | an HTML injection elsewhere (none found) | fixed |
| A4 | Low–Medium | ffmpeg opens untrusted files with no protocol or format restriction | a crafted file in a library, an old or permissive ffmpeg | fixed (one ffmpeg tested) |
| A5 | Low–Medium | Sign-in limits count per full IPv6 address: a /64 gives unlimited tries, and can empty `SignInThrottle` | IPv6 reachability | fixed |
| A6 | Low | "This computer" = loopback + `Host`: true for a TCP forwarder's visitors and for every user of the computer | a raw TCP tunnel, or a shared computer | fixed (setup); Local mode documented |
| A7 | Low | A locked account answers "locked" before the password is checked: tells the name exists, and faster | — | fixed |
| A8 | Low | Anonymous `GET api/migration` shows the update's raw error and log | a failed update | fixed |
| A9 | Low | Secret files are created, then restricted; Data Protection keys are stored unencrypted | read access to `data/` | fixed (key encryption left, with A2) |
| A10 | Low | `Sfe:Tenants:AllowedRoots` is checked by spelling, not where the folder really is | server administrator + a link in an allowed root | fixed |
| A11 | Low | The app's CSP lets pictures and media load from any site (`img-src http: https:`) | untrusted markup shown under the app's policy | fixed |
| A12 | Low | npm: `zod` 4.6.5 (GHSA-mrhx-c7xc-m3w5); no audit step in the build | — | fixed (no CI step yet) |
| A13 | — | Still open from the first audit: #3, #8, #9, #10, #11 | | open |

## What was done, and what was not

- Read: the pipeline (`Program.cs`, `RequestGuard`, `SecurityHeaders`, rate limits), sessions, sign-in, lockout,
  recovery, setup, installation, update and backup routes, `LibraryPaths`, `ServerFiles`, `FolderChecks`, the routes
  serving files (raw, shared, flash, epub, archive entries), the routes writing files (new, rename, delete, uploads,
  text save), accounts and groups, settings and spaces routes, the ffmpeg calls, the Docker image, the Windows service setup.
- Run: `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev`; greps of the client for
  HTML sinks (`innerHTML`, `bypassSecurityTrust…`, `srcdoc`, `eval`: none in the app's code).
- **Not done:** nothing was attacked on a running server; findings come from reading the code. A1's write path and A3
  are worth a test before fixing (see each).
- **Read only in part or not at all:** archive reading, extraction and compression (`Core/Files/Compression/`,
  `ArchiveExtractor`), the search parser and queries, file jobs (`FileJobs`), trash, tags, tasks and schedules, logs,
  keep alive, the SQL Server path, most of the Angular client (viewers for Markdown, PDF, EPUB, CSV, JSON beyond their
  link handling). The existing tests listed in [security.md](security.md) cover several of these; they were not rerun.

## Found sound

- Path handling: `NormalizeRelative` refuses `.`/`..`, NUL and `:` on Windows; the work folder is refused with its 8.3 name.
- Sessions: cookie HttpOnly, SameSite=Lax, stamp checked on every request; `X-SFE-Request` on every non-GET of
  `/api`; the only GET that changes state is the one-time fast sign-in link.
- Secrets: PBKDF2-SHA512 210k with a dummy check for unknown names, constant-time code compares, 16-character generated
  codes (about 79 bits), link tokens signed and encrypted, tokens kept as hashes or used once.
- Space administrators cannot make or change server administrators (`UpdateUserAsync` keeps the flag under a
  `tenantScope`), nor take in accounts of other spaces.
- Installation routes: server administrator + code after setup; in the image, root and ffmpeg come from the environment.
- Docker image: not root, External mode, no setup without code, example library mounted read-only.
- Files served: sandbox CSP + `nosniff` + download for anything but pictures, video, audio and plain text.
- Client: no HTML sink in the app's code; Markdown links and pictures are rewritten, `javascript:` dropped; external
  player and data server URLs have their schemes checked on the server.

## Findings

### A1 — Links in a library lead the server out of it, also to write (Medium)

- **Status: partly fixed, by choice** (decided 2026-11-11: new installs keep following links, so no upgrade
  step). `Sfe:Security:LinksOutsideLibrary` = `Follow` (default) | `ReadOnly`, configuration only, with
  `Sfe:Security:LinkTargets` as the allow-list. `ReadOnly`: `FileEndpoints.ProtectedFromWrites` (`LibraryLinks`)
  refuses a place whose real path is outside the library on new file/folder, rename, delete, text save and copy,
  uploads (start and each folder on the way), job targets (move, copy, extract, compress) and move sources
  (`IFileJobAccess.IsReadOnly`), the editor (new file and save over), restore from the trash, duplicate removal.
  Tests: `LibraryLinkTests` (a real junction; the two default-mode tests pin that writes still go through).
- **Left:** a `Refuse` mode (reads too: every reading route, listings, thumbnails, streams, the index); the default
  itself; tag-driven and scheduled work was not reviewed for writes (it changes the index, not files); the swap of a
  link between check and write, and hard links, as below.
- `LibraryPaths.TryResolve` is lexical. `FileEndpoints.Protected` and `ServerFiles` refuse only SFE's own files and
  work folder. Folder rules use the real path, but only matter where it is inside the library.
- So `library/link/…`, where `link` leads to `/etc`, `C:\Users\other` or `C:\Windows`, is listed, served, and
  accepted as a target by new file, rename, delete, upload, text save, move/copy, extract. `FileOperations` and
  `Uploads` have no link check of their own. With A2 the service account may write everywhere.
- It is the documented behaviour (security.md: "links can lead out of the root"), and home installs use links to add
  drives to a library. The risk is a library someone else can write to outside SFE (an SMB/NFS share, SFTP, a
  shared computer): one link gives SFE's users whatever the service account reaches.
- SFE itself makes no links (uploads, extraction and copies leave them out), so the web alone is not enough.
- To verify first: an upload and a rename through a junction leaving the root, in `FileRouteTests`.
- Possible fixes:
  - A setting, e.g. `Sfe:Files:LinksOutsideLibrary` = `Follow` | `ReadOnly` | `Refuse`. Refuse or read-only for new
    installs; an upgrade step writes `Follow` for existing ones, which keeps them working, and the administration page
    says so.
  - One helper used by every route (beside `Protected`): the real path must be inside a library root or a folder
    named in an allow-list of link targets.
  - Lesser: at least refuse writes whose real path is outside the root, whatever the setting.
- Related, unchanged: hard links to the server's files are not caught (first audit, #4); checks are by path and the
  file is opened afterwards, so a link swapped in between wins (same precondition as above).

### A2 — Windows service as Local System, installed from any folder (Medium)

- **Status: installer check done** (the scope chosen 2026-11-11); the account is unchanged. `--install-service`
  refuses a program folder where Users, Authenticated Users, Everyone, INTERACTIVE… hold a right that changes files
  (`ProgramFolderAccess`), prints the `icacls` command that closes it, and installs anyway with
  `--allow-open-folder`; `install-service.cmd` passes its arguments on. Tests: `ProgramFolderAccessTests` (access
  lists built in memory and one real folder). **Not tested:** an actual `--install-service` run (needs an elevated
  console) and the `.cmd` wrapper's elevation with arguments.
- **Left:** new services as `NT SERVICE\SFE`; a notice for services already installed from an open folder; only the
  program's folder is examined, not the ones above it.
- `WindowsServiceSetup` creates the service without an account (`sc create` default: Local System), from wherever
  the portable folder was unpacked. Settings and data stay in that folder.
- A folder a standard user can write (Downloads, Desktop, `C:\SFE` made by that user) gives that user SYSTEM:
  replace `Sfe.Api.exe`, or set `Sfe:Thumbnails:FfmpegPath` in `appsettings.json` to any program.
- Local System also widens A1 and A4 to the whole machine.
- Possible fixes:
  - `--install-service` checks the folder's ACL and refuses (or warns and asks) when Users or Authenticated Users may
    write; the documentation says to install under `Program Files` or an administrators-only folder.
  - New installs: create the service as `NT SERVICE\SFE` (virtual account) and grant it the program folder, the data
    folder and the library folder; the first audit left the default alone because existing installs depend on Local
    System's access. Existing services stay as they are; an administration page notice proposes the switch.

### A3 — Library files count as the app's own scripts (Medium, defence in depth)

- **Status: fixed** (both fixes). `LibraryContent.SafeType` serves script, style and WebAssembly types as
  `application/octet-stream` (`LibraryEndpoints.ContentTypeOf`, so raw, shared, flash and archive entries);
  `NeverRunByPages()` on those routes answers 404 to `Sec-Fetch-Dest` script, style, worker…, and
  `NeverRunByPagesButStyled()` on `api/files/epub` (a book's styles still load; its `.js` was already `text/plain`).
  Tests: `LibraryContentTests`. Not checked in a browser (the dev tools test below).
- The app's CSP is `script-src 'self'`. `api/files/raw/{path}` (and `shared`, `flash`, archive entries, EPUB
  entries) serve a `.js` file as `text/javascript` from that same origin. `Content-Disposition: attachment` and the
  answer's own sandbox CSP apply to a file opened as a page, not to one loaded by `<script src>`.
- Anyone who can upload therefore owns a script URL the CSP accepts: any HTML injection found later (a viewer, a
  library's dependency, EPUB chapters, which are rendered under the app's policy) becomes script execution in an
  administrator's session. No injection was found; the CSP just stops being the second barrier it is meant to be.
- To verify: a page of the app with `<script src="api/files/raw/x.js">` added by hand in the dev tools runs it.
- Possible fixes (both are cheap and change nothing for users):
  - In `LibraryEndpoints.ServeFile` and the entry routes, serve script types (`text/javascript`,
    `application/javascript`, `application/ecmascript`, `application/wasm`, `text/css` where no viewer needs it) as
    `application/octet-stream`: with `nosniff` the browser refuses to run them.
  - Refuse (404) library-file requests whose `Sec-Fetch-Dest` is `script`, `style`, `worker`, `sharedworker`,
    `serviceworker`, `embed` or `object`.

### A4 — ffmpeg on untrusted files, unrestricted (Low–Medium)

- **Status: fixed.** `Ffmpeg.LibraryInputAsync` builds the input of the four calls on library files:
  `-protocol_whitelist file` and `-format_whitelist` with every demuxer the binary lists (`ffmpeg -demuxers`, read
  once) except those that follow what the file says (`FfmpegDemuxers.Refused`: concat, hls, dash, sdp, rtsp, imf,
  AviSynth/VapourSynth scripts, devices). All real formats stay, so no table per `FileTypes` is needed. Checked first
  with the real ffmpeg: the protocol list alone still let a `concat` script named `.mp4` open another file of the
  library. Tests: `FfmpegInputTests` (need `SFE_TEST_FFMPEG`).
- **Left:** tested only with the 2026-10-12 Windows build; the oldest supported ffmpeg and the Docker image's are
  not (an ffmpeg without `-demuxers` gets the protocol list only, with a warning in the log). The hardening notes for
  the service account and the systemd unit are not written.
- `ThumbnailService`, `MediaDimensions`, `TrickplayService`, `VideoStreamer` run `ffmpeg -i <file>` on whatever is
  in the library, picked by extension. ffmpeg chooses the demuxer from the content: a file named `.mp4` may be a
  playlist (HLS, concat, DASH) naming other files or URLs.
- Recent ffmpeg limits this itself (protocol whitelist of local playlists, extension checks); older or
  distribution builds vary, and the administrator chooses the binary. The outcome ranges from nothing to requests made
  from the server (SSRF) or frames of another local media file in a thumbnail.
- ffmpeg's decoders are also the largest native attack surface of the server, with its account's rights (A2).
- Possible fixes:
  - Add `-protocol_whitelist file` before every `-i` of a library file (one place: a helper in `Ffmpeg` building the
    input arguments), and `-format_whitelist` with the demuxers of the configured `FileTypes`; test against the oldest
    ffmpeg supported and the one in the Docker image.
  - Hardening notes for the service account (A2) and the systemd unit (no network for the ffmpeg children is not
    possible per child; `ProtectSystem`, `ProtectHome` and a read-only library where it fits).

### A5 — Sign-in limits per full IPv6 address (Low–Medium)

- **Status: fixed.** `AddressKeys.Of` (IPv4 whole, IPv6 as its /64, IPv4-mapped unwrapped) is the key of the
  `sign-in` rate limit, of `SignInThrottle` and of `RequestLimits.Caller`; a full `SignInThrottle` drops its oldest
  tenth, entries waiting out a lockout last, instead of everything. Tests: `SignInLimitTests`.
- The `sign-in` rate limit (`SecurityServiceCollectionExtensions`), `SignInThrottle` (name + address) and
  `RequestLimits.Caller` use `RemoteIpAddress` whole. An IPv6 customer has at least a /64: each try comes from a new
  address, so neither limit ever applies.
- What remains is the account lock (`AccountLockoutFailures`), which the attacker then triggers at will: a
  server administrator is locked out until the recovery code is used; other accounts until an administrator unlocks them.
- `SignInThrottle.Trim` clears every entry at 50,000: the same attacker empties everyone's counters.
- Possible fixes: one helper giving the limit key of an address (IPv4 whole, IPv6 masked to /64, IPv4-mapped
  unwrapped), used by the three places; `Trim` drops the oldest entries instead of all. No stored data: no migration.

### A6 — "This computer" is wider than the person at its keyboard (Low)

- **Status: fixed for the setup.** `LocalVisitor.SetupCodeWaivedAsync` also needs the header `X-SFE-Setup-Link`
  with this run's token (`LocalSetupLink`: 16 random bytes, in memory). `BrowserLauncher` opens `/?setup=<token>` on
  a new install and the console box prints that link; the client (`core/setup-link.ts`) takes it out of the address
  and sends it. Loopback without the link, or with a wrong one, is asked for the code. Tests: `LocalStartTests`,
  `setup-link.spec.ts`. **Not checked in a real browser** (the link opened by the portable package, a reload).
- **Left:** the link is in the command line of the browser SFE starts, as the fast sign-in token is (visible to
  other users in the process list; it works from this computer only and until an account exists); Local mode's
  "own computer only" gate still rests on loopback (an account is needed there): documented in `docs/first-start.md`
  for the setup only, not yet in the Local mode page.
- `LocalVisitor.IsOnThisComputer`: loopback connection, no forwarding header, loopback `Host`. The `Host` is whatever
  the client sends, so the check rests on the connection alone.
- A raw TCP forwarder on the computer (`ssh -R`, `ngrok tcp`, frp, socat, some rootless container port forwarders)
  delivers outside visitors from 127.0.0.1 without any header. They then pass: the setup of a new install without the
  code, the installation page's code-less token during that setup, and Local mode's "own computer only" gate
  (an account is still needed there).
- Every user of a shared computer is "this computer" too: any of them can set up a new install before its owner, and
  the fast sign-in token is in the command line of the browser SFE starts (visible in the process list for a moment;
  one use, one minute).
- Possible fixes:
  - Setup without a code only with a one-time token in the address of the browser SFE opens (as `FastSignIn` does),
    not for any loopback request; without the token the code is asked. No stored state: nothing to migrate.
  - Documentation: Local mode protects against the network, not against tunnels or other users of the computer.

### A7 — A locked account says so before the password is checked (Low)

- **Status: fixed.** `LocalUserDirectory.ValidateAsync` verifies the password of a locked account and says
  "locked" only when it is right. Test: `SignInLimitTests`.
- `LocalUserDirectory.ValidateAsync` returns `LockedOut` before `PasswordHashing.Verify`: "Too many failed attempts"
  for a name that exists and is locked, "Wrong name or password" (after a hash) otherwise. It tells the name exists,
  and answers measurably faster.
- "This account is disabled" is only told after the right password: fine.
- Possible fix: when locked, run the dummy verify and answer as a wrong password unless the password is right
  (then say it is locked). The first audit's #6 fixed the per-address message only.

### A8 — The update page shows raw errors to anyone (Low)

- **Status: fixed.** `GET api/migration` gives `log` (it names cache folders even without an error) and the text of
  `error` only with the setup code in the header `X-SFE-Setup-Code`, which the update page sends once the code is
  typed; otherwise `log` is empty and `error` is "The reason is in the server's log." `TenantEndpoints.Create` logs
  the reason and answers without it. Tests: `MigrationTests`, `api.service.spec.ts`, `migration-page.spec.ts`.
- **Left:** the note in the backup manifest (`BackupSources`), read by whoever holds the backup.
- `GET api/migration` is anonymous and returns `MigrationCoordinator.Error` and `Log`, which hold `ex.Message` of a
  failed update (database server names, file paths, sometimes parts of a connection string).
- `TenantEndpoints.Create` also returns `ex.Message` (server administrators only), and the backup manifest notes one.
- Possible fix: a generic sentence for the anonymous answer, the detail only with the setup code (the page already asks for
  it) and in the server log. Closes what is left of the first audit's #10 for these routes.

### A9 — Secrets at rest (Low)

- **Status: fixed** for the files and new folders: `SecretFiles.WriteAllText` creates the setup and recovery code
  files with mode 600 (and narrows one that existed); the data folder and `data/keys` are created 700 when missing;
  an existing keys folder open to others gets a warning at start (`WarnIfKeysAreOpen`). Tests: `SecretFilesTests`
  (the mode tests only run on Linux/macOS: **not run yet**, this machine is Windows).
- **Left:** encrypting the keys (with A2's account change); Windows ACLs (A2).
- `GeneratedSetupCode.Ensure` and `AccountRecovery.WriteFile` write the file, then set mode 600: for a moment it has
  the process's default mode. On Windows nothing is set: the file has the data folder's ACL (see A2).
- Data Protection keys (`data/keys`) are plain XML: reading them is forging an administrator's cookie. `ServerFiles`
  keeps them out of the libraries and they are not in backups, so this is about who can read the folder.
- Possible fixes: create the files with their mode (`FileStreamOptions.UnixCreateMode`); set 700 on `data/` and
  `data/keys` when the server creates them (not on existing ones, which an administrator may have shared on purpose:
  log a warning instead). Encrypting the keys (`ProtectKeysWithDpapi`) ties them to the service account: only with
  A2's account change, and with a tested path for existing key files.

### A10 — Allowed roots compared by spelling (Low)

- **Status: fixed.** `TenantEndpoints.IsInside` compares `LibraryPaths.RealPath` of both (space creation and the
  folder picker). Test: `TenantTests` (a junction in the allowed root).
- `TenantEndpoints.IsInside` compares `Path.GetFullPath` results: a link inside an allowed root lets a server
  administrator root a space wherever the link leads, which `AllowedRoots` exists to prevent on an External server.
- Possible fix: compare `LibraryPaths.RealPath` of both; goes with A1.

### A11 — The app's CSP lets pictures and media come from anywhere (Low)

- **Status: fixed.** `SecurityHeaders.AppPolicyFor` builds the policy from the server-level `Sfe:DataServer`
  (`AppPolicySource`, rebuilt when the settings change): none configured = `'self' data: blob:` only; one configured
  = its origin added; a template without a fixed origin keeps `http: https:`. Tests: `AppPolicyTests`.
- **Left:** EPUB chapters were not checked in a browser, before or after.
- `img-src … http: https:` and `media-src … http: https:` exist for `Sfe:DataServer`. Any untrusted markup rendered
  under the app's policy can then fetch from other sites, which tells them the reader's address and when.
  Markdown blocks external pictures itself; EPUB chapters (frames under the app's policy) were not checked.
- Possible fix: build the policy from the configured data server's origin (none configured: `'self' data: blob:`
  only). To verify for EPUB before and after.

### A12 — Dependencies (Low)

- **Status: fixed** for `zod` (`npm audit fix`: 4.6.6, `npm audit --omit=dev` reports nothing). **Left:** the CI
  step.
- `npm audit --omit=dev`: `zod` 4.6.5, GHSA-mrhx-c7xc-m3w5 (memory exhaustion validating oversized collections),
  brought by `@angular/forms` and the Angular CLI. In the browser only, and only where zod validates input of a
  hostile size: `npm audit fix` (zod ≥ 4.6.6), then `npm test`.
- `dotnet list package --vulnerable --include-transitive`: none.
- Possible fix for later ones: both commands as a CI step that fails on a known vulnerability.

### A13 — Still open from the first audit

- **Status: open**, all of them.
- **#8 `AllowedHosts: *`:** unchanged; it is also why `IsOnThisComputer` must check `Host` itself. A possible path
  that keeps installs working: the installation page proposes the host names seen so far and saves the list.
- **#11 shared links:** up to 7 days, no single revocation. Possible fix without invalidating settings: a per-link id
  in the token and a small revocation list; the lifetime setting stays.
- **#9 editor:** still holds up to 200 MB per upload in memory.
- **#3:** see A2.
- **Local mode gaps** noted in [security.md](security.md) (file links and jobs made before a switch): unchanged.

## Suggested order

1. ~~A3 and A5: small, no migration, no visible change.~~ Done.
2. ~~A1 with A10~~: done as an opt-in `ReadOnly` mode (default `Follow`, decided); `Refuse` left.
3. ~~A2: installer check~~ done; the service account for new installs with its documentation is left.
4. ~~A4, A6–A9, A11–A12~~: done.

What is left, by weight: A1's default and `Refuse` mode; A2's service account; A13; then the "Left" line of each
finding.

Written with the fixes: `CHANGELOG.md` (`## Unreleased`), `docs/configuration.md` and `docs/upgrading.md`, the new
rules and attack vectors in [security.md](security.md).
