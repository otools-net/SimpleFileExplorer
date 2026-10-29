# Real-phone checks: plan and progress

What the emulated end-to-end suite ([testing-e2e](testing-e2e.md), `tests/e2e/`) can't see, checked on real phones
by a person: Chrome's address bar, Safari's history, real timing. Split from the mobile e2e epic on 2026-10-29, to be
done in a session of its own with the user holding the phones.

Status legend: `[ ]` to do, `[~]` started, `[x]` done, `[-]` dropped (why).

## How to use this file

- The agent prepares the app and the steps; the user runs them on the phones and reports what they saw; the agent
  writes the findings here and fixes what they show (each fix with its e2e test where emulation can reproduce it,
  else a unit spec and a line in the by-hand checklist of [testing-e2e](testing-e2e.md)).
- When everything is ticked or dropped: what stays true goes into [testing-e2e](testing-e2e.md) and the feature
  notes, then this file and its links are removed (`AGENTS.md`, "Keeping these notes current").

## Setup

- [ ] A command that starts the same app as the e2e suite (sample library, 600 photos, `admin` / `sam`, scanned)
  reachable from phones on the local network: `tests/e2e/scripts/server.mjs` binds `localhost` only; add a host
  option (e.g. `SFE_E2E_HOST=0.0.0.0`) and the setup / scan of `setup/global-setup.ts` as a script, documented in
  `tests/e2e/README.md`. Prints the address to type on the phone (the PC's LAN IP); Windows firewall rule noted.
- [ ] Plain `http` on the LAN: list what the phones refuse or change without `https` (no secure context: clipboard,
  service workers) so a finding isn't blamed on the app.

## Checks

- [ ] **The first CI run** of `.github/workflows/e2e.yml` on GitHub (the quick set, after `edge` was pushed on
  2026-10-30): green, or what Linux changes (Playwright's Chromium instead of Edge, ffmpeg from apt on full runs). Its
  first version was refused (`runner.temp` in a job's `env:`), fixed since. Read it in the repository's Actions tab.

- [ ] **The report, Android Chrome:** scroll the listing down so the address bar hides; long press an item; tap its
  ⋮. Does the bar at the bottom move (how far, when)? Does the address bar come back? Does the menu open on the
  first tap, at once? Same with a dialog (Rename). A screen recording kept outside the repo.
- [ ] **The report, iPhone Safari:** the same (select with the tile's box, no long press menu on iOS).
- [ ] **Timing, Android:** Chrome remote debugging (`chrome://inspect` on the PC, USB debugging on the phone),
  Performance panel around the tap on ⋮: input → sheet painted, long tasks, layout shifts. Compare with the
  emulated numbers (`android-slow`: ~70 ms median at 4× CPU).
- [ ] **iPhone Back after leaving SFE:** open a folder, scroll down, open another site in the same tab, Back. Is the
  listing where it was? WebKit on Windows comes back with `history.state` null (`navigation.spec.ts`, `fixme` on
  WebKit). If the iPhone loses it too: keep listing positions in `sessionStorage` as well
  ([position-restore](../features/position-restore.md)) and drop the `fixme`.
- [ ] **The by-hand checklist** of [testing-e2e](testing-e2e.md) ("By hand on real phones"): keyboard, viewer
  swipes and pinch, video full screen and rotation, PDF / EPUB by finger, safe areas in portrait and landscape.

## Findings

(none yet)
