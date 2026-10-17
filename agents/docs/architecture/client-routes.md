# Client routes

Code: `src/Sfe.Web/src/app/app.routes.ts`, `admin/leave-admin-page.ts`; server fallback in `src/Sfe.Api/Program.cs`.

- `''` = `LibraryPageComponent` (the query string `?path=&q=&sort=&desc=1` says what it lists), `admin` =
  `AdminPageComponent` (`config`, its former address, redirects there), `player` = `PlayerPageComponent`, `login` =
  `LoginPageComponent` (`?return=`), `setup` = `SetupPageComponent`, `migration` = `MigrationPageComponent`.
- All but the first two load lazily (`loadComponent`: their chunks keep the initial bundle under its 600 kB budget, a
  warning only; 598 kB at 1.42.0, 631 kB at 1.55.0: over it, a warning), so the admin page's guard lives apart (`admin/leave-admin-page.ts`).
- `<base href="./">` + `APP_BASE_HREF` from `document.baseURI` keep them working under a virtual directory, which is why
  routes stay one level deep; the server's `MapFallbackToFile("index.html")` serves `/admin`, `/player`, `/login`,
  `/setup` and `/migration`.
- Same-URL navigation reloads: see [position-restore.md](../features/position-restore.md).
