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

- [ ] `InstallationSettings.Save` refuses `root` and `ffmpegPath`; `DescribeAsync` reports them locked by "External
      mode" with the variables to set (`Sfe__Root`, `Sfe__Thumbnails__FfmpegPath`).
- [ ] Local mode only: `LocalVisitor.SetupCodeWaivedAsync`, `FastSignIn.IssueAsync` / `RedeemAsync`,
      `BrowserLauncher`.
- [ ] Setup page and installation form show root and ffmpeg read-only in External mode.
- [ ] Tests: refused saves, no waiver, no fast sign-in, spaces still limited to `AllowedRoots`.
- [ ] [security](security.md): attack vectors for External (changing a path from the web). Local mode's are in
      (1.77.0).
- [ ] Local start tests (`LocalStartTests`) then need `settings.Remove(ServerModes.Key)`: test servers are External.

### Phase 4 — Docker image

- [ ] `Dockerfile`, multi-stage:
  1. Node LTS slim: `npm ci`, `npm run build` in `src/Sfe.Web`;
  2. `mcr.microsoft.com/dotnet/sdk:10.0`: restore with the project files copied first, then
     `dotnet publish src/Sfe.Api -c Release -r linux-<x64|arm64 from TARGETARCH> -p:SfePortable=true
     -p:SfeSkipWebClient=true -o /app`;
  3. `mcr.microsoft.com/dotnet/runtime-deps:10.0` + `apt-get install ffmpeg`, non-root `app` user.
- [ ] Environment: `Sfe__Mode=External`, `Urls=http://+:8080`, `Sfe__Root=/library`,
      `Sfe__Thumbnails__FfmpegPath=/usr/bin/ffmpeg`, `Sfe__OpenBrowser=false`; volumes `/data`, `/library`; entry point
      `/app/Sfe.Api --contentRoot /data`; check that SQLite `data/sfe.db` lands under `/data`.
- [ ] `.dockerignore` (bin, obj, node_modules, artifacts, data, .git); `docker-compose.yml` example (library mount,
      `Sfe__Tenants__AllowedRoots__0`, `Sfe__Security__TrustedProxies`, `Sfe__Setup__Code`).
- [ ] Agent note `agents/docs/environment/docker.md` (build and run commands, what was verified).

### Phase 5 — user docs and wrap-up

- [ ] `docs/first-start.md` (mode choice), new `docs/docker.md`, `docs/upgrading.md` (upgraded installs become Local:
      other computers and non-administrators are refused; `Sfe__Mode=External` keeps them), README, screenshots.
- [ ] Feature note `agents/docs/features/server-modes.md` (lasting facts) with its `AGENTS.md` line; update
      [local-start](../features/local-start.md) and [installation-page](../features/installation-page.md).

## Open questions

- Phase 4: publish the image (registry, tags) or build only for now; a `docker` target in `build/build.sh`?
- Phase 4: Node LTS version pinned in the image (Angular 22's requirement).

## Found while building

- **Not in `SfeOptions`:** the configuration binder throws on an enum value it can't parse, and `SfeOptions` is bound
  everywhere: a typo in `Sfe:Mode` would stop the server. `ServerModes.Configured` reads the word; unknown = External.
- **Tests:** the test settings come after the installation file, like environment variables: setting `Sfe:Mode` there
  makes it locked. `ApiClients.CreateFrom(factory, "127.0.0.1")`, or `ApiClients.AddressHeader` on a signed-in client,
  makes a visitor on this computer.
