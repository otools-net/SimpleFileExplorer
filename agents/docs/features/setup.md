# First setup and setup code

Code: `src/Sfe.Core/Access/SetupService.cs`, `GeneratedSetupCode.cs`, `Options/AccessOptions` (`SetupOptions`),
`src/Sfe.Api/Program.cs`; client `src/app/account/` (`SetupPage`, `SetupCodeSteps`, `InstallationForm`).
Installation step: [installation-page.md](installation-page.md).

- `SetupService`: required while no enabled server administrator exists; needs `Sfe:Setup:Code` (≥ 12, constant-time
  compare, `SetupOptions.Matches`), except a new install set up from the server's own computer
  ([local-start.md](local-start.md)); creates the local admin in the first library's Administrators group, renames the
  library.
- Without a configured code, `GeneratedSetupCode` (post-configures `SetupOptions`, `CodeIsGenerated`) writes one to
  `Sfe:Setup:CodeFile` (`data/setup-code.txt`) when needed: Program.cs at start (and prints it in a box after
  `StartAsync`), and lazily in `GET api/setup` / `GET api/migration`, since under `WebApplicationFactory` nothing after
  `builder.Build()` in Program.cs runs.
- The box goes to `Console.Out`, **never through a logger**: `AuditLogProvider` stores every log entry (database, daily
  files, backups). The logger only says where the file is. The 1.47.0 upgrade step `RenewSetupCode` deletes the file
  (1.46 logged codes); `GeneratedSetupCode.Renew` forgets it until the next `Ensure`.
- `SetupInfo.CodeFile` / `MigrationInfo.CodeFile` are only filled while the page needs the code (anonymous routes).
- A generated code compares without dashes, spaces and case; a configured one exactly.
- The setup page's steps: `checkInstallation(code)` (token + `InstallationView`), `InstallationForm.save()`,
  `completeSetup` (code sent again); recovery skips the library step.
- `Sfe:Setup:Recover` re-opens it (the app keeps working) to reset a local admin.
- Program.cs creates the first library (`EnsureDefaultTenantAsync`) at every start and moves per-library keys out of
  `settings.json` (`SettingLayers.MovePerLibrarySettingsAsync`).
- Until an administrator exists, `RequestGuard` answers 503 `setup-required`
  ([sessions-and-pipeline.md](../architecture/sessions-and-pipeline.md)).
