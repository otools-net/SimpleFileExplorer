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
