# Visual checks

Scripts that look at the real page in headless Edge (driver: `../docs-screenshots/cdp.mjs`), for layout bugs unit
tests can't see. Each needs a docs-screenshots run first (`node agents/tools/docs-screenshots/run.mjs ...`): it reuses
its build and seeded data in `<tmp>/sfe-docs` and starts its own app on another port. After a client change, rebuild
that copy first: `dotnet build src/Sfe.Api --artifacts-path <tmp>/sfe-docs/build` (run it twice if the first fails on
a missing `chunk-….js`).

| Script | Checks |
|---|---|
| `context-menus.mjs [<out prefix>]` | Real right clicks (CDP) on a file, a folder, a breadcrumb and the background, the ContextMenu key and Shift+F10 (the browser's echo swallowed), Shift and a text field keeping the browser's menu, Open in new tab opening a tab, the sheet at 390 px; prints `ok`/`FAIL` per check (exit code 1 on a failure), saves `<prefix>-file.png`, `-background.png`, `-phone.png` |
| `phone-sheets.mjs <out prefix>` | 390 px, classic scrollbars, list view: the item menu and a dialog sheet span the whole width (left 0, right 390), the rows keep their width and the scroll position stays; prints the numbers and saves `<prefix>-menu.png`, `<prefix>-dialog.png` |
