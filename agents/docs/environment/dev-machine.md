# Dev machine

- Windows; shells PowerShell 5.1 and Git Bash.
- The `node` on PATH is **26.11** (checked 2026-10-13): `npm test`, `npm run build` and the full `dotnet build`/`test`
  work.
- Python 3.12 is installed (no `websockets`).
- ffmpeg is at `C:/Portables/ffmpeg/bin/ffmpeg.exe` (2026-10-12 full build, not on PATH; checked 2026-10-13): set
  `SFE_TEST_FFMPEG` for the ffmpeg tests, else they are skipped.
- `docker` points at an unreachable remote context. WSL Ubuntu exists but has no .NET SDK.
- PowerShell 5.1 turns native stderr into errors under `ErrorActionPreference=Stop`, and a function's native output
  becomes its return value: `Invoke-Native` in `build.ps1` handles both.
- Line endings and git noise: [line-endings.md](line-endings.md).
- SQL Server LocalDB is installed (`(localdb)\MSSQLLocalDB`; no `sqlcmd`): check a migration with `dotnet ef database
  update --context AccessDbContext --project src/Sfe.Core --connection "Server=(localdb)\MSSQLLocalDB;Database=<scratch>;Integrated Security=true;TrustServerCertificate=true"`,
  query it from PowerShell with `System.Data.SqlClient.SqlConnection`, then drop the scratch database (checked 2026-10-16).
