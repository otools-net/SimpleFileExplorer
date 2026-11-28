# Dashboard and start page

Code: client `src/app/dashboard/` (`dashboard-page.component.*`, `dashboard-widgets.ts` the registry,
`dashboard-layout.ts`, `start-page.ts` the guard, `widgets/`), `styles/dashboard.css`; server the setting
`Browse:StartPage` (`Options/StartPage.cs`, user key `browse.startPage`), answered by `api/config` as `startPage`.
User docs: `docs/user-guide/browsing.md#dashboard`, `docs/administration/interface.md#browsing`.

- `dashboard` is a lazy route like the other pages: a header of its own (brand and **Files** to the library's top,
  the bell, Administration, Help; not the listing's top bar or ⋮ menu, whose entries act on the folder listed), then
  the cards. The ⋮ menu's **Dashboard** leads to it.
- **Registry** (`DASHBOARD_WIDGETS`): `id` (the name in layouts, never renamed), `title`, `icon`, `shown(can)` (the
  rights what it reads needs), `load()` (a dynamic import: each widget its own chunk, nothing in the initial bundle).
  A widget is a component without inputs that reads what it shows itself (root services, `Api`).
- **Layout**: fixed columns (`DASHBOARD_COLUMNS` = 2), each a list of widget ids; `arrangeLayout` drops unknown ids
  (a layout saved by a later version), widgets not shown to the session and repeats, and puts extra columns at the
  end of the last. CSS grid: one column at ≤ 760 px, the columns one after the other. Only `DEFAULT_LAYOUT` so far;
  layouts saved per user and the administrators' default come with 2.0 step 5.5b.
- **Start page** (`browse.startPage`: `dashboard` default since 2.0, `home`, `root`): the guard `startPage` on the
  `''` route acts only on the app's **first navigation**, to the address **without a query**, from a **fresh
  history entry** (`history.state` without the router's `navigationId`, read when the module loads): a bookmark, a
  typed address, a new tab. A reload, back from another site, `?path=…` and every in-app `/` (the brand, the root
  crumb) keep the listing. Old clients and servers without `startPage`: `root`.
- The guard loads the configuration (it needs the user's setting) and hands it to the listing page
  (`takeStartConfig`) so the start costs one `api/config`; `home` also loads the home folder (`HomeFolderService`),
  and opens the top of the library when there is none or it is not there. Everything injected **before** the first
  `await` (the injection context ends there).
- Widgets so far: `places` (home folder and quick links; a file's link opens its folder, as the store's `reveal`
  would act on a listing that isn't shown) and `storage` (`api/storage` for the library's top, read once).
- e2e: `global-setup.ts` sets the e2e library's start page to `root` (the specs opening `./` expect the listing);
  `dashboard.spec.ts` opens `./dashboard`. The docs-screenshots `library` scene opens `?path=` for the same reason;
  scene `dashboard` takes `dashboard.png` and `phone-dashboard.png`.
