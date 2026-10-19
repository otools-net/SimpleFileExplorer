# Server modes (Local / External)

Code: `src/Sfe.Api/Security/ServerModes.cs`, `Maintenance/InstallationSettings.cs` (`ModeKey`, `Save`),
`Endpoints/InstallationEndpoints.cs`; client `account/installation-form.component.ts` (mode fieldset), `setup-page`
(`[setup]="true"`). Tests: `InstallationTests` (mode tests), `account.spec.ts`. Epic and what is still to build:
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
