# Battery probe

What the page does while a phone holds it, to find what drains batteries: requests and how often, server-sent
events, timers and animation frames that fire, main-thread time, layouts. Findings and fixes:
[battery-probe](../../docs/environment/battery-probe.md).

```sh
node agents/tools/battery-probe/probe.mjs                      # every scenario, 120 s each (~22 min: run it in the background)
node agents/tools/battery-probe/probe.mjs --seconds 30 listing listing-hidden
node agents/tools/battery-probe/probe.mjs --list               # the scenarios
```

- Needs a docs-screenshots run first (`agents/tools/docs-screenshots/run.mjs`): it reuses its build and seeded data in
  `<tmp>/sfe-docs` (or `SFE_DOCS_WORK`) and starts its own app on :5099. After a client change rebuild that copy:
  `dotnet build src/Sfe.Api --artifacts-path <tmp>/sfe-docs/build`. A database of an earlier version is updated first
  (code `doc-screenshots-setup`).
- Each scenario opens a fresh 390×844 phone browser (touch, scale 3), signs in as `admin`, opens the page, waits
  7 s, then counts for `--seconds`. Output: a table, the requests and busiest timers of each scenario, and
  `<out>/battery-probe.json` (`--out`, default `<tmp>/sfe-battery-probe`) to compare runs before and after a fix.
- Timers are counted by the line that set them and their delay (`setInterval 1000 ms new i (main-….js:8:38673)`):
  the bundle is minified, so look the column up in `src/Sfe.Api/wwwroot/main-….js` (`sed -n 8p … | cut -c…`).
- `scan*` scenarios answer `api/scan` with a scan that runs forever (CDP `Fetch`), so the page polls as during a long
  scan.

## Limits

- **Hidden is simulated:** `document.hidden` / `visibilityState` are overridden and `visibilitychange` sent, as on a
  tab switch. Headless Edge doesn't throttle those timers as a real background tab does (Chrome: once a minute after
  5 min hidden, and Android freezes background tabs later): the probe shows what the app *asks* for. Network
  connections (the change feed) are kept open by a real phone too.
- Headless draws on the CPU (no GPU): `scroll` vs `scroll-noblur` compares the cost of `backdrop-filter` roughly only;
  confirm on a phone.
- Desktop numbers say what wakes the phone, not how much battery it takes: check on a real phone (below).

## On a real Android phone

### Phone only (no PC)

1. Charge to 100 %, close every Chrome tab, note the time.
2. **Background:** open SFE on a folder with videos, press Home, screen off for 1 h. Then Settings › Battery ›
   Battery usage (Samsung: Battery › usage per app): note Chrome's % and its background time.
3. **Baseline:** the same hour with only `about:blank` open in Chrome. Compare with 2.
4. **Active:** 15 min browsing and scrolling thumbnail folders: note Chrome's % and how warm the phone gets.
5. **Video:** play a video, open a new tab `chrome://media-internals`, tap the player entry: `kVideoDecoderName`
   shows a hardware decoder (`MediaCodecVideoDecoder`) or a software one (`FFmpegVideoDecoder`, `Dav1dVideoDecoder`,
   `VpxVideoDecoder`: costly). Note the codec, the resolution, and whether it was the file or an ffmpeg stream.
6. Repeat 2 after a fix, with the same phone and folder.

### With a PC (Chrome remote debugging)

1. **USB debugging (once):** Settings › About phone › tap **Build number** 7 times (Samsung: About phone › Software
   information › Build number); then Settings › System › **Developer options** › **USB debugging** on. Plug the phone
   in, accept **Allow USB debugging?** (Always allow). Nothing? Pick **File transfer** in the USB notification;
   Windows may need the Google (Pixel) or Samsung USB driver.
2. **Open SFE on the phone:** on the PC `chrome://inspect/#devices` (or `edge://inspect`), tick **Discover USB
   devices**, **Port forwarding…** `<port>` → `localhost:<port>`, tick **Enable port forwarding**; on the phone open
   `http://localhost:<port>`. Or over Wi-Fi: the server in External mode, listening on the network, the firewall open,
   `http://<PC's IP>:<port>` on the phone.
3. **Attach:** click **inspect** under the SFE tab in `chrome://inspect`. An attached DevTools can keep a background
   tab from being frozen: background numbers seen this way are an upper bound.
4. **Idle (5 min):** Network tab, **Preserve log**, a folder of videos untouched (Developer options › **Stay awake**
   keeps the screen on while charging). `api/files/changes` › **EventStream** shows a `ping` every 25 s; note what else
   comes back regularly. Right-click › **Save all as HAR**.
5. **Background (10 min):** the same log, press Home, screen off, come back: what ran meanwhile is in the Waterfall.
6. **CPU at rest:** ⋮ › More tools › **Performance monitor** (CPU, layouts/s, style recalcs/s: all near 0 when idle),
   then **Performance** › record 60 s idle: tasks repeating at a fixed interval, Bottom-Up for their function.
7. **Scrolling:** record 20 s of scrolling a big folder; **Rendering** › **Paint flashing** / **Layer borders**: the
   whole screen flashing on each step points to the blurred sticky bars. Untick `backdrop-filter` on `.top-bar` in
   Elements and record again to compare.
8. **Battery numbers (adb, DevTools closed):** `adb shell dumpsys batterystats --reset`, unplug (or
   `adb shell dumpsys battery unplug`), 30 min of one scenario, then
   `adb shell dumpsys batterystats com.android.chrome > out.txt` (mobile radio active time, wake locks, CPU,
   estimated power use); the same with only `about:blank` as the baseline; `adb shell dumpsys battery reset` at the end.
