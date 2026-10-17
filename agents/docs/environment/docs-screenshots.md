# Docs screenshots (docs/images/)

Tool: `agents/tools/docs-screenshots/` (how to add a scene: its `README.md`).

- `node agents/tools/docs-screenshots/run.mjs --ffmpeg <path to ffmpeg>` (else env `FFMPEG`, else the `PATH`; this
  machine's path is in [dev-machine.md](dev-machine.md)) builds into `<tmp>/sfe-docs`, makes the sample library, starts
  the app on :5093, seeds it, takes every shot and checks the links (~6 min); `--shots-only <scene…>` retakes some on
  the last run's data.
- Without ffmpeg, video tiles show an icon and trickplay/similar videos stay empty: always pass it. Edge has no H.264,
  so sample videos are WebM (VP8).
- `--shots-only` after a version bump would meet the update gate (every scene waits): `run.mjs` refuses it at once
  ("belongs to SFE …: run without --shots-only"). Run without it (it reseeds), or run the update
  (`POST api/migration` with `{"code":"doc-screenshots-setup"}`) then `node shots.mjs <scene>`.
- **Watchdog:** `run.mjs` prints each screenshot as it is written (`  + name.png`) and stops a step (seed, shots,
  links) that writes no screenshot and prints nothing for `--stall` minutes (default 5, `0` = never): the run fails
  naming the last screenshot, so a stuck scene is the one after it. Run it in the background and read its output
  instead of waiting on a silent process. `node()` in `run.mjs` is async: a script call without `await` runs alongside
  the next step (a missing one let seeding start before the sample videos existed).
- The last scene (`streams`) can leave its headless Edge alive, so `shots.mjs` never exits (log stops after
  `admin-playback-streams.png`): the watchdog stops it after `--stall` minutes (the images are already written), or
  `taskkill /PID <that msedge> /T /F` and the run goes on.
- After a client change, the run's first build fails once ("build failed": `No file exists for the asset …
  wwwroot\chunk-….js`, the static web assets list still names the previous build's chunks): `run.mjs` builds once
  more by itself. A single-scene run without `--shots-only` still reseeds everything (and re-saves
  `setup-page.png`).
- A full run re-saves every image with tiny pixel changes: `git checkout` the ones whose screen didn't change.
- `check-links.mjs` checks the links of `README.md`, `AGENTS.md`, `agents/docs/` and `docs/`.
