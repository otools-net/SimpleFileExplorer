# Client test gotchas (Vitest + jsdom)

Rules and helpers: [testing.md](testing.md). Code: `src/Sfe.Web/src/testing/setup.ts`, `fakes.ts`.

- Angular runs Vitest with `isolate: false`: a worker's spec files share globals (window, storage, prototypes) and
  `src/testing/setup.ts` runs again for each file; which files share a worker varies per run, so a leak fails "only
  sometimes". setup.ts must replace what it installs (a `??=` IntersectionObserver once made `intersect()` miss every
  observer), clears storage before each test and restores `isSecureContext`, `scrollY`, `navigator.clipboard`,
  `document.execCommand`: add any other global a spec redefines to its `patchedGlobals`.
- Specs run in a zoneless TestBed: `await settle(ms)` lets timers, promises and change detection run. It waits on a
  real timer, so it hangs under `vi.useFakeTimers()`: use `vi.advanceTimersByTimeAsync` + `fixture.detectChanges()`
  there.
- An `async` helper must not return the promise under test (it gets awaited): wrap it, `{ result }`.
- jsdom sanitises `type=number` values (`' 300 '` becomes `''`), resets `cancelBubble` after dispatch, and has no modal
  `<dialog>`, `matchMedia`, `IntersectionObserver` or `isSecureContext`: `setup.ts` adds stand-ins, driven by
  `setMediaQuery` / `intersect` from `fakes.ts`.
- jsdom doesn't implement `HTMLMediaElement` `play`/`pause`/`load`: `video-player.component.spec.ts` stubs them on the
  prototype and calls `TestBed.resetTestingModule()` before `vi.restoreAllMocks()` (the player pauses on destroy).
- Tag picker press flags: see [tags.md](../features/tags.md).
- One spec file: `npm test -- --include src/app/account/account.spec.ts`; plain `npx vitest run` fails (no Angular
  build: `history is not defined`).
- Use dotless host names in specs ([fully-local.md](fully-local.md)).
- Settings schema field order is read by a backend test: see [settings-new-key.md](../features/settings-new-key.md).
- CodeMirror runs in jsdom with the real editor; `setup.ts` stubs `Range.getClientRects`/`getBoundingClientRect`,
  which it calls a frame after a change (else an unhandled "getClientRects is not a function" fails the run).
- `confirm` (unsaved changes) is jsdom's "not implemented" stub returning nothing: spy on `window.confirm` in specs.
- An `<iframe>` with a `src` logs jsdom's "Not implemented: navigation to another Document": harmless (the Flash
  view's `flash-player.spec.ts`); its `contentWindow` exists, and a message "from it" is a `MessageEvent` dispatched on
  `window` with `source: frame.contentWindow`.
