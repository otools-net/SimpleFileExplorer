# Local start: browser at start, setup without code, fast sign-in

Code: `src/Sfe.Api/BrowserLauncher.cs`, `StartupLogo.cs`, `CommandLine.cs`, `Security/LocalVisitor.cs`, `Security/FastSignIn.cs`, `Program.cs`,
`Endpoints/SessionEndpoints.cs` (`api/session/start`), `SetupService.IsFreshAsync`; client `account/setup-page.component.ts`,
the `startup` tab (`settings/tabs/system-settings.ts`) (System › Start-up). Tests: `LocalStartTests`.

- **Browser at start:** only portable builds started interactively (`Portable`/`AppImage` hosting). Off with
  `--no-browser` (taken out of `args` before the builder: the command-line provider would eat the next argument as its
  value) or the server-level setting `openBrowser` (`SettingLayers.ServerLevel()`, so settings.json counts, not only
  `Sfe:OpenBrowser`).
- **Program switches** (`--no-browser`, `--no-logo`, also `/name`, any case) go through `CommandLine.TakeSwitch`, which
  removes them from `args` before the builder. A new switch uses it too, and gets a line in `CommandLine.HelpText`.
- **`--help`** (`-h`, `-?`, `/?`): checked first in `Program.cs` (before the service commands), prints
  `CommandLine.HelpText` and exits 0. Grouped, ASCII, within 80 columns (a test checks it); the settings shown are the
  usual ones, `docs/configuration.md` has them all.
- **Logo at start** (`StartupLogo`): ASCII only, printed before the builder; not with `--no-logo`, for a service
  (`!Environment.UserInteractive`) or when stdout is redirected (journal, `docker logs`, test host).
- **"On this computer"** (`LocalVisitor.IsOnThisComputer`): loopback connection, no `X-Forwarded-For`/`X-Original-For`/
  `Forwarded` (a local proxy brings other computers' visitors), and a loopback `Host` (DNS rebinding: a foreign site
  pointing its name at 127.0.0.1 would otherwise reach the setup through the visitor's browser).
- **Modes** ([server-modes.md](server-modes.md)): fast sign-in is Local only; the waiver ends when the configuration
  (not the setup page) makes the server External.
- **Setup link** (`LocalSetupLink`, audit 2026-11 A6): being on this computer is not enough to skip the code (a raw
  TCP tunnel's visitors and every user of a shared computer are loopback visitors). One token per run (16 random
  bytes, in memory); `BrowserLauncher` opens `/?setup=<token>` on a new install and `Program.AnnounceSetupCode`
  prints the same link in the console box. The client (`core/setup-link.ts`, read once when `api.service.ts` loads)
  takes it out of the address, keeps it in `sessionStorage` and sends `X-SFE-Setup-Link` on every request.
- **Setup without code:** `LocalVisitor.SetupCodeWaivedAsync` = `Sfe:Setup:LocalWithoutCode` (default on) + on this
  computer + the setup link's header + `SetupService.IsFreshAsync` (no `Users` row at all, not Recover). An install whose admins were deleted or
  disabled still needs the code. `GET api/setup` answers `codeRequired`; `installation/check` then gives a token holding
  `"local-setup"` (`InstallationAccess.GrantWithoutCode`), accepted only while the waiver still holds, so it dies with
  the first account; `CompleteAsync(input, codeWaived)` re-checks freshness itself.
- The test server has no remote address: existing tests keep needing the code; waiver tests use
  `ApiClients.CreateWithSetupLink(factory)` (127.0.0.1, Host `localhost`, the run's link);
  `CreateFrom(factory, "127.0.0.1")` alone is a loopback visitor without the link, who needs the code.
- **Fast sign-in** (`fastSignIn`, server-level, right `server.settings`): at start `BrowserLauncher` asks
  `FastSignIn.IssueAsync` for a token (32 random bytes, kept hashed in memory, one at a time, 1 min) and opens
  `api/session/start?token=`; `RedeemAsync` consumes it, re-checks the setting and that the account is still the only
  enabled server admin (`AccessService.GetServerAdminIdsAsync`, at most two ids). The route only accepts local visitors,
  always redirects to the app (`PathBase + "/"`), logs with `LogSecurityWarning`. Not issued while an update is due.
- The setup page offers fast sign-in when `hosting` is `portable`/`appImage`; the endpoint saves it with
  `SettingLayers.SaveServerValueAsync` (no tenant container on setup routes, so not `SettingsService`).
- `openBrowser`/`fastSignIn` are the only catalog keys whose right is `server.settings` (server-only); the catalog test
  allows it for `SettingLevel.Server` keys.
