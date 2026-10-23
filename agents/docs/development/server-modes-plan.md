# Server modes (Local / External) and the Docker image: plan and progress (epic)

Code (planned): `src/Sfe.Core/Options/SfeOptions.cs` (`Mode`), `src/Sfe.Api/Security/ServerModes.cs`,
`Maintenance/InstallationSettings.cs`, `Security/RequestGuard.cs`, `Security/LocalVisitor.cs`, `Security/FastSignIn.cs`,
`BrowserLauncher.cs`, `Endpoints/SetupEndpoints.cs`; client `src/Sfe.Web/src/app/account/` (setup page, installation
form), `app/access/`; `Dockerfile`, `.dockerignore`, `docker-compose.yml`. Related: [local-start](../features/local-start.md),
[installation-page](../features/installation-page.md), [setup](../features/setup.md), [security](security.md),
[portable-build](../environment/portable-build.md).

Goal: SFE runs in one of two modes, chosen during setup and forceable from the environment or a configuration file:

- **Local** (today's portable use): one person on their own computer. Only administrators sign in, only from this
  computer.
- **External** (reached by other people over the network, e.g. the Docker image): library paths and the ffmpeg path are
  not changed from the UI, only through environment variables or the configuration file.

## How to use this file

- Built over several sessions: **update it in the commit that does the work** (tick `[x]` with the version, move
  findings into the phase), so the next session starts here: take the first phase that isn't done. One phase = one
  session = one commit = one minor version.
- Gotchas found while building go in [Found while building](#found-while-building); questions are decided at the start
  of the phase that needs them and move to [Decisions](#decisions).
- When a phase is done, its lasting facts go into a feature note (`agents/docs/features/server-modes.md`, indexed in
  `AGENTS.md`); this file keeps the checklist.

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## Decisions

- **Mode source: configuration key `Sfe:Mode`** (`Local` | `External`). The setup writes it into `data/installation.json`
  (`InstallationSettings.Keys` gets `ModeKey`); appsettings after the file, the environment (`Sfe__Mode`) or the command
  line win, and the key then shows as locked (`InstallationSettings.Field`). Never an `EditableSettings` key. Compared
  (2026-10-19):
  - a database row with an environment override: its own override and lock plumbing, easy to flip from code;
  - build-time only (portable = Local, image = External): no choice during setup;
  - inferred from `Urls` / the proxy: implicit, wrong behind reverse proxies.
- **Missing key = Local** once the setup is done: that is the upgrade of existing installs ("always Local"), so no
  upgrade step and no database change. **Before the setup** nothing is restricted, so a headless server can still be set
  up from another computer with the code.
- **Switching from the UI:** only Local → External (it tightens). External → Local only by editing the configuration or
  the environment, so an External administrator can never re-open path editing from the web.
- **Local mode:** only server administrators and members of a space's built-in `Administrators` group sign in, and only
  from this computer (`LocalVisitor.IsOnThisComputer`); guest access is off. Accounts and groups stay manageable, so
  administrators can add other administrators.
- **External mode:** the first library's root and the ffmpeg path are read-only in the UI; `Urls` stays editable. New
  spaces may still be created from the UI, but only inside `Sfe:Tenants:AllowedRoots` (configuration only, already
  enforced by `TenantEndpoints.Create`); no route changes an existing space's root. No setup without a code, no fast
  sign-in, no browser at start.
- **A non-administrator on a Local server** gets the sign-in page (its session ends), which refuses it with "only its
  administrators sign in" (1.77.0).
- **External locks the paths also during the setup** (1.78.0), as asked ("never from the UI"): whoever wants to pick
  them on the page does it while the server is still Local, then switches.
- **Phase 1 ships alone** (1.76.0): the switch to External exists before any Local limit applies; the UI and docs say
  the mode is only recorded until the next phases.
- **Docker:** reuses the portable build (`-p:SfePortable=true`: web root from the exe folder, content root from
  `--contentRoot /data`, as `build/linux/AppRun` does). The image is External.

## Verified constraints

- **No "setup done" flag:** done = `AccessService.HasServerAdminAsync` (an enabled `IsServerAdmin` user); until then
  `RequestGuard` answers 503 `setup-required` except for setup, installation, session and migration routes.
- **`LocalVisitor.IsOnThisComputer`** needs a loopback connection, no forwarded headers and a loopback `Host`: in Local
  mode `http://<machine-name>:port` is refused even from the same computer, so the message must name
  `http://localhost:<port>`.
- **`Urls`:** portable `appsettings.json` has `http://localhost:5080`, the non-portable one `http://*:5080`. Local mode
  does not rewrite `Urls` (existing installs keep their files): the guard refuses other computers instead.
- **Space administrators** have no flag: a space administrator is whoever gets `tenant.members` without `server.users`,
  by default the space's `BuiltInGroup.Administrators` ([accounts](../features/accounts.md)).
- **Docker detection exists:** `HostingKind.Docker` from `DOTNET_RUNNING_IN_CONTAINER` (`SetupEndpoints.DetectHosting`);
  `BrowserLauncher` already does nothing there.
- **Client build** runs inside MSBuild (`npm ci`, `npm run build` into `src/Sfe.Api/wwwroot`);
  `-p:SfeSkipWebClient=true` skips it. No Node version is pinned (Angular 22).
- **ffmpeg** is not bundled (`Sfe:Thumbnails:FfmpegPath`, default `ffmpeg` on the PATH).
- **Default data paths** are relative to the content root (`data/sfe.db`, `data/settings.json`,
  `data/installation.json`, `data/thumbs`, `data/logs`, `data/keys`, `data/setup-code.txt`).
- **Reverse proxy:** `UseForwardedHeaders` trusts loopback proxies only; behind a proxy in Docker set
  `Sfe:Security:TrustedProxies`.
- **This machine:** the docker context points to an unreachable remote ([dev-machine](../environment/dev-machine.md)):
  try docker in WSL.

## Phases

### Phase 1 — mode key, setup choice, installation page

- [x] (1.76.0) `ServerModes` (Api) reads `Sfe:Mode` as a word, not `SfeOptions.Mode` (see Found while building).
- [x] (1.76.0) `InstallationSettings`: `ModeKey` in `Keys`, `InstallationInput.Mode`; External → Local refused once set
      up, Local refused to a visitor on another computer; lock state from `Field`.
- [-] `SetupInfo` fields: not needed, the installation view carries `mode`, `modeInEffect`, `onThisComputer`.
- [x] (1.76.0) Setup page and System › Installation: a "How SFE is used" fieldset in the shared installation form;
      Local preselected on this computer, External elsewhere; Local disabled where it can't be chosen; read-only when
      locked. A mode not chosen yet is always sent on save.
- [x] (1.76.0) Tests: `InstallationTests` (choice, no way back, lock, parsing), `account.spec.ts` (form).
- Moved to phase 2: `api/config` gives `mode` (the first phase whose client hides controls by mode).

### Phase 2 — Local mode restrictions

- [x] (1.77.0) `RequestGuard`: 403 `local-only` to other computers after the setup check, except setup, session,
      installation and migration; the client opens a `local-only` page (where to open SFE, and the switch to External
      for the server administrator: the way out of an install upgraded to Local while used over the network). No
      static page: the SPA itself loads.
- [x] (1.77.0) Administrators only: `AccessService.IsAdministratorAsync`, checked by `SignIn` (400
      `local-administrators-only`) and `SfeSession.ValidateAsync` (sessions end).
- [x] (1.77.0) No visitors: `AnonymousGuests` post-configured off (no change to `CurrentAccess`); notes on the space's
      guest switch and on the accounts page (a note above the list, not a mark per account).
- [x] (1.77.0) Installation form: network radios hidden while Local is chosen (the port stays). `Network = true` not
      refused: harmless, as the guard refuses other computers anyway.
- [x] (1.77.0) `api/config` gives `mode`. The "nothing before the setup" rule needs no code: the guard checks after the
      setup check, and nobody signs in before the setup.
- [x] (1.77.0) Tests: `ServerModeTests`; test servers default to External (`TestLibrary.Settings`).
- [x] (1.77.0) `CHANGELOG.md` and `docs/upgrading.md`: upgraded installs are Local, and the three ways to External.

### Phase 3 — External mode restrictions

- [x] (1.78.0) `InstallationSettings.Field` reports root and ffmpeg locked on External (so `Save` refuses them, with
      the variables to set); also refused together with `Mode: External`.
- [x] (1.78.0) `FastSignIn` Local only. The setup waiver ends only when the **configuration** makes the server External
      (External chosen on the setup page keeps it: the rest of that setup needs it).
- [-] `BrowserLauncher` stays on for External: opening a browser is harmless, only the fast sign-in link is Local.
- [x] (1.78.0) Setup page and installation form show root and ffmpeg read-only once External is chosen (saved or not);
      fast sign-in offered only for Local.
- [x] (1.78.0) Tests: `InstallationTests` (External locks, switch with a folder), `LocalStartTests` (Local by default;
      External by configuration: code, no link; External chosen during a code-free setup). `AllowedRoots` unchanged
      (`TenantTests`).
- [x] (1.78.0) [security](security.md): External attack vectors.

### Phase 4 — Docker image

- [x] (1.79.0) `Dockerfile`: node 24 → SDK (portable build, RID from `TARGETARCH`) → `runtime-deps:10.0` + ffmpeg,
      user `$APP_UID` ([docker.md](../environment/docker.md)).
- [x] (1.79.0) Entry point `build/docker/entrypoint.sh`: copies `appsettings.json` to `/data` on the first start (the
      configuration is read from the content root), then `--contentRoot /data`. Index at `/data/data/sfe.db`.
- [x] (1.79.0) `.dockerignore`, `docker-compose.yml` example, `docs/docker.md`.
- [x] (1.79.0) Asked during the phase: the database, caches and logs placed by environment variables
      (`SFE_DATABASE_DIR`, `SFE_CACHE_DIR`, `SFE_LOGS_DIR`, mapped by the entry point).
- [x] (1.79.0) Built and smoke-tested with Docker Desktop (amd64); arm64 built under emulation.
- [x] (1.79.0) Fixed on the way: the console box offered a code-free setup on a server configured External.

### Phase 5 — user docs and wrap-up

- [x] Done along each phase (1.76.0–1.79.0): `docs/first-start.md`, `docs/administration/system.md`,
      `docs/upgrading.md`, `docs/configuration.md`, `docs/docker.md`, README, screenshots (`setup-page`,
      `admin-system-installation`, `local-only`), feature note [server-modes](../features/server-modes.md), notes
      [local-start](../features/local-start.md), [installation-page](../features/installation-page.md),
      [security](security.md).

**The epic is done (1.79.0).** Left for later: a health check route. Publishing the image on ghcr.io
is done ([docker.md](../environment/docker.md)). Dropped: a `docker` target in
`build/build.sh`/`build.ps1` (decided 2026-10-19: those scripts don't build images).

## Open questions

- None open. Decided: the image is built, not published (publishing is outward-facing: ask first); Node 24 (LTS).

## Found while building

- **Not in `SfeOptions`:** the configuration binder throws on an enum value it can't parse, and `SfeOptions` is bound
  everywhere: a typo in `Sfe:Mode` would stop the server. `ServerModes.Configured` reads the word; unknown = External.
- **Tests:** the test settings come after the installation file, like environment variables: setting `Sfe:Mode` there
  makes it locked. `ApiClients.CreateFrom(factory, "127.0.0.1")`, or `ApiClients.AddressHeader` on a signed-in client,
  makes a visitor on this computer.
