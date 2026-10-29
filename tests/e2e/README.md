# End-to-end tests (phones first)

The real app, built from this checkout, in real browsers sized and driven like phones (touch, Android Chromium, iOS
WebKit). Plan and progress: `agents/docs/development/mobile-e2e-plan.md`.

## Run

Needs Node 22+, the .NET SDK, and Microsoft Edge on Windows (elsewhere Playwright's Chromium).

```sh
cd tests/e2e
npm ci                       # once, and after package.json changes
npm run browsers             # once: Playwright's WebKit (iPhone) and Chromium

npm run e2e                                     # everything: builds the app, starts it, runs, stops it
npm run e2e -- --project=android                # one device: android, android-small, iphone, landscape, desktop
npm run e2e -- --grep @smoke                    # the quick set
npm run e2e -- specs/reported.spec.ts           # one file
SFE_E2E_SKIP_BUILD=1 npm run e2e                # reuse the last build (no client or server change since)
npx playwright show-report <tmp>/sfe-e2e/report # the HTML report, with traces and videos of failures
```

Each run starts from scratch: `scripts/server.mjs` builds `Sfe.Api` (client included), copies
`tests/samples/library`, and starts the app on it on port 5181 (External mode, SQLite); `setup/global-setup.ts`
creates the accounts, scans and saves their sign-ins. The app stops when the run ends.

| Environment | |
|---|---|
| `SFE_E2E_WORK` | work folder (build, library copy, database, report, traces); default `<tmp>/sfe-e2e` |
| `SFE_E2E_PORT` | the app's port (default 5181) |
| `SFE_E2E_SKIP_BUILD=1` | use the build already in the work folder |
| `SFE_E2E_CHANNEL` | Chromium channel (default `msedge` on Windows) |
| `SFE_E2E_WORKERS` | parallel workers (default half the cores) |

## Layout

| Path | |
|---|---|
| `playwright.config.ts` | devices (projects), server, reports |
| `paths.ts` | work folder, port, accounts |
| `scripts/server.mjs` | build + library + app, run by Playwright's `webServer` |
| `setup/global-setup.ts` | setup, scan, accounts (`admin`, `sam`), sign-in states |
| `fixtures/test.ts` | the `test` to import: signed in (`test.use({ account: 'sam' })`), fails on console errors, page errors, CSP violations, 5xx; `openFolder`, `item` |
| `fixtures/touch.ts` | `longPress`, `swipe` (Chromium: DevTools touch events), `needsTouchGestures` |
| `fixtures/steady.ts` | `watchBoxes(page, { name: selector }).expectSteady()`: fails when a box moves between frames |
| `fixtures/timing.ts` | `expectOpensWithin(page, selector, tap, ms)`: one tap, shown within the budget |
| `specs/` | the tests, one file per area; `reported.spec.ts` holds user-reported bugs |

## Writing a spec

- Import `test`, `expect` from `fixtures/test`, never from `@playwright/test`.
- Phones are touched: `locator.tap()`, `longPress`, `swipe`; `click()` only in `@desktop` code paths.
- Wait on states (`expect(...).toBeVisible()`), not on time; the only waits on time are inside the stability and
  timing helpers, which measure over time.
- A spec that changes files works in a folder of its own (made and removed by the spec): workers run in parallel on
  one library.
- Tags in the title: `@smoke` (the quick set), `@desktop` (also on the desktop project), `@desktop-only`.
- A phone bug fix adds a spec that fails without the fix.
