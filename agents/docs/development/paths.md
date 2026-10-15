# Paths

Code: `src/Sfe.Core/Paths/LibraryPaths.cs`; client `src/Sfe.Web/src/app/core/paths.ts`.

- API paths are root-relative with `/`; always resolve through `LibraryPaths` (root-relative, escape-proof).
- Comparison follows the host file system (case-sensitive on Linux).
- SQL Server caps paths at 260 (`SfeDbContext.MaxPathLength`).
- Client URLs stay relative (virtual directory): see [frontend.md](frontend.md).
