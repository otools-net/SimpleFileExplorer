# Plan: namespaces `Sfe.*` become `Otools.Net.Sfe.*`

Files: every `.cs` file of `src/Sfe.Api`, `src/Sfe.Core`, `tests/Sfe.Tests` (namespaces, `using`s, `<see cref>`s),
`*.csproj` (`RootNamespace`), `.editorconfig` if it names namespaces, `agents/tools/readability/` (references),
`agents/docs/` (type names in notes). Part 3 of the [epic](external-access-epic-plan.md); comes before
[client-library-plan](client-library-plan.md) so the library is born as `Otools.Net.Sfe.Client` (a namespace in a
released package can't be renamed without breaking its users). Delete this plan once its last item is ticked.

Requested by the owner on 2026-11-13. A **refactoring**: no behaviour changes, committed apart from features
([readability](readability.md)).

**Done when:** no C# namespace starts with `Sfe.` any more, the OpenAPI document and `routes.txt` are unchanged
except for what is listed under "Kept as they are", and existing installs upgrade with nothing to do.

## Scope (decided with the owner, 2026-11-13)

- **Namespaces only.** Projects, folders and assemblies keep their names (`src/Sfe.Core`, `Sfe.Api.exe`,
  `Sfe.Tests`, `Sfe.slnx`): installed Windows services, the systemd unit, the Docker entry point, the portable zip,
  the AppImage and the smoke tests stay as they are. `RootNamespace` in each `.csproj` is set to the new namespace so
  new files get it; `AssemblyName` stays.
- The client library of plan 4 follows the same rule: namespace `Otools.Net.Sfe.Client`; its project and package
  names are decided in that plan.

## Progress

- [x] Phase 0: scope decided (namespaces only) and written here
- [x] Phase 1: `RootNamespace` and every `namespace`/`using` of `Sfe.Core` → `Otools.Net.Sfe.Core` (tests follow)
- [x] Phase 2: the same for `Sfe.Api` and `Sfe.Tests`. As built (1 and 2 together): `agents/tools/namespace-rename/`
  rewrote code, `cref`s and the EF snapshots' entity names (not older designer files); `AuditLogService.DroppedCategory`
  keeps the old category; `TaskLogProvider` takes `Otools.Net.Sfe.*` and `Sfe.*`; other `ILogger<T>` categories now
  start with `Otools.Net.`, and `LegacyLogCategories` copies each `Logging` level rule naming `Sfe…` to
  `Otools.Net.Sfe…` (unless set), so users' old keys still apply; the log search matches text inside the category, so
  old names still find new entries
- [ ] Phase 3: `agents/docs/` and `AGENTS.md` type names, readability references rewritten
  (`references.mjs --only openapi,routes`: identical document), CHANGELOG line (internal: no version bump on its own)

## Kept as they are (persisted or configured: changing them breaks installs)

- Data protection purposes (`"Sfe.FileLinks.v1"`, `"Sfe.FlashLinks.v1"`, `"Sfe.Installation.v1"`): links already
  given out and protected settings would stop opening.
- Log categories written to the log and named in users' `Logging:LogLevel` (`ProblemLog.Category` =
  `"Sfe.Api.Problems"`, `"Sfe.Api.Archives"`, `typeof(AuditLogService).FullName`): turn `FullName` ones into
  constants holding the old text, so the log page's category filter and users' log levels keep working.
- The `Sfe` configuration section, connection string name and environment variables (`Sfe__…`).
- EF migrations: only `MigrationId`s are stored; the model snapshots' namespaces may change (check both providers:
  `dotnet ef migrations has-pending-model-changes`).
- OpenAPI schema names: they come from type names, not namespaces; check the document stays identical.

## Traps

- A text replace of `Sfe.` also hits strings above, `X-SFE-…` headers, file names (`sfe.db`) and `Sfe.slnx`: rename
  with the IDE / Roslyn (`dotnet format` won't do it), or a script restricted to `namespace` and `using` lines, then
  grep `"Sfe.` for what must stay.
- `InternalsVisibleTo("Sfe.Tests")` names the **assembly**, not the namespace: unchanged.
- Default names that come from the assembly, not the namespace (embedded resource names, `AssemblyName`-based
  paths, user secrets id): check none reads a namespace-derived name (`GetManifestResourceStream("Sfe.…")`).
- Fully qualified names in `<see cref="Sfe.Core…"/>` and in `nameof`/attributes.
