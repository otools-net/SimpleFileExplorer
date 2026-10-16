# Docs screenshots (docs/images/)

Tool: `agents/tools/docs-screenshots/` (how to add a scene: its `README.md`).

- `node agents/tools/docs-screenshots/run.mjs --ffmpeg <path to ffmpeg>` (else env `FFMPEG`, else the `PATH`; this
  machine's path is in [dev-machine.md](dev-machine.md)) builds into `<tmp>/sfe-docs`, makes the sample library, starts
  the app on :5093, seeds it, takes every shot and checks the links (~6 min); `--shots-only <scene…>` retakes some on
  the last run's data.
- Without ffmpeg, video tiles show an icon and trickplay/similar videos stay empty: always pass it. Edge has no H.264,
  so sample videos are WebM (VP8).
- `--shots-only` after a version bump meets the update gate (every scene fails on a missing element) and `run.mjs` then
  hangs with the app still up: run the update (`POST api/migration` with `{"code":"doc-screenshots-setup"}`), then
  `node shots.mjs <scene>`.
- The last scene (`streams`) can leave its headless Edge alive, so `shots.mjs` never exits (log stops after
  `admin-system-streams.png`): `taskkill /PID <that msedge> /T /F` and the run goes on.
- After a client change, the run's first build fails once ("build failed": `No file exists for the asset …
  wwwroot\chunk-….js`, the static web assets list still names the previous build's chunks): run it again. A single-scene run without `--shots-only` still reseeds everything (and re-saves
  `setup-page.png`).
- A full run re-saves every image with tiny pixel changes: `git checkout` the ones whose screen didn't change.
- `check-links.mjs` checks the links of `README.md`, `AGENTS.md`, `agents/docs/` and `docs/`.
