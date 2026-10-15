# Stack

SFE is a port of an ASP.NET MVC 5 / .NET Framework 4.8 app (EF6, jQuery, Accord/System.Drawing); the old app's database
and thumbnail cache are not adopted. Accounts, groups, rights, guests and several libraries (tenants) since 1.34.0.

| Area | Stack |
|---|---|
| Runtime | .NET 10, C# latest, `Nullable` + `ImplicitUsings`; solution `Sfe.slnx` |
| Web | ASP.NET Core minimal APIs (`MapGroup`, `TypedResults`), ProblemDetails, enums as camelCase strings |
| API docs | `Microsoft.AspNetCore.OpenApi` (`/openapi/v1.json`) + Swashbuckle Swagger UI only (`/api/docs`) |
| Data | EF Core 10, **SQL Server** (migrations) and **SQLite** (`EnsureCreated`); `IDbContextFactory<SfeDbContext>` |
| Media | external **ffmpeg** CLI (optional): thumbnails, trickplay, video fingerprints |
| Frontend | **Angular 22** + TypeScript 6 in `src/Sfe.Web`: standalone components, signals, zoneless, OnPush, `HttpClient`, router; global CSS; built by npm into `src/Sfe.Api/wwwroot` (git-ignored) |
| Tests | MSTest 4, `WebApplicationFactory<Program>`, method-level parallelism; client: Vitest via `ng test` (`*.spec.ts`) |
| Hosting | Kestrel `:5080`, Windows service / systemd / IIS, portable zip + AppImage |

External references:

- TagStudio (tag model): https://github.com/TagStudioDev/TagStudio
- Angular (client framework, signals, control flow): https://angular.dev/
- ffmpeg: https://ffmpeg.org/ (filters used: `fps`, `scale`, `tile`, `untile`, `format`)
- appimagetool 1.9.1 and AppImage type2-runtime (pinned, downloaded into `artifacts/tools` by the build):
  https://github.com/AppImage/appimagetool, https://github.com/AppImage/type2-runtime
- ASP.NET Core Hosting Bundle (IIS): https://dotnet.microsoft.com/download/dotnet/10.0
