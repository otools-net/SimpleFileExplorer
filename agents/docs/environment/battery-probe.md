# Battery probe

Tool: `agents/tools/battery-probe/probe.mjs` ([README](../../tools/battery-probe/README.md)).

- Counts what an open page does on a phone-sized headless Edge: requests per URL and their interval, SSE events,
  timer and animation-frame fires by the line that set them, main-thread time, layouts. Scenarios: listing, scan,
  tasks page, text file, scrolling, video, each also with the tab hidden.
- Reuses the docs-screenshots build and data (`<tmp>/sfe-docs`), its app on :5099; updates an older database itself.
- Hidden is simulated (`document.hidden` overridden): it shows what the app asks for, not the browser's throttling.
- Pipe its output through `tail` and nothing shows until the end: run it in the background and read the JSON.
- Found with it (1.83.0, fixed after): the change feed open in hidden tabs (a `ping` every 25 s), scan and
  administration polling while hidden, a 1 s scan panel clock always on, the scan bar animating `left` (60 layouts a
  second). Rule that came of it: [frontend](../development/frontend.md) (Battery).
- Measured after the fixes (90 s): idle listing 4.7 requests and 9 timer fires a minute; hidden tab (listing, scan or
  Tasks) no requests, the feed closed after 30 s; a running scan 60 layouts and ~0.3 s of main thread a minute.
- Not a cause in headless: `backdrop-filter`, the text viewer's disk check. Open: originals under 512 KB
  (`Thumbnails:LargeImageKilobytes`) are shown in the grid, decoded whole on phones.
