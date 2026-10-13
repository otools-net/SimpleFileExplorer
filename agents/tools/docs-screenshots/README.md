# Documentation screenshots

Tools that regenerate `docs/images/` from a running SFE on a generated sample library, and check the docs' links.
Node.js 22+, the .NET SDK and Microsoft Edge are needed; no npm packages.

ffmpeg is needed for real screenshots (video thumbnails, durations, trickplay, similar videos). `run.mjs` takes it
from `--ffmpeg <path>`, else the `FFMPEG` environment variable, else `ffmpeg` on the `PATH`; without one it warns and
the video parts come out empty. Where it lives differs per machine: see AGENTS.md › Dev machine.

```sh
# everything: build, sample library, app on :5093, seed, all screenshots, link check
node agents/tools/docs-screenshots/run.mjs --ffmpeg <path to ffmpeg>

# again after a UI change, reusing the library and database of the last run
node agents/tools/docs-screenshots/run.mjs --ffmpeg <path to ffmpeg> --skip-build --shots-only viewer search

# links, images and #anchors of README.md, AGENTS.md and docs/ (also lists unused images)
node agents/tools/docs-screenshots/check-links.mjs
```

| File | |
|---|---|
| `run.mjs` | The whole run (see its header for the options). Work folder: `<tmp>/sfe-docs` by default (build, library, database, caches, `app.log`). Its own scene `update-page` shoots the update page on a copy of the database marked as 1.35.0, served by a second app on the next port (`node:sqlite`) |
| `cdp.mjs` | Minimal Chrome DevTools Protocol driver for headless Edge (env `EDGE` = any Chromium browser, else Edge or Chrome in their usual folders or on the `PATH`): `launch({ width, height, mobile, colorScheme })`, then `goto`, `eval`, `waitFor`, `click`, `realClick`, `hover`, `drag(from, x, y, { drop })` (a real drag and drop), `type`, `key`, `mouse`, `rect`, `shot(file, clip?)` |
| `make-media.mjs` | The sample library: landscape photos drawn on a canvas (various sizes, for HR/LR), WebM clips, audio, duplicates in `Inbox/`, documents. With ffmpeg: 40 s clips, two re-encoded copies for similar videos, an MP3 |
| `seed.mjs` | On an empty database: setup page shot, the administrator (`admin` / `admin-password-1`), trickplay on every 2 s, scan, tags, accounts (`sam`, guest `kids`), a group, schedules; then deletes a tagged file, scans again (a moved file, task history), waits for trickplay and compares similar videos |
| `shots.mjs` | The screenshots, as named scenes (`node shots.mjs viewer tags`); each scene opens a fresh browser |
| `check-links.mjs` | Link checker (GitHub heading slugs) |

## Adding a screenshot

1. Add a scene to `shots.mjs` (or a step to an existing one): `session()` gives a signed-in browser, `b.open(url)`
   hides the scan panel, `b.clickTitle('…')` clicks by the button's title (every button has one), `mask(b)` replaces
   the scratch library's path with `D:\Media` before a shot that shows a server path.
2. Per-browser state (search history, theme) lives in `localStorage`: set it in the scene, before `open`.
3. Run only that scene with `--shots-only <scene>`, look at the picture, reference it from the docs page as
   `![What it shows](../images/<name>.png)`, then run `check-links.mjs`.

Shots are 1280×800 (admin pages 1280×900), dark theme; phones 390×844. Keep the sample data neutral (no real paths or
names).
