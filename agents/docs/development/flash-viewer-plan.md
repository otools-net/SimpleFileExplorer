# Flash (SWF) viewer: plan and progress (epic)

Goal: `.swf` files open in the viewer and on their own page (`view?path=`) like PDF and EPUB, played by
[Ruffle](https://ruffle.rs) (a Flash Player emulator in Rust compiled to WebAssembly), served by the app, with the
movie unable to act as the signed-in user. How it is built: [flash-viewer](../features/flash-viewer.md); this file
keeps what is left and why Ruffle was chosen.

## How to use this file

- Tick `[x]` in the commit that does the work; what stays true goes into the feature note.
- When nothing is left: delete this file and its links (see `AGENTS.md`, "Keeping these notes current").

Status legend: `[ ]` to do, `[~]` started, `[x]` done, `[-]` dropped (why).

## Is Ruffle viable? (investigated 2026-10-25)

Yes, in a sandboxed frame.

- `@ruffle-rs/ruffle` 0.7.1 (stable; nightlies daily), MIT OR Apache-2.0, a self-hosted build (no bundler needed).
- Compatibility (ruffle.rs/compatibility): AS1/AS2 language 99 %, API 82 %; AS3 language 90 %, API 82 %.
- WebAssembly needs `'wasm-unsafe-eval'`, which the app's policy doesn't have (the PDF viewer kept it out with
  `useWasm: false`): given to the frame's page only.
- A movie can make requests: on the app's origin it would act as the user, hence the frame of no origin and flash
  links. A spike (Node server with the planned headers, hand-made movies, headless Edge) proved it: scripts and
  `.wasm` load under `'self'` from the opaque frame, no CSP violation, requests carry no cookie, sibling movies load
  with `base`, `javascript:` and `_blank` do nothing.

## Done (2026-10-25)

- [x] Server: `FlashLinks`, `FlashEndpoints`, `SecurityHeaders.FlashPolicy`, `Player:Flash`, `/api/config` `flash`;
  `FlashRouteTests`; attack vectors in [security](security.md).
- [x] Client: Ruffle pinned and copied to `flash/ruffle/`, the frame's page, `FLASH_FORMAT`, the view, the bridge;
  specs.
- [x] `make-swf.mjs`, `Documents/Flash/` in the sample library, scene `flash` (real Ruffle in the real app: the movie
  and the one it loads from its folder); `docs/images/flash-viewer.png`; user docs, configuration, changelog.
- [x] `ng serve`: the dev server sends `Access-Control-Allow-Origin: *` (`angular.json`), or the frame can't fetch the
  `.wasm`.

## Left

- [ ] By hand in the app: full screen, sound after the opening click, keyboard in the frame, Esc passed on.
- [ ] Phones: touch input; Ruffle's virtual keyboard (opened when a text field of the movie takes the focus).
- [ ] Firefox: headless Firefox started from the agent's shell fetched nothing; check by hand.
- [ ] `docs/images/admin-playback-player.png` again (the new Flash movies group).
- [x] `docs/upgrading.md` note (1.83.0).

Later, not in this epic: a thumbnail of the first frame (Ruffle's desktop build can render headless; ffmpeg can't
play ActionScript), `.spl` (FutureSplash), movies' saved games (`SharedObject`: the frame has no storage).

## Production

- No database, stored-file or cache change; the setting defaults on and needs no migration.
- Packages grow by ~29 MB (see [portable-build](../environment/portable-build.md), [docker](../environment/docker.md)):
  check the size limits of the release jobs.
