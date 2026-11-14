# C# namespaces

Files: every `.cs` file of `src/` and `tests/`, `RootNamespace` in each `.csproj`, `LegacyLogCategories`,
`TaskLogProvider`, `agents/tools/namespace-rename/`.

- Every C# namespace starts with `Otools.Net.Sfe.` (`Otools.Net.Sfe.Core.Files`, `Otools.Net.Sfe.Api.Security`,
  `Otools.Net.Sfe.Client`). Renamed from `Sfe.*` on 2026-11-14, at the owner's request.
- Only namespaces: projects, folders, assemblies and the executable keep their `Sfe.*` names (`src/Sfe.Core`,
  `Sfe.Api.exe`, `Sfe.slnx`, `InternalsVisibleTo("Sfe.Tests")`), so services, the systemd unit, Docker, the
  portable zip and the AppImage didn't change.
- `RootNamespace` is set in each `.csproj`, so new files get the right namespace.
- EF model snapshots name entities with the new namespace; older migration designer files keep the old one (history,
  not read at run time).

## Strings that keep the old `Sfe.` text (persisted or configured)

- Data protection purposes `"Sfe.FileLinks.v1"`, `"Sfe.FlashLinks.v1"`, `"Sfe.Installation.v1"`: links given out
  and protected settings would stop opening.
- Log categories given by hand: `"Sfe.Api.Problems"` (`ProblemLog.Category`), `"Sfe.Api.Archives"`, and
  `AuditLogService.DroppedCategory`.
- The `Sfe` configuration section, the connection string name, `Sfe__…` environment variables, `X-SFE-…` headers.
- A text replace of `Sfe.` hits all of these: never rename by plain search and replace.

## Log categories

- Categories from `ILogger<T>` are the type's full name, so they read `Otools.Net.Sfe.…` since the rename.
- `LegacyLogCategories` (registered in `AddSfeAccess` and `AddSfeCore`) copies each `Logging` level rule naming
  `Sfe…`, under `LogLevel` or a provider section, to `Otools.Net.Sfe…` unless that is set, so users' old keys still
  apply (`LegacyLogCategoriesTests`).
- `TaskLogProvider` takes categories starting with `Otools.Net.Sfe.` or `Sfe.`.
- The log page searches text inside the category, so old names still find new entries.

## Checks done for the rename

- The OpenAPI document (`OpenApiBaselineTests`) and `routes.txt` (`references.mjs --only routes`) were identical
  before and after; `dotnet ef migrations has-pending-model-changes` said no changes for both contexts.
