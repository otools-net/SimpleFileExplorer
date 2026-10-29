# End-to-end tests (Playwright, phones first)

Code: `tests/e2e/` (how to run: its [README](../../../tests/e2e/README.md)). Rules: [testing](testing.md). Plan and
open work: [mobile-e2e-plan](mobile-e2e-plan.md).

- `cd tests/e2e && npm run e2e` builds `Sfe.Api` into `<tmp>/sfe-e2e/build`, copies `tests/samples/library` (plus
  `Many photos/`: 600 copies of a 20 KB photo, made per run), starts the app on :5181 (External mode, SQLite) through
  Playwright's `webServer`, which stops it after; `setup/global-setup.ts` makes `admin` and `sam`, scans, saves their
  sign-ins. `SFE_E2E_SKIP_BUILD=1` reuses the build: ~10 s instead of ~45 s before the first test.
- Devices (projects): `android` (Pixel 7, Edge), `android-small` (360 px), `android-slow` (CPU 4× slower, only
  `@timing` specs), `iphone` (WebKit, iPhone 13), `landscape` (800×360: menus are popups, not sheets), `desktop`
  (only `@desktop` specs).
- Playwright 1.64.1 pinned exactly; Chromium is the installed Edge (`channel: 'msedge'`), WebKit is downloaded once
  (`npm run browsers`, ~60 MB in `%LOCALAPPDATA%\ms-playwright`).
- Long press: raw `Input.dispatchTouchEvent` start/end gives no `contextmenu` and a `click` after it (unlike a phone);
  `Input.synthesizeTapGesture` with a duration gives no click but still no `contextmenu` in headless Chromium on
  Windows. `longPress` uses the gesture and dispatches a `contextmenu` (`pointerType: 'touch'`) from the page at
  500 ms: untrusted, so the browser's own menu isn't covered. `harness.spec.ts` checks the sequence.
- WebKit has no long press or swipe in Playwright (no DevTools protocol): `needsTouchGestures` skips those specs
  there. iOS sends no `contextmenu` anyway; iPhone specs select with the select box.
- `expectOpensWithin` splits the time at the `click` (browser: a double-tap delay would show here) and at the element
  entering the page (`MutationObserver`; before: the app's script, after: style, layout, paint). Use
  `performance.now()` inside the frame, not the frame's time stamp (it can predate the element). Floors measured by
  `harness.spec.ts` (`baseline`): Chromium 11–19 ms, WebKit 1–3 ms.
- WebKit on Windows paints in software: its absolute timings swing (30–430 ms for the same menu); compare with the
  baseline and Chromium, confirm on an iPhone.
- `about:blank` in the iPhone project lays out 980 px wide and a tap there never resolved: serve a test page from the
  app's origin with `page.route` and a viewport meta (`baseline` test).
- The guard (`fixtures/test.ts`) fails a test on console errors, page errors, CSP violations and 5xx; Edge prints
  `LoadEnclaveImageW failed, error code 577` on its own stderr (not the page's console): harmless.
- `@diagnose` specs (`specs/diagnose/`) measure rather than assert; they skip unless `SFE_E2E_DIAGNOSE=1`.
  `SFE_E2E_TIMINGS=1` prints each `expectOpensWithin` (project | test | split).
- WebKit (Playwright on Windows) sends a tap's `click` to what was under the finger before a scroll that just happened,
  Playwright's own retry scrolls included: use `tapWhenSettled` after scrolling (`fixtures/touch.ts`). Not known for a
  real iPhone.
- `expectOpensWithin` prints the tap's events when nothing shows (`pointerdown`/`touchend`/`click` targets, the
  element in and out): a `click` on another element than the `pointerdown` means the page moved under the finger.
- Timings swing on a loaded machine (single taps 80–600 ms at 4× CPU): `expectMedianOpensWithin` takes the median of
  5. Budgets per project are ratchets (`MENU_BUDGET` in `reported.spec.ts`); WebKit under the full suite's load ran
  ~1.5× its alone time.
- The setup dismisses its scan's panel in the saved browser states (`sfe.scan.dismissed`): else it shows above the
  listing in every test. The old wait read `isRunning`, which the status hasn't (`state`): it never waited.
- Diagnostics: `menu-trace` (kinds of work, layout objects), `menu-profile` (functions, source-mapped: build first with
  `node scripts/profile-client.mjs`; Playwright's injected `visitNode`, `_shadowRoot`… are its own checks, not the
  app), `sheet-cost`, `listing-cost` (Chromium metrics, what-if CSS).
- `locator.tap()` scrolls its target "into view" first; on a sticky element (the location bar's links) that moved
  the page, and the app then saved that position: tap what a finger sees with `page.touchscreen.tap(x, y)` at its
  box. `filter({ visible: true })` means CSS-visible, not on screen: pick by `boundingBox`.
- A horizontal swipe with nothing to scroll sideways is Chrome's swipe back (overscroll history navigation): in the
  viewer it left the page (`about:blank` in a test: the document gone, `window` globals undefined). Fixed with
  `overscroll-behavior-x: none` while the viewer is open (base.css).
- Helpers besides touch and timing: `layout.ts` (`expectFitsScreen`, `expectNothingCut`: controls cut by a box that
  doesn't scroll or past the screen's edge, an ellipsis and a sideways scroller being fine; `expectTapTargets`;
  `expectNoZoomOnFocus`), `viewport.ts` (`setAddressBar`, `openKeyboard` + `expectFocusedFieldShown`, `turn`,
  `setSafeArea` + `expectClearOfInsets`), `touch.ts` `pinch` (Chromium). `expectOpensWithin` lists long tasks
  (Chromium's `longtask`) in its report line.
- The keyboard is emulated by a shorter viewport (as `interactive-widget=resizes-content`); Chrome and Safari shrink
  only the visual viewport, which no test can do: the real one is on the by-hand list.
- Safe areas: `Emulation.setSafeAreaInsetsOverride` works in this Edge (`env(safe-area-inset-*)` takes the values;
  `expectClearOfInsets` checks that first); WebKit in Playwright has no equivalent (skipped).
- No mouse in phone specs: `harness.spec.ts` fails on `.click(`, `.dblclick(`, `.hover(`, `mouse.*(` in specs and
  fixtures unless the line ends with `// mouse: <why>` (the desktop project's branch).
- `npm run typecheck` in `tests/e2e`: Playwright strips the types without checking them.
- WebKit (Windows) gives a page reloaded by Back `history.state` null (Chromium: the state written before leaving), so
  listing positions saved there are lost; `navigation.spec.ts` marks it `fixme` on WebKit until checked on an iPhone.

## Running it in the work

- Times on this machine: the quick set (`--grep "@smoke|@reported"`, android + iphone) ~50 s; everything (~245
  tests, 6 devices) ~7.5 min with the default workers; the first run of a session adds the build (~45 s).
- CI: `.github/workflows/e2e.yml`, the quick set on pushes to `edge`, everything on `main` and by hand; failures keep
  the report, traces and app log as the run's artifact. CI retries a failed test once (`retries` in the config).
- Flaky tests: no fixed sleeps outside the measuring helpers; a test failing twice in a week without a change is
  fixed or marked `test.fixme` with the reason and a line in the plan, never deleted silently.
- `test.fixme` / `test.skip` always say why (a browser that can't, a width where it doesn't apply).

## By hand on real phones (before a release; emulation can't see these)

- Android Chrome and an iPhone, on a library of a few hundred photos, scrolled down so the address bar is hidden:
  select an item (long press on Android, its box on iPhone), open its ⋮ menu: the bar at the bottom stays, the
  address bar stays hidden, the menu opens at once; a dialog (Rename) the same.
- The on-screen keyboard: Rename, search and tags fields stay visible above it, their buttons reachable; no zoom.
- The viewer: swipes between photos (never back a page), pinch on a picture, a video's Play, full screen and turning
  the phone; a PDF and an EPUB by finger.
- Back on the iPhone after leaving SFE for another site: the listing comes back where it was (WebKit on Windows loses
  it, `navigation.spec.ts`).
- The home indicator and notch (safe areas): nothing under them in portrait and landscape.

## Possible next steps (not planned)

- Playwright's experimental Android support (`_android`, a real phone over adb) for the `@smoke` set.
- Visual comparisons (`toHaveScreenshot`) for sheets and bars, once the layout is stable.
