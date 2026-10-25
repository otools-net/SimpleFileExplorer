# Server modes (Local / External)

Code: `src/Sfe.Api/Security/ServerModes.cs`, `Maintenance/InstallationSettings.cs` (`ModeKey`, `Save`),
`Endpoints/InstallationEndpoints.cs`, `Security/RequestGuard.cs`, `Security/SfeSession.cs` (`ValidateAsync`),
`Endpoints/SessionEndpoints.cs` (`SignIn`), `AccessService.IsAdministratorAsync`; client
`account/installation-form.component.ts` (mode fieldset), `setup-page` (`[setup]="true"`),
`account/local-only-page.component.ts`, `core/api.service.ts` (403 `local-only`). Tests: `InstallationTests` (mode
tests), `ServerModeTests`, `account.spec.ts`. The Docker image (External): [docker](../environment/docker.md).

- `Sfe:Mode` is read as a word (`ServerModes.Configured`), never bound into `SfeOptions`: the binder throws on a
  misspelled enum and would stop the server. An unknown word counts as External (the stricter one). None set = Local
  (`InEffect`): that is what installs from before 1.76 are.
- It is one of the installation file's keys (`InstallationSettings.Keys`), so a value from appsettings after the file,
  the environment or the command line shows as locked (`Field`).
- `Save` rules, with `InstallationVisitorInfo(SetupDone, OnThisComputer)` from the endpoint: Local needs a visitor on
  this computer (`LocalVisitor.IsOnThisComputer`) unless it is already the configured mode; once the setup is done,
  External never goes back to Local from the web. A mode change doesn't restart the libraries (only root/ffmpeg do).
- `InstallationView` gives `Mode` (configured field; a null value = not chosen), `ModeInEffect` and `OnThisComputer`
  (set by the endpoints, `DescribeAsync` doesn't know the request).
- Client: a mode not chosen yet is preselected (Local on this computer, External elsewhere) and **always sent** on the
  next save, so the install says which one it is; the setup page passes `setup` (External → Local still allowed there).
  The network radios only show for External (the port stays).

## Local mode limits (1.77.0)

- **This computer only:** `RequestGuard`, after the setup check (so nothing is restricted before the setup), answers 403
  `local-only` to `/api` and `/openapi` from another computer, except setup, session, installation and migration: a
  remote server administrator of an install upgraded to Local signs in and switches to External on the `local-only`
  page (it embeds `InstallationSectionComponent`). The client opens that page on any 403 `local-only`.
- **Administrators only** (`AccessService.IsAdministratorAsync`: `IsServerAdmin` or in a `BuiltInGroup.Administrators`
  group, cached per `AccessChanges.Version`): `SignIn` refuses others (400 `local-administrators-only`), and
  `SfeSession.ValidateAsync` rejects their cookie, so every route answers 401 (not 403: a 403 wouldn't open the sign-in
  page). Checking in `CurrentAccess` instead gave 403s, because the rights handler still sees the cookie's user.
- **No visitors:** `ServerModes.ApplyTo` post-configures `SecurityOptions.AnonymousGuests = false`; it follows a mode
  change because options recompute on configuration reloads. The existing `anonymousGuestsAllowed` greys the guest
  switch; the note names Local mode from `api/config`'s `mode`.
## External mode limits (1.78.0)

- **Paths from the configuration only:** `InstallationSettings.Field` reports `Sfe:Root` and the ffmpeg path locked on
  an External server ("the server's configuration (External mode: Sfe__Root …)"), so `Save` refuses them with the
  existing lock check, and the form shows them read-only. A root or ffmpeg sent with `Mode: External` in the same save is
  refused too (the client never sends them once External is chosen). What the file already holds stays in effect.
- New spaces stay creatable inside `Sfe:Tenants:AllowedRoots` (configuration only, `TenantEndpoints.Create`); no route
  changes a space's root.
- **Setup without a code** (`LocalVisitor.SetupCodeWaivedAsync`) ends when External comes from the configuration
  (`InstallationSettings.SetByConfiguration`, or not held by the installation file: appsettings): External chosen on the
  setup page by the local visitor keeps the waiver, which the rest of that setup needs (else `POST api/setup` would ask
  for a code nobody has).
- **Fast sign-in** (`FastSignIn.IssueAsync`/`RedeemAsync`) is Local only; the setup page offers it only while the
  installation is not External. The browser still opens at start on an External portable server (harmless).

## Decisions kept from the epic (1.76.0–1.79.0)

- Mode in the configuration (`Sfe:Mode`), not a database row, not build-time, not inferred from `Urls` or a proxy.
- Missing key = Local once the setup is done (the upgrade of existing installs: no upgrade step, no database change);
  before the setup nothing is restricted, so a headless server can still be set up from another computer with the code.
- `LocalVisitor.IsOnThisComputer` needs a loopback connection, no forwarded headers and a loopback `Host`: in Local mode
  `http://<machine-name>:port` is refused even from the same computer, so messages name `http://localhost:<port>`.
- Local mode does not rewrite `Urls` (existing installs keep their files): the guard refuses other computers instead.
- External locks the paths also during the setup: who wants to pick them on the page does it while still Local.
- Left for later: a health check route. Dropped: a `docker` target in `build/build.sh`/`build.ps1` (those scripts
  don't build images).

## Tests

- Tests: the test servers set `Sfe:Mode=External` (`TestLibrary.Settings`), because their clients have no remote
  address, so they aren't on this computer; Local tests remove the key and use `ApiClients.CreateFrom(factory,
  "127.0.0.1")` or the `AddressHeader` on a signed-in client. `LocalStartTests.Factory` is Local by default; installation
  tests that pick a folder or ffmpeg remove the key.
