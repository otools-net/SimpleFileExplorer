# Visual check (headless Edge)

Driver: `agents/tools/docs-screenshots/cdp.mjs` (`launch`, `eval`, `realClick`, `shot`) does all of this.

- Screenshot: `msedge --headless=new --screenshot=out.png --virtual-time-budget=6000 URL` (fresh `--user-data-dir` each
  run; no H.264, use WebM).
- To click: Edge `--remote-debugging-port=0` (fixed ports like 9333 are reserved here: "bind() … forbidden"; read the
  port from `<user-data-dir>/DevToolsActivePort`), then a Node script with the built-in `WebSocket`:
  `Runtime.evaluate` (setup with `fetch('api/setup', …)`) then `Page.captureScreenshot`.
- Covers and previews load lazily: give a screenshot a budget of ~20 s before calling an image missing.
- Headless shots hide scrollbars: `launch({ scrollbars: true })`.
