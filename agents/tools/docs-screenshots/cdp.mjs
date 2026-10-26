// Minimal Chrome DevTools Protocol driver for headless Edge.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

/** The browser: env EDGE (any Chromium: Edge, Chrome, Chromium), else the usual install folders, else the PATH. */
const EDGE = process.env.EDGE ?? [
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
].find(p => fs.existsSync(p)) ?? ['microsoft-edge', 'msedge', 'google-chrome', 'chromium', 'chromium-browser'].find(onPath);
if (!EDGE) throw new Error('no Edge/Chrome/Chromium found: set EDGE to its path');

function onPath(program) {
    return spawnSync(process.platform === 'win32' ? 'where' : 'which', [program]).status === 0;
}
export const sleep = ms => new Promise(r => setTimeout(r, ms));

export async function launch({ width = 1280, height = 800, mobile = false, scale = 1, colorScheme = 'dark', scrollbars = false } = {}) {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sfe-edge-'));
    const proc = spawn(EDGE, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${dir}`,
        '--no-first-run', '--no-default-browser-check', ...(scrollbars ? [] : ['--hide-scrollbars']), '--autoplay-policy=no-user-gesture-required',
        `--window-size=${width},${height}`, 'about:blank'], { stdio: 'ignore' });
    let port;
    for (let i = 0; i < 100 && !port; i++) {
        await sleep(100);
        try { port = fs.readFileSync(path.join(dir, 'DevToolsActivePort'), 'utf8').split('\n')[0]; } catch { }
    }
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const page = targets.find(t => t.type === 'page');
    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise(r => ws.addEventListener('open', r, { once: true }));
    let id = 0;
    const pending = new Map();
    const listeners = [];
    ws.addEventListener('message', e => {
        const msg = JSON.parse(e.data);
        if (msg.id && pending.has(msg.id)) {
            const { resolve, reject } = pending.get(msg.id);
            pending.delete(msg.id);
            msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result);
        } else if (msg.method) listeners.forEach(l => l(msg));
    });
    const send = (method, params = {}) => new Promise((resolve, reject) => {
        const n = ++id;
        pending.set(n, { resolve, reject });
        ws.send(JSON.stringify({ id: n, method, params }));
    });
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: scale, mobile });
    if (mobile) await send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
    await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: colorScheme }] });
    listeners.push(m => { if (m.method === 'Runtime.exceptionThrown') console.error('page error', m.params.exceptionDetails.text, m.params.exceptionDetails.exception?.description); });

    const api = {
        send,
        /** Calls `listener(params)` for each `method` event (enable its domain first, e.g. `Network.enable`). */
        on(method, listener) {
            listeners.push(m => { if (m.method === method) listener(m.params); });
        },
        async eval(expression) {
            const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
            if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
            return r.result.value;
        },
        async goto(url, wait = 2500) {
            await send('Page.navigate', { url });
            await sleep(wait);
        },
        async waitFor(selector, timeout = 15000) {
            const end = Date.now() + timeout;
            while (Date.now() < end) {
                if (await api.eval(`!!document.querySelector(${JSON.stringify(selector)})`)) return true;
                await sleep(150);
            }
            throw new Error('timeout waiting for ' + selector);
        },
        async click(selector) {
            const ok = await api.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return false; el.click(); return true; })()`);
            if (!ok) throw new Error('no element ' + selector);
        },
        async rect(selector) {
            return api.eval(`(() => { const r = document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height }; })()`);
        },
        async mouse(type, x, y, extra = {}) {
            await send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: type === 'mouseMoved' ? 0 : 1, ...extra });
        },
        async realClick(selector) {
            const r = await api.rect(selector);
            const x = r.x + r.width / 2, y = r.y + r.height / 2;
            await api.mouse('mouseMoved', x, y);
            await api.mouse('mousePressed', x, y);
            await api.mouse('mouseReleased', x, y);
        },
        async hover(selector) {
            const r = await api.rect(selector);
            await api.mouse('mouseMoved', r.x + r.width / 2, r.y + r.height / 2);
        },
        /**
         * A real drag and drop (the browser's own, intercepted): pressed on `from`, moved to (x, y), then dropped
         * there unless `drop` is false (the page then shows the drag over its target until the next call).
         */
        async drag(from, x, y, { drop = true } = {}) {
            await send('Input.setInterceptDrags', { enabled: true });
            const started = new Promise(resolve => listeners.push(m => { if (m.method === 'Input.dragIntercepted') resolve(m.params.data); }));
            const r = await api.rect(from);
            const sx = r.x + r.width / 2, sy = r.y + r.height / 2;
            await api.mouse('mouseMoved', sx, sy);
            await api.mouse('mousePressed', sx, sy);
            for (let i = 1; i <= 5; i++)
                await api.mouse('mouseMoved', sx + (x - sx) * i / 5, sy + (y - sy) * i / 5, { buttons: 1 });
            const data = await Promise.race([started, sleep(3000).then(() => { throw new Error('no drag started on ' + from); })]);
            await send('Input.dispatchDragEvent', { type: 'dragEnter', x, y, data });
            await send('Input.dispatchDragEvent', { type: 'dragOver', x, y, data });
            if (drop) {
                await send('Input.dispatchDragEvent', { type: 'drop', x, y, data });
                await api.mouse('mouseReleased', x, y);
                await send('Input.setInterceptDrags', { enabled: false });
            }
            return data;
        },
        async type(text) { await send('Input.insertText', { text }); },
        async key(key, code = key, keyCode = 0, modifiers = 0) {
            await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode: keyCode, modifiers });
            await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode: keyCode, modifiers });
        },
        async shot(file, clip) {
            const r = await send('Page.captureScreenshot', { format: 'png', ...(clip ? { clip: { ...clip, scale: 1 } } : {}) });
            fs.mkdirSync(path.dirname(file), { recursive: true });
            fs.writeFileSync(file, Buffer.from(r.data, 'base64'));
            console.log('saved', file);
        },
        async close() {
            try { ws.close(); } catch { }
            proc.kill();
            await sleep(500);
            try { fs.rmSync(dir, { recursive: true, force: true }); } catch { }
        },
    };
    return api;
}
