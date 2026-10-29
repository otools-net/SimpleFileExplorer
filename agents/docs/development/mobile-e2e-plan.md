# Mobile end-to-end tests: plan and progress (epic)

Goal: a suite of end-to-end tests that drives the real app (built server, real browser, seeded library) **the way a
phone does**, with real touch input, phone viewports and both engines (Chromium for Android, WebKit for iOS), and
fails on what users see: bars that jump, menus that open late or need a second tap, things off screen, taps that do
the wrong thing. It runs on demand and before each `edge` → `main` merge, and every phone-facing fix adds a scenario.

Started 2026-10-29 from user reports on the phone view, for example: *select an item, open its menu: the bar at the
bottom jumps, and the menu doesn't open at once*.

## How to use this file

- Tick `[x]` in the commit that does the work; what stays true goes into the notes named under "Done when".
- When nothing is left: delete this file and its links (see `AGENTS.md`, "Keeping these notes current").

Status legend: `[ ]` to do, `[~]` started, `[x]` done, `[-]` dropped (why).

## What exists, what is missing

- Vitest + jsdom specs: behaviour of components, **no layout, no touch, no timing** ([testing-client](testing-client.md)).
- `agents/tools/docs-screenshots/cdp.mjs` + `agents/tools/visual-check/*.mjs`: headless Edge at phone sizes, static
  layout checks (`narrow-audit`, `overflow`, `phone-sheets`, `context-menus`). They click with the mouse
  (`realClick`); `launch({ mobile: true })` turns on touch emulation but nothing sends touch events; no WebKit; no
  assertion over time (a bar that moves for 300 ms and comes back passes); not a test runner (no retries, traces,
  report); run by hand on the docs-screenshots data.
- Nothing runs them before a release.

## Decisions (take first)

- [x] **Everything in the repo, runnable on demand by any developer** (2026-10-29): specs, fixtures, helpers,
  config, global setup and `package-lock.json` are committed under `tests/e2e/`, with a `README.md` saying how to run
  them; one command (`npm run e2e` there) builds what it needs, starts its own app on a temp copy of the sample library
  and stops it. Nothing depends on the scratchpad, on a docs-screenshots run, or on state left by an earlier run.
  Reproductions of reported bugs (Phase 0) are specs too, not throwaway scripts.

- [x] **Runner: Playwright Test** (`@playwright/test` 1.64.1, pinned exactly; Apache-2.0), chosen over growing `cdp.mjs`: device
  profiles (`hasTouch`, `isMobile`, viewport, DPR), real touch (`page.touchscreen.tap`, CDP `Input.dispatchTouchEvent`
  for long press and swipes), **WebKit** (closest to iOS Safari, which sends no `contextmenu` and behaves differently
  with fixed bars and `100dvh`), traces / video on failure, parallel workers, an HTML report. Chromium from the
  installed Edge (`channel: 'msedge'`); WebKit downloaded once by `npx playwright install webkit` (test tooling only:
  [fully-local](fully-local.md) is about the client). Runs on this machine (2026-10-29).
- [x] **Where**: `tests/e2e/` with its own `package.json` (not in `src/Sfe.Web`: the client's deps stay the client's),
  `playwright.config.ts`, `fixtures/`, `specs/<area>.spec.ts`. Command `npm run e2e` there; documented in
  [commands](../environment/commands.md).
- [x] **Server under test**: `scripts/server.mjs` (Playwright's `webServer`) builds `Sfe.Api` (Debug, into a temp artifacts folder), copies
  `tests/samples/library` to a temp folder, starts the app on a free port with SQLite, a setup code, a scratch keys
  folder ([running-the-app](../environment/running-the-app.md)), creates the administrator and accounts through the
  API, scans, and stops everything at the end, also on failure (the machine has little RAM). No ffmpeg needed for the
  default run; a tagged `@ffmpeg` group for video thumbnails / trickplay / conversion.
- [~] **Isolation** (fresh context per test done; per-test folders when the first spec changes files): read-only specs share the library; specs that change files (rename, move, delete, upload,
  extract) get their own folder under the library per test (made through the API, removed after) so workers don't
  collide. Each test has a fresh browser context (storage, cookies).
- [x] **Projects (devices)**: `android` (Chromium, Pixel 7), `android-slow` (CPU 4×, `@timing` only), `android-small` (360×800),
  `iphone` (WebKit, 390×844, DPR 3, touch), `landscape` (Chromium, 800×360), `desktop` (Chromium 1280×800, a smoke
  subset tagged `@desktop` so phone fixes don't break the mouse path). Same target sizes as
  [overflow-menus](../features/overflow-menus.md).
- [ ] **What stays in `visual-check/`**: the static audits (`narrow-audit`, `overflow`) stay scripts; the checks that
  are really tests (`phone-sheets.mjs`, the phone part of `context-menus.mjs`) move into the suite and are removed
  from the scripts in the same commit.

## Phase 0: reproduce the reports (before building anything)

- [ ] On a real Android phone (Chrome) and an iPhone (Safari), on a seeded library: select an item (long press), tap
  its ⋮; write down exactly what moves (the selection bar? by how much? when?) and how late the sheet opens, or
  whether a second tap is needed. Screen recordings into the report folder (not the repo).
- [x] The same in emulation, as a committed spec (`specs/reported.spec.ts`): findings below.
- [ ] With Chrome remote debugging on the Android phone (`chrome://inspect`: Performance panel around the tap, layout
  shifts, long tasks).
- [~] Check these suspects one by one, write down which hold (`[x]` holds, `[-]` doesn't):
  - [x] Opening a sheet sets `html { overflow: hidden }` (`html:has(.sheet-backdrop)`, `styles/phone-sheets.css`):
    on Android Chrome that brings the hidden address bar back, the layout viewport shrinks, and the fixed
    `.selection-dock` (`bottom: max(20px, env(safe-area-inset-bottom))`, `styles/selection.css`) moves up; closing
    moves it back. Not seen in emulation (no address bar), but recorded in context-menu.md since 1.62 ("brings back
    Chrome's address bar"). **Fixed 2026-10-29:** on touch screens the root is left alone (backdrop `touch-action:
    none`, sheet `overscroll-behavior: contain`); tests: root overflow kept, swipes on sheet and backdrop don't scroll.
    Confirm on a phone. Modal dialog sheets (`dialog.sheet[open]`, `base.css`): same fix (dialog `touch-action:
    none`, body `overscroll-behavior: contain`; the body still scrolls), tests in `reported.spec.ts`. The viewer
    (`dialog.viewer[open]`) still locks the root: it covers the screen, nothing at the bottom to jump; left as is.
  - [x] Found on the way: tiles re-packed as their previews loaded (shape learned on load), so items moved under the
    finger in the first half second; a tap on ⋮ then hit another element (WebKit, 2 runs in 6). **Fixed:** tiles take
    the index's width/height at once (`PreviewsService.shapeOf`); test `listing.spec.ts`.
  - [-] The selection bar re-measures when the selection changes (`OverflowRow`, `fitGaps`): the long / short count
    and the "⋯" swap, the centred pill changes width and re-centres; or it is re-created and replays its `toast-in`
    animation. Steady within 1 px on every project.
  - [-] `padding-right: var(--scrollbar-w)` while a sheet is open shifts fixed content on a device with a scrollbar. Phones have
    overlay scrollbars (`--scrollbar-w` 0); a desktop concern only.
  - [-] The first tap is spent elsewhere: an `outside` `document:pointerdown` closing another popup, the 800 ms
    `contextmenu` swallow after a key (`context-menu.ts`), the long press's own `contextmenu`/`click` arriving after
    the finger lifts, or a tap on ⋮ while something is selected toggling the selection instead of opening the menu. One tap
    always opened it; the long press sends no click.
  - [ ] `ItemMenu` closes its menu when `ListingStore.generation` changes: a refresh (change feed, scan) right after
    opening closes it, looking like "didn't open".
  - [x] A long task on the tap (change detection over a big listing, overflow measuring, layout after `has()`):
    measure input → sheet painted. 165–460 ms at 4× CPU, mostly the app's script; the lock's `:has()` part of it.
  - [x] Found on the way: a tap on the sheet's backdrop closed it at the press, so its click reached what was under
    it (the sidebar button opened). **Fixed:** the backdrop closes on its click; test "a tap on the dimmed area…".
  - [x] Found on the way: tile captions were `content-visibility: auto` with a 72 px guess (real 54–124 px): tiles
    changed height after showing and a tap on ⋮ became a click on the link below (landscape). **Fixed:** captions
    always laid out (cheaper: `listing-cost.spec.ts`).
- [~] **Menu still slow at phone speed** (median ~180 ms at 4× CPU, target 100). Profile (`menu-profile.spec.ts`):
  Angular's render pass ~95 ms, of which `ContextMenu.place` ~45–55 ms (its `offsetWidth` forces the layout of the
  menu: 121 objects, 40–48 ms) and the menu's template ~30–40 ms; then paint/layerize 20–40 ms. Options, to decide:
  build the menu while the finger is down (`pointerdown`, shown on `click`), a lighter menu (fewer elements per
  entry), or accept it. The test holds it with ratchet budgets meanwhile.
- [~] Each confirmed cause gets a failing e2e scenario **before** its fix (the fixes are separate commits with
  changelog lines; this epic itself is tests only).

### Findings in emulation (2026-10-29)

Measured with `specs/reported.spec.ts` and `specs/diagnose/sheet-cost.spec.ts` (`SFE_E2E_DIAGNOSE=1
SFE_E2E_TIMINGS=1`), 5 runs each, select box then ⋮; floors (`harness.spec.ts` baseline): Chromium 11–19 ms, WebKit
1–3 ms.

- **The menu is late, and it is the app.** Tap to sheet on screen: Chromium 45–65 ms on this PC, **165–460 ms at
  4× CPU** (`android-slow`, about a mid-range phone); WebKit 85–470 ms. The browser's part is small (the click comes
  3–6 ms after the finger, no tap delay; 11–98 ms with 600 photos listed, a busy main thread); the time is the app's
  script between the click and the menu entering the page (100–400 ms at 4× CPU) and, on WebKit, style / layout /
  paint after it. `reported.spec.ts` fails on `android-slow` and `iphone` (budget 100 ms): the regression test.
- **The page lock costs part of it.** Removing `html:has(.sheet-backdrop)` (the sheet's lock): Chromium −20–40 ms
  on this PC; at 4× CPU the medians drop from 234 to 165 ms (6 photos) and 391 to 218 ms (600). WebKit too noisy
  to tell. Next: a CPU profile of the ⋮ click (`ContextMenu.toggle`, `ItemMenu.entries`, the menu's render and
  positioning) to find the rest.
- **The bar jump is not reproduced** in a fixed viewport: the selection bar stays within 1 px through open and close
  on every project (`watchBoxes`). The suspect left is the browser's address bar coming back when the lock sets
  `overflow: hidden` (Android Chrome), which emulation doesn't have: confirm on a phone (steps above) or with
  Playwright's Android support over adb; then emulate it (viewport helpers, Phase 1) for a regression test.
- Ruled out: a 300 ms tap delay (`width=device-width` is set; the click comes at once); the long press's own events
  (no click after it in the gesture); the first tap lost (one tap always opened it).

## Phase 1: harness

- [x] `tests/e2e/` package, Playwright pinned, config with the projects above, `npm run e2e`,
  `npm run e2e -- --project=iphone`, `--grep @smoke`.
- [x] Global setup / teardown: build, library copy, app start, wait for `api/config`, setup + accounts
  (`admin`, a limited account, a guest), scan, a `storageState` per account so tests start signed in.
- [x] Fixture (`fixtures/test.ts`): signed-in page per account (`account` option); `openFolder(page, path)`; console errors, page errors, CSP violations
  (`securitypolicyviolation`) and failed requests (4xx/5xx not expected by the test) **fail the test**.
- [~] Touch helpers (`fixtures/touch.ts`): `locator.tap()`, `longPress`, `swipe` done; `pinch` left;
  no-`click()` check left (CDP `Input.dispatchTouchEvent` on Chromium; WebKit:
  `touchscreen.tap` + what Playwright offers, gaps written down). Never `click()` in phone specs (a lint rule or a
  grep in the suite's own check).
- [x] Stability helper (`watchBoxes(...).expectSteady()`) `expectSteady(locators, during: () => action, { ms: 600, tolerance: 1 })`: samples the
  boxes of fixed elements (selection dock, top bar, location bar, operations panel, toasts) on every animation frame
  before, during and after the action; fails with the frames where one moved, and how far. This is the test for
  "the bar jumps".
- [x] Responsiveness helper `expectOpensWithin(action, locator, { ms })`: time from the touch end to the element
  visible and stable (`requestAnimationFrame` after it is laid out), budget **100 ms** for menus and sheets, 300 ms
  for dialogs that load data; also asserts **one** tap was enough. Long tasks (`PerformanceObserver` `longtask`, on
  Chromium) over 50 ms during the action are reported (left). CPU slowed 4× (CDP `Emulation.setCPUThrottlingRate`):
  done, project `android-slow` (`@timing` specs). The time is split at the click and at the element entering the page.
- [ ] Viewport helpers: address bar shown / hidden (a height-only resize, as Chrome does), on-screen keyboard
  (height shrink + `visualViewport` resize while a field has the focus), orientation turn (swap width and height),
  safe-area insets (CDP `Emulation.setSafeAreaInsetsOverride` if this Edge has it; else a CSS override and a by-hand
  check on an iPhone).
- [~] Layout helpers (`fixtures/layout.ts`): `expectFitsScreen` (no sideways scroll), `expectTapTargets` (fails
  under 24 px, WCAG 2.2 AA; under 44 listed in the report). Off-screen and cut text from `narrow-audit.mjs`: left.
- [x] On failure: trace, video, screenshot; report folder in the temp folder, not the repo.
- [x] A first `@smoke` spec (sign in, list a folder, open the item menu sheet, close it) green on every project.
- [x] Note [testing-e2e](testing-e2e.md): how to run, fixtures, helpers, gotchas; a line in `AGENTS.md`;
  [testing](testing.md) points to it.

## Phase 2: scenarios (each on `android`, `android-small`, `iphone`, `landscape` unless marked)

Every scenario also runs the cross-cutting checks of Phase 3. Rights-dependent ones also as the limited account.

### Listing and selection — [selection-and-keys](../features/selection-and-keys.md), [listing-page](../features/listing-page.md)

- [x] **The report:** long press an item → selected, selection bar appears **once** (no replayed animation); tap ⋮ on
  it → sheet within 100 ms on the first tap; the selection bar does not move (`expectSteady`) while the sheet opens,
  is open, and closes; backdrop tap closes; selection kept.
- [x] Long press selects and opens no menu, no browser menu, no text selection, no callout (iOS); lifting the finger
  doesn't also open the file.
- [x] In selection mode a tap toggles files and folders (folders don't navigate); tapping ⋮ opens the menu, not a
  toggle.
- [x] Select all, Clear; the count updates; the bar fits and its buttons are ≥ 24 px; the bar doesn't cover the
  last row (`selection.spec.ts`). Safe area: not emulated (see viewport helpers).
- [ ] **Decide:** the selection bar's buttons are 30 px tall on phones (Clear, Select all, Tags, "⋯": 30×30 to
  78×30): above WCAG AA's 24, under Apple's / AAA's 44. Larger on touch screens (`pointer: coarse`)?
- [ ] Tiles, list and cards views: tap opens a file / folder; scroll by swipe, infinite scroll loads more, scroll
  position restored on Back ([position-restore](../features/position-restore.md)).
- [x] Address bar shown / hidden (height-only resizes): the selection bar stays at the bottom
  (`selection.spec.ts`); a swipe scrolls and selects nothing.
- [ ] A refresh from the change feed while the menu is open: the menu's behaviour is what we decide (stays, or
  closes visibly), never silently gone.

### Menus and sheets — [context-menu](../features/context-menu.md), [frontend](frontend.md) (phone sheets)

- [ ] Item menu, folder menu (location bar "⋯"), viewer ⋮, overflow "⋯" of each row, player speed menu, default tags
  panel, tag chip menu: each a sheet, full width, against the bottom, backdrop under, page locked, opens in ≤ 100 ms
  on one tap.
- [ ] A sheet longer than the screen scrolls inside itself; the page behind doesn't scroll (swipe on the backdrop
  and on the sheet).
- [ ] Submenus (Speed, view switch) open in place; Back returns.
- [ ] Closing: backdrop tap, a chosen entry, Android Back (history) if we decide it should, orientation turn; never
  on the scroll that the lock itself causes.
- [ ] After closing: the scroll position and the selection are as before; focus doesn't pop the keyboard.
- [ ] iOS: every function reachable without `contextmenu` (⋮ buttons everywhere a menu exists).

### Top bar, location bar, search — [location-bar](../features/location-bar.md), [search-history](../features/search-history.md)

- [ ] Search field: tap focuses, keyboard opens, the page doesn't zoom (font ≥ 16 px on iOS), recent searches and tag
  suggestions are tappable above the keyboard, Enter searches, results listed.
- [ ] Location bar sticky while scrolling, its "⋯" opens the folder menu, breadcrumbs scroll sideways, up / previous
  / next folder.
- [ ] Main menu (⋮ in the top bar): opens from the top, every link works, closes on a tap outside.
- [ ] Quick links on a phone: open, Move to… instead of drag.

### Viewer — [video-player](../features/video-player.md), [hover-preview](../features/hover-preview.md)

- [ ] Open a picture by tap; swipe left / right to the next / previous file; swipe down or ✕ to close; the listing
  behind keeps its scroll position.
- [ ] Picture: double tap / pinch zoom, pan with a finger (`touch-action: none` on the stage), no page scroll behind.
- [ ] Video `@ffmpeg`: tap shows controls, double tap seeks, the controls row on one line with its "⋯", full screen
  and back, rotation in full screen, hold-to-preview on a tile (trickplay on touch).
- [ ] Audio: play / pause, the bar reachable above the home indicator.
- [ ] Documents: text (scroll, find, Aa panel, long lines sideways), Markdown, JSON, CSV (virtual rows by swipe), PDF
  (pinch / zoom buttons, find), EPUB (tap zones / swipe to turn pages), archive viewer (rows, Go to, open an entry).
- [ ] `view?path=` page on a phone: same checks as in the viewer.

### File operations — [new-folder-and-rename](../features/new-folder-and-rename.md), [move-and-copy](../features/move-and-copy.md), [uploads](../features/uploads.md), [trash](../features/trash.md)

- [ ] New folder, Rename, Batch rename: the dialog is a sheet; the field above the keyboard; buttons reachable with
  the keyboard open; Enter confirms.
- [ ] Move to… / Copy to… with the folder picker by touch; the operations panel doesn't cover the selection bar or
  the last row.
- [ ] Upload from the phone (file chooser: `setInputFiles`), progress, the file listed.
- [ ] Delete → trash, Restore; Compress / Extract dialogs on a phone.
- [ ] Text editor: edit, Save, the unsaved-changes guard on Back.

### Tags — [tags](../features/tags.md), [scope-tagging](../features/scope-tagging.md)

- [ ] Tag the selection from the bar: picker opens, keyboard and suggestions, chips added; tag a whole folder.

### Accounts, administration, other pages

- [ ] Sign in / out on a phone (keyboard, password manager field types, no zoom on focus); setup page.
- [ ] Administration: section chips scroll sideways, each section usable at 360 px, settings saved (a smoke per
  section, not every field).
- [ ] Tasks, logs, duplicates, similar videos, missing files: lists scroll, row buttons reachable.
- [ ] Server lost / back while on a phone (the ⋮ warning), a session that expired (redirect to sign in, back after).
- [ ] Light and dark theme: the first paint has the right theme (no flash), sheets readable in both.

## Phase 3: checks on every scenario (in the fixture, not repeated by hand)

- [ ] No console error, page error, CSP violation, unexpected failed request.
- [ ] Page no wider than the screen; nothing interactive off screen or under the home indicator.
- [ ] Fixed bars steady across the scenario's actions (`expectSteady` on the dock, top bar, location bar).
- [ ] One tap = one action: no double fire (tap opening and the long press also selecting), no tap needing a second
  one.
- [ ] Tap targets ≥ 44×44; nothing reachable only by hover or right click.
- [ ] No focus that pops the keyboard unasked (a sheet focusing its first field on open, on phones).
- [ ] Optional `@a11y` run: `@axe-core/playwright` on each screen's main states (labels, contrast, roles).

## Phase 4: in the workflow

- [ ] Run time: the whole suite under ~10 min on this machine with workers; `@smoke` under 2 min.
- [ ] Rule in `AGENTS.md` and [testing](testing.md): a change visible on phones runs `npm run e2e -- --grep @smoke`
  plus its area's specs; a phone bug fix adds a scenario that fails without it; the full suite before merging `edge`
  into `main` ([versioning-and-changelog](versioning-and-changelog.md)).
- [ ] CI: a GitHub workflow (next to `.github/workflows/docker-image.yml`) running `@smoke` on `android` and `iphone`
  on pushes to `edge`, the full suite on pull requests to `main`; traces uploaded as artifacts.
- [ ] Flakiness: no fixed sleeps (wait on states), retries 1 in CI only, a test failing twice in a week is fixed or
  quarantined with a bullet here.
- [ ] By hand before each release (what emulation can't see): the "Reproduce" steps of Phase 0 on a real Android
  phone and an iPhone, the address bar and home indicator, the keyboard, rotation; a short checklist in
  `testing-e2e.md`.
- [ ] Later, not in this epic: Playwright's experimental Android support (`_android`, a real phone over adb) for the
  `@smoke` set; visual comparisons (`toHaveScreenshot`) for sheets and bars once the layout is stable.

## Production

- Tests and tooling only: no database, stored-file, cache or setting change; no version bump or changelog line for
  the suite itself ([versioning-and-changelog](versioning-and-changelog.md)). Each bug it finds is fixed in its own
  commit with its changelog line and, where user-visible, `docs/`.
- The suite never touches a real library: always the copy of `tests/samples/library` in a temp folder.

## Done when

- The reported bugs (bar jumping, menu late) have failing-then-passing scenarios on `android` and `iphone`.
- Phases 1–4 ticked or dropped with a reason; `testing-e2e.md` holds how it works; the moved `visual-check` scripts
  are gone; this file is deleted.
