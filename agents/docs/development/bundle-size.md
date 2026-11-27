# Bundle size (the client's initial download)

Code: `src/Sfe.Web/angular.json` (`budgets`), `app/app.routes.ts`, `app/library/library-page.component.ts`,
`app/library/listing-dialogs.component.ts`. Tools: [agents/tools/bundle-size/](../../tools/bundle-size/README.md).

- The **initial** budget in `angular.json` warns just above the size the app has (980 kB, error at 1.1 MB): a warning
  means something new went into the first download. Find what with the tools, move it out, or raise the budget on
  purpose (say so in the commit).
- Sizes on 2026-11-26: 944 kB raw, 231 kB gzipped (from 1.08 MB / 253 kB). 2026-11-27: 971 kB after the read-only
  marks (release 2.0 step 4.6: Access section, lock marks, their CSS), 974.5 kB with the help (5.1: `help.css`, the
  menu entry); warning raised from 960 to 980 kB then. The help page itself and the Markdown renderer are lazy: the
  Api imports `help/help-files.ts` only (importing `help-links.ts` brought 324 kB of markdown-it and DOMPurify). About 380 kB is Angular (core, router,
  common, platform-browser) and rxjs, 151 kB the global CSS, the rest the listing page and the viewer.
- **Pages other than the listing** are routes with `loadComponent` (`/player` was eager until 2026-11-26).
- **Listing dialogs** are in `ListingDialogsComponent`, shown in `@defer (on immediate)` by the listing page: a chunk
  of their own fetched right after the first render. Callers open them with `dialogs.x()?.…` and menus read
  `Boolean(dialogs.x())` in `computed`s, so entries appear once the chunk has registered them.
- `@defer` only splits components listed **directly** in the deferring component's `imports`: a spread of an array
  from another file (`...LISTING_DIALOGS`, as it was) leaves them in the initial bundle without a word. Wrap them in
  a component and defer that.
- **The viewer stays eager** (it costs ~65 kB with `@use-gesture` and the video player): a click on a file opens the
  viewer only if it is registered (`ItemActions.view`), else the browser follows the link to the raw file; the
  reveal of a file from a link (`ListingStore`) also opens it at once. Deferring it needs those paths to wait for it.
- Not split, on purpose: the global CSS of the administration pages. `admin.css`, `access.css`, `tasks.css`,
  `settings.css` style classes the listing side uses too (`field-hint`, `settings-intro`, the `?` help, trickplay
  status); the clean ones (`logs.css`, `connections.css`, `access-tokens.css`) are ~8 kB.
- `@use-gesture/core/types` is in `allowedCommonJsDependencies`: an empty, types-only module that the build resolves
  to its CommonJS file.
