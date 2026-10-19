# Server modes (Local / External)

Code: `src/Sfe.Api/Security/ServerModes.cs`, `Maintenance/InstallationSettings.cs` (`ModeKey`, `Save`),
`Endpoints/InstallationEndpoints.cs`, `Security/RequestGuard.cs`, `Security/SfeSession.cs` (`ValidateAsync`),
`Endpoints/SessionEndpoints.cs` (`SignIn`), `AccessService.IsAdministratorAsync`; client
`account/installation-form.component.ts` (mode fieldset), `setup-page` (`[setup]="true"`),
`account/local-only-page.component.ts`, `core/api.service.ts` (403 `local-only`). Tests: `InstallationTests` (mode
tests), `ServerModeTests`, `account.spec.ts`. Epic and what is still to build:
[server-modes-plan](../development/server-modes-plan.md).

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
- Tests: the test servers set `Sfe:Mode=External` (`TestLibrary.Settings`), because their clients have no remote
  address, so they aren't on this computer; Local tests remove the key and use `ApiClients.CreateFrom(factory,
  "127.0.0.1")` or the `AddressHeader` on a signed-in client.
