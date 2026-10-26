# Server log (Administration › Logs)

Code: `src/Sfe.Core/Logs/` (`AuditLogProvider`, `AuditLogService`), entity `Data/Entities/Logs/LogEntry.cs`
(`AccessDbContext.LogEntries`, migration `AuditLog`), `src/Sfe.Api/AuditLogWorker.cs`, `Endpoints/LogEndpoints.cs`
(`GET api/logs`); client `src/app/logs/` (`LogsSectionComponent`, `logs.ts` filters), `styles/logs.css`. User docs:
`docs/administration/logs.md`.

- `AuditLogProvider` is one root `ILoggerProvider` (registered in `AddSfeAccess`): it only queues (bounded at
  `MaxQueued`, oldest dropped and counted). `TenantHost` hands libraries `ForTenant(id)` instead of the instance, which
  marks their entries with `TenantId`; root entries (requests' own loggers, start-up, accounts) have none.
- Levels follow the `Logging` section through the logger factory's filters, as the console: no extra filter.
- **Security warnings:** log security events with `logger.LogSecurityWarning(...)` (`Core/Logs/SecurityLog`: a
  warning with `EventId` 9000 "Security"). The console shows a warning; `AuditLogProvider` stores
  `LogEntryLevel.SecurityWarning` (7; `LogEntry.Level` is `LogEntryLevel`, same numbers as `LogLevel`, so no
  migration), the API and client name it `securityWarning`, files write `SEC`. Use it for anything acting against the
  protections (sign-in failures, lockouts, codes, recoveries, installation changes), not for operational warnings.
- **Refusals and failures of the API** (`Sfe.Api/Endpoints/ProblemLog`, an endpoint filter on the `/api` group): any
  answer with a problem body is logged under `Sfe.Api.Problems` — method, path (never the query: unlock ids), status,
  user, the detail and each field's errors. 400 warning, 5xx error, others (409/413/422) information; plain
  401/403/404 not. Routes need nothing: return `TypedResults.Problem`/`ValidationProblem` and it is logged. Test:
  `LogTests.WhatTheApiRefusesOrFailsWithIsLoggedWithWhoAskedButNoPassword`.
- `AuditLogProvider.Suppress()` (an `AsyncLocal`) keeps the log's own EF commands and failures out of it, else EF at
  Information would log every insert into the log forever. Use it around anything the log service does.
- `AuditLogWorker` (root hosted service) flushes every 2 s and purges hourly; while `MigrationCoordinator.IsBlocking`
  it flushes with `database: false` (files still written, entries kept in `_pending`, bounded) and never purges.
- The service reads its settings with `SettingLayers.ServerLevel()` (configuration + `settings.json`, no database):
  it works while the database is down or not updated.
- Settings `logs.retentionDays`, `logs.writeFiles` are server keys (`SettingCatalog.ServerKeys`) with the tenant right
  `settings.logs` (every setting needs a tenant right: `LayeredSettingsTests`). `Sfe:Logs:Directory` is configuration
  only (decides where data lives), resolved against the content root in `AddSfeAccess`.
- Rights: `logs.view` is a tenant right (since 4), so a space's groups can be given it; the route forces `tenant` to
  the caller's space unless `IsServerAdmin`, and only server administrators get `filesDirectory`.
- Search: keyset by `Id` desc (`before`), text by `LIKE` with `\` escaping (SQL Server's `[` too) over message, category
  and exception, so it scans; the retention bounds the table.
- Tests: `LogTests` (call `AuditLogService.FlushAsync` instead of waiting for the worker); `TestLibrary.LogsPath` keeps
  the files out of `src/Sfe.Api/data`.
