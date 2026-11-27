# Help inside the app

Code: server `Help/HelpDocs.cs` (the resources, the index), `Help/HelpOutline.cs` (title and headings of a page),
`Endpoints/HelpEndpoints.cs` + `HelpContracts.cs`, `ApiRoutes.IsHelp`, `RequestGuard` (setup gate), the
`EmbeddedResource` of `docs/**` in `Sfe.Api.csproj`; client `help/` (`help-page.component.*`, `help-links.ts`,
`help-search.ts`, `admin-help.ts`), `Api.helpIndex` / `helpPage`, `styles/help.css`, Help in the ⋮ menu
(`main-menu.component.html`), the "?" of the admin page (`admin-page.component.*`), the setup page's link. Tests:
`HelpRouteTests`, `HelpOutlineTests`; `help/*.spec.ts`, `admin-page.spec.ts`, `topbar.spec.ts`. User docs:
[help](../../../docs/user-guide/help.md) (`help-page.png`, `help-search.png`, `phone-help.png`: scene `help`).
Release 2.0 step 5.1 (2026-11-27).

## Shipping the docs

- `docs/**` is built into `Sfe.Api.dll` as resources named `help/<path>` (`%(RecursiveDir)` gives `\` on Windows:
  `HelpDocs` turns it into `/`). Every package has it without packaging steps (portable single file, AppImage,
  Docker, IIS, `dotnet run`); the DLL grows by the docs' size (~16 MB, nearly all screenshots).
- Docker: `docs/` is no longer in `.dockerignore` and the server stage copies it (`COPY docs/ docs/`).
- A docs change rebuilds `Sfe.Api` (the resources are inputs): nothing else to regenerate.

## Routes

- `GET api/help`: `{ pages: [{ path, title, headings: [{ level, text }] }] }`, `README.md` first in each folder,
  folders by depth then name. Made once per process (`Lazy`) by `HelpOutline` (ATX `#`..`###` outside fences, text
  without code marks, emphasis and links). Decided 2026-11-27: the index is made from the shipped files on first use
  rather than at build time (a few ms, nothing to keep in step).
- `GET api/help/files/{**path}`: a page (`text/markdown; charset=utf-8`) or a picture (image types only; anything
  else 404), `SecurityHeaders.FilePolicy`, `Cache-Control: private, no-cache` with an `ETag` of the build (the
  module's MVID): a 304 until the app is rebuilt. `../` can't escape: only resource names are looked up.
- **Who:** `.AllowAnonymous()` with a check in the handler: a signed-in account (any rights, even none), a visitor
  with rights (a space open to guests), or a server not set up yet (`RequestGuard` lets `api/help` pass the
  setup gate). Else 401. Local mode still refuses other computers (not a server entry route).
- No new right (the plan said "no right needed beyond being signed in").

## Client

- Route `help` (lazy): `help?page=<path>#<anchor>`, `README.md` without `page`. The page is rendered by the Markdown
  viewer's `renderMarkdown` with `helpMarkdownLinks`: `.md` links → `help?page=…#…` (the link's `#fragment` reaches
  `MarkdownLinks.view` as its second argument, added for this), pictures → `api/v1/help/files/…`, links leaving
  `docs/` (`../AGENTS.md`) → no `href`. Clicks on page links and heading links go through the router; modified clicks
  follow the `href` (a new tab). Headings' ids are `md-<GitHub slug>`; the URL's fragment is the slug alone.
- Colours: the article is a `.text-view.prose` with `data-reader-theme="app"`; the prose reading preferences apply
  (`ReadingService.style('prose')`), a width of 860 px when none is set.
- Search: `searchHelp` over titles and headings (every word, parts of words, any case, accents folded), titles
  first, then headings, then headings whose page title gives the rest; 40 at most. Contents: `helpGroups` (Getting
  started, Using SFE, Administration, Reference, then any other folder by name).
- 401 → "Sign in to read the help" with a link to `login?return=/help…` (the API calls don't redirect).
- **Opened in a new tab** from the ⋮ menu, the admin "?" and the setup page (decided 2026-11-27): the page left keeps
  its state (unsaved settings, listing position).
- `ADMIN_HELP` maps each admin section to its page and each sub-page to a heading's anchor; `admin-help.spec.ts`
  reads `docs/` and fails when a section, a sub-page (of a section with several), a page or a heading is missing:
  renaming a heading in `docs/administration/` means updating the map.

## Gotchas

- English only (release 2.0 step 2.2: languages prepared, not translated).
- `api.service.ts` is the only file making requests: `helpPage` asks for `responseType: 'text'` through `send`.
- jsdom has no `Element.scrollTo`: the page resets `scrollTop` instead.
