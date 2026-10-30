# Flash viewer

Code: client `app/formats/flash/` (`flash-player.ts`: the frame behind `FLASH_LOADER`; `flash-view.component.ts`),
`FLASH_FORMAT` + `setFlashMovies` in `formats/formats.ts`, `angular.json` (assets `flash/ruffle/`), the frame's page
`public/flash/player.html` + `player.js`, the Flash part of `styles/reader.css`. Server: `Security/FlashLinks.cs`,
`Endpoints/FlashEndpoints.cs` (`POST api/files/flash-links`, `GET api/files/flash/{token}/{**entry}`),
`SecurityHeaders.FlashPolicy`, setting `Player:Flash`. Tests: `FlashRouteTests`, `flash-view.component.spec.ts` (a
fake loader: jsdom runs no WebAssembly), `flash-player.spec.ts` (messages), a row in `formats.spec.ts`; the real Ruffle
runs in the docs screenshots (scene `flash`, `make-swf.mjs`). User docs: `docs/user-guide/viewer.md#flash-movies`,
`docs/administration/playback.md#flash-movies`.

## Why Ruffle (decided 2026-10-25)

- The only maintained Flash Player emulator for the web (Rust compiled to WebAssembly), licence MIT OR Apache-2.0,
  a self-hosted build that needs no bundler.
- Before building it, a spike (a Node server with the planned headers, hand-made movies, headless Edge) proved the
  frame of no origin works: scripts and `.wasm` load under `'self'` from the opaque frame, no CSP violation, requests
  carry no cookie, sibling movies load with `base`, `javascript:` and `_blank` do nothing.
- Not done, maybe later: a thumbnail of the first frame (Ruffle's desktop build can render headless; ffmpeg can't play
  ActionScript), `.spl` (FutureSplash), movies' saved games (`SharedObject`: the frame has no storage).
- The epic was closed by the user on 2026-10-30. Never checked by an agent (headless Firefox started from the agent's
  shell fetched nothing): Firefox; touch input and Ruffle's virtual keyboard on phones; full screen, sound after the
  opening click, keyboard in the frame and Esc passed on, in a real browser. Look there first if a report comes in.

## Ruffle, bundled and local

- **`@ruffle-rs/ruffle`** (MIT OR Apache-2.0), **pinned exactly** (a release changes its hashed file names and
  behaviour). It is Ruffle's *self-hosted* build: a ready `ruffle.js` that loads its chunks and `.wasm` from its own
  folder. `angular.json` copies `ruffle.js`, `core.ruffle.*.js` and `*.wasm` (no maps) to `flash/ruffle/`; Angular
  doesn't bundle it.
- Two `.wasm` of ~14 MB (≈5 MB compressed, `MapStaticAssets`); a browser fetches one (with or without WebAssembly
  extensions). Packages are ~29 MB larger.
- Its texts name `ruffle.rs` and `github.com` (links of its error panel, never fetched): allowed in
  `ApiTests.WebClientHasNoExternalResources`.
- Compatibility (ruffle.rs): ActionScript 1/2 nearly all, ActionScript 3 most; what fails shows Ruffle's panel or our
  message with Download.

## A frame of no origin

- A movie's ActionScript can make requests (`URLLoader`, `loadVariables`, headers). On the app's origin it would send
  the session cookie and could add `X-SFE-Request`: any SWF in a library could act as whoever opens it. So Ruffle runs
  only in `flash/player.html`, framed with `sandbox="allow-scripts allow-pointer-lock"` (no `allow-same-origin`,
  no `allow-popups`), and the page's own CSP sandboxes it too (opened alone, it still has no origin).
- `SecurityHeaders`: `FlashPolicy` (`'wasm-unsafe-eval'`, which WebAssembly needs, on this page only; `'self'` still
  matches the app in an opaque frame) and `X-Frame-Options: SAMEORIGIN` + `frame-ancestors 'self'` for
  `/flash/player.html`; `Access-Control-Allow-Origin: *` for `/flash/ruffle/*` (the frame fetches the `.wasm` in CORS
  mode). Every other answer is unchanged.
- From the frame, requests carry `Origin: null` and no cookie (`SameSite=Lax`); a non-GET would need a CORS preflight
  the app never grants. `localStorage` throws there: Ruffle copes (no saved games across visits).

## Flash links

- The movie can't use `api/files/raw` (cookie): `POST api/files/flash-links` (`files.view` at the movie, `.swf` only,
  404 with `Player:Flash` off) answers `api/files/flash/{token}/{name}`. `FlashLinks`: tenant, user, security stamp,
  the movie's **folder**; 12 hours; its own Data Protection purpose (an external player's token doesn't work here, nor
  the reverse).
- `GET api/files/flash/{token}/{**entry}` (anonymous, `RightMetadata` `files.view`): the account as it is now (stamp,
  disabled), `files.view`, the library's `Player:Flash`, then `entry` under the folder (`NormalizeRelative`: no `..`;
  subfolders open, nothing above), not the server's files nor the work folder, folder rules at that file (hidden → 404).
  `ServeFile` (sandbox CSP), `Access-Control-Allow-Origin: *` on every answer.
- Ruffle resolves a movie's relative addresses against the page, as Flash Player did: `player.js` loads with
  `base` = the movie's folder, so `loadMovieNum("level2.swf")` reads `…/{token}/level2.swf`.

## The frame and the view

- `player.js` sets Ruffle's config: `polyfills: false`, `allowScriptAccess: false`, `allowNetworking: "internal"`,
  `openUrlMode: "deny"`, `socketProxy: []`, `upgradeToHttps: false`, `splashScreen: false`, `showSwfDownload: false`,
  `autoplay: "on"`, `unmuteOverlay: "visible"`, `contextMenu: "rightClickOnly"`, `letterbox: "on"`.
- Messages (`postMessage`, `'*'`: the frame's origin is `"null"`): the frame hears its `parent` only and plays only
  URLs of its own address under `/api/files/flash/`; `openFlash` hears its frame's `contentWindow` only. To the frame:
  `load { url, volume }` (also "play again"), `play`, `pause`, `volume { value }`, `focus`. Back: `ready`, `loaded`,
  `failed { message }`, `metadata`, `state { playing }` (Ruffle has no play/pause event: `isPlaying` checked every
  500 ms), `key { Escape }`.
- The view: play / pause and full screen stay in the bar, play again leaves into "⋯", the volume slider leaves without
  an entry (mute stays). Volume and mute per browser (`sfe.flash.volume`, `sfe.flash.muted`). Status: ActionScript
  version, SWF version, size, frame rate.
- Full screen is asked by the view on the stage around the frame (a request from the frame would need a gesture
  inside it). Esc in the movie comes back as `key`, and the view dispatches an Escape keydown on its host: the viewer
  takes the focus back, the next Esc closes it.
- Destroying the view removes the frame: the sound stops at once.
- `FLASH_FORMAT` matches `.swf` (kind `file`) only while `config.flash` (setting on and `files.view`), set by
  `ConfigService.load` through `setFlashMovies`, as `setTextExtensions`.

## Gotchas

- `ng serve`: the frame's page comes from the dev server, without `FlashPolicy` (still sandboxed by the iframe's
  attribute); its `headers` option (`angular.json`) adds `Access-Control-Allow-Origin: *`, without which the opaque
  frame can't fetch Ruffle's `.wasm`.
- jsdom: an `<iframe>` with a `src` logs "Not implemented: navigation to another Document" (harmless).
- `make-swf.mjs`: a shape's fill on the inside of edges drawn clockwise (on screen) is fill style **1**.
