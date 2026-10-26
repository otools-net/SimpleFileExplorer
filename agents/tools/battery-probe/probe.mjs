// What the page does while a phone holds it: requests (and how often), timers and animation frames that fire,
// main-thread time, per scenario (an idle listing, the tab hidden, a scan running, the tasks page, scrolling, a video).
// Usage: node probe.mjs [--seconds <n>] [--out <folder>] [scenario...]   (`--list` lists the scenarios)
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs, or SFE_DOCS_WORK); serves them on
// :5099. Writes <out>/battery-probe.json and prints one table (default out: <tmp>/sfe-battery-probe).
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = process.env.SFE_DOCS_WORK ?? path.join(os.tmpdir(), 'sfe-docs');
const data = path.join(work, 'data');
const url = 'http://localhost:5099/';

const args = process.argv.slice(2);
const option = (name, fallback) => {
    const i = args.indexOf(name);
    return i < 0 ? fallback : args.splice(i, 2)[1];
};
const seconds = Number(option('--seconds', 120));
const out = option('--out', path.join(os.tmpdir(), 'sfe-battery-probe'));

/** A scan that runs (forever): `api/scan` answered by the probe, so the page polls as during a long scan. */
const RUNNING_SCAN = {
    state: 'running', path: '', startedAt: new Date().toISOString(), finishedAt: null, directoriesScanned: 12, directoriesQueued: 40,
    filesIndexed: 300, removed: 0, errors: 0, currentDirectory: 'Videos', message: null, relinked: 0, missing: null,
};
const LISTING = '?path=Videos/Holidays';
const SCENARIOS = {
    'listing': { page: LISTING, about: 'a folder of videos, untouched' },
    'listing-hidden': { page: LISTING, hidden: true, about: 'the same, the tab in the background' },
    'scan': { page: LISTING, scan: true, about: 'a folder while a scan runs' },
    'scan-hidden': { page: LISTING, scan: true, hidden: true, about: 'the same, the tab in the background' },
    'tasks': { page: 'admin?section=tasks', about: 'Administration › Tasks, untouched' },
    'tasks-hidden': { page: 'admin?section=tasks', hidden: true, about: 'the same, the tab in the background' },
    'text-hidden': { page: 'view?path=Documents/Trip diary.txt', hidden: true, about: 'a text file open, the tab in the background' },
    'scroll': { page: '?q=e:png e:jpg e:webm e:mp3', scroll: true, about: 'scrolling a search of the media up and down' },
    'scroll-noblur': { page: '?q=e:png e:jpg e:webm e:mp3', scroll: true, noBlur: true, about: 'the same without backdrop-filter' },
    'video': { page: 'player?path=Videos/Holidays/Campfire.webm', video: true, about: 'a video playing in the player page' },
};

if (args.includes('--list')) {
    for (const [name, s] of Object.entries(SCENARIOS))
        console.log(`${name.padEnd(16)} ${s.about}`);
    process.exit(0);
}
const chosen = args.length ? args : Object.keys(SCENARIOS);
for (const name of chosen)
    if (!SCENARIOS[name]) throw new Error(`no scenario ${name} (--list)`);

// Counts each timer's callbacks, by the line that set it and its delay, and whether the page was hidden then.
const INSTRUMENT = `(() => {
    const counts = {};
    window.__probe = { counts, reset: () => { for (const k in counts) delete counts[k]; } };
    const site = () => {
        const line = (new Error().stack || '').split('\\n').slice(3).find(l => !l.includes('__probe')) || '';
        return line.trim().replace(/^at /, '').replace(/\\(?https?:\\/\\/[^/]+\\//, '(').replace(/\\?[^:)]*/, '');
    };
    const count = (kind, delay, where) => {
        const key = kind + ' ' + (delay ?? '') + ' ' + where;
        const c = counts[key] ??= { kind, delay, where, fired: 0, hidden: 0 };
        c.fired++;
        if (document.hidden) c.hidden++;
    };
    for (const kind of ['setTimeout', 'setInterval']) {
        const original = window[kind];
        window[kind] = function (fn, delay, ...rest) {
            if (typeof fn !== 'function') return original.call(this, fn, delay, ...rest);
            const where = site();
            return original.call(this, function () { count(kind, Math.round(delay || 0), where); return fn.apply(this, arguments); }, delay, ...rest);
        };
    }
    const raf = window.requestAnimationFrame;
    window.requestAnimationFrame = function (fn) {
        const where = site();
        return raf.call(this, t => { count('rAF', null, where); fn(t); });
    };
})();`;

/** The tab in the background, as the page sees it: hidden, `visibilitychange` sent, `pagehide` not (a tab switch). */
const HIDE = `(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
})()`;

const installation = path.join(data, 'installation-battery-probe.json');
fs.writeFileSync(installation, JSON.stringify({ Urls: url.replace(/\/$/, ''), Sfe: { Root: path.join(work, 'library') } }));
const app = spawn('dotnet', [path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll'), '--contentRoot', path.join(repo, 'src/Sfe.Api'), '--no-browser', '--no-logo'], {
    cwd: work, stdio: 'ignore',
    env: {
        ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
        Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
        Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
        Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
    },
});

/** Waits for the app, and runs its update when the docs database is of an earlier version. */
async function ready() {
    let migration = null;
    for (let i = 0; i < 120 && !migration; i++) {
        try { migration = await (await fetch(url + 'api/migration')).json(); } catch { await sleep(500); }
    }
    if (!migration)
        throw new Error('the app did not start');
    if (migration.status === 'none')
        return;
    console.log(`updating the docs database from ${migration.databaseVersion} to ${migration.appVersion}`);
    await fetch(url + 'api/migration', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ code: 'doc-screenshots-setup' }) });
    for (let i = 0; i < 240; i++) {
        migration = await (await fetch(url + 'api/migration')).json();
        if (migration.status === 'none')
            return;
        if (migration.status === 'failed')
            throw new Error(`the update failed: ${migration.error}`);
        await sleep(500);
    }
    throw new Error('the update did not finish');
}

const metric = (metrics, name) => metrics.find(m => m.name === name)?.value ?? 0;

/** One scenario in a fresh phone-sized browser: settles, then counts for `seconds`. */
async function run(name) {
    const s = SCENARIOS[name];
    const b = await launch({ width: 390, height: 844, mobile: true, scale: 3 });
    try {
        await b.send('Network.enable');
        await b.send('Performance.enable', { timeDomain: 'timeTicks' });
        await b.send('Page.addScriptToEvaluateOnNewDocument', { source: INSTRUMENT });
        if (s.noBlur)
            await b.send('Page.addScriptToEvaluateOnNewDocument', {
                source: `document.addEventListener('DOMContentLoaded', () => document.head.append(Object.assign(document.createElement('style'), { textContent: '* { backdrop-filter: none !important; -webkit-backdrop-filter: none !important; }' })));`,
            });
        await b.goto(url + 'login', 1500);
        await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
        // The scan panel stays dismissed (its result from the seed), except when the scenario runs one.
        if (s.scan) {
            await b.send('Fetch.enable', { patterns: [{ urlPattern: '*/api/scan' }] });
            b.on('Fetch.requestPaused', ({ requestId }) => b.send('Fetch.fulfillRequest', {
                requestId, responseCode: 200, responseHeaders: [{ name: 'Content-Type', value: 'application/json' }],
                body: Buffer.from(JSON.stringify(RUNNING_SCAN)).toString('base64'),
            }).catch(() => { }));
        }

        const requests = [];
        const sse = [];
        let counting = false;
        b.on('Network.requestWillBeSent', ({ requestId, request, timestamp }) => {
            if (counting) requests.push({ requestId, url: request.url.replace(url, '').replace(/\?.*/, ''), method: request.method, t: timestamp, bytes: 0 });
        });
        b.on('Network.dataReceived', ({ requestId, encodedDataLength }) => {
            const r = requests.find(r => r.requestId === requestId);
            if (r) r.bytes += encodedDataLength;
        });
        b.on('Network.eventSourceMessageReceived', ({ eventName, timestamp }) => {
            if (counting) sse.push({ eventName, t: timestamp });
        });

        await b.goto(url + s.page, 5000);
        if (s.hidden)
            await b.eval(HIDE);
        if (s.video)
            await b.eval(`document.querySelector('video')?.play()`).catch(() => { });
        await sleep(2000);

        await b.eval('window.__probe.reset()');
        const before = (await b.send('Performance.getMetrics')).metrics;
        counting = true;
        const start = Date.now();
        if (s.scroll) {
            let down = true;
            while (Date.now() - start < seconds * 1000) {
                await b.send('Input.dispatchMouseEvent', { type: 'mouseWheel', x: 195, y: 500, deltaX: 0, deltaY: down ? 400 : -400 });
                await sleep(100);
                if (await b.eval(down ? 'innerHeight + scrollY >= document.documentElement.scrollHeight - 2' : 'scrollY <= 0'))
                    down = !down;
            }
        } else {
            await sleep(seconds * 1000);
        }
        counting = false;
        const after = (await b.send('Performance.getMetrics')).metrics;
        const timers = Object.values(await b.eval('window.__probe.counts')).sort((a, b) => b.fired - a.fired);
        const delta = key => metric(after, key) - metric(before, key);

        const byUrl = {};
        for (const r of requests) {
            const g = byUrl[`${r.method} ${r.url}`] ??= { count: 0, bytes: 0, times: [] };
            g.count++;
            g.bytes += r.bytes;
            g.times.push(r.t);
        }
        for (const g of Object.values(byUrl)) {
            const gaps = g.times.slice(1).map((t, i) => t - g.times[i]);
            g.every = gaps.length ? +(gaps.reduce((a, b) => a + b, 0) / gaps.length).toFixed(1) : null;
            delete g.times;
        }
        const minutes = seconds / 60;
        return {
            name, about: s.about, seconds,
            requestsPerMinute: +(requests.length / minutes).toFixed(1),
            kbPerMinute: +(requests.reduce((a, r) => a + r.bytes, 0) / 1024 / minutes).toFixed(1),
            sseEventsPerMinute: +(sse.length / minutes).toFixed(1),
            timerFiresPerMinute: +(timers.filter(t => t.kind !== 'rAF').reduce((a, t) => a + t.fired, 0) / minutes).toFixed(1),
            framesPerMinute: +(timers.filter(t => t.kind === 'rAF').reduce((a, t) => a + t.fired, 0) / minutes).toFixed(1),
            mainThreadMsPerMinute: Math.round(delta('TaskDuration') * 1000 / minutes),
            scriptMsPerMinute: Math.round(delta('ScriptDuration') * 1000 / minutes),
            layoutsPerMinute: +(delta('LayoutCount') / minutes).toFixed(1),
            styleRecalcsPerMinute: +(delta('RecalcStyleCount') / minutes).toFixed(1),
            requests: Object.fromEntries(Object.entries(byUrl).sort((a, b) => b[1].count - a[1].count)),
            sse: Object.entries(Object.groupBy(sse, e => e.eventName)).map(([event, list]) => ({ event, count: list.length })),
            timers: timers.slice(0, 15),
        };
    } finally {
        await b.close();
    }
}

try {
    await ready();
    const results = [];
    for (const name of chosen) {
        console.log(`${name}: ${SCENARIOS[name].about} (${seconds} s)`);
        results.push(await run(name));
    }
    fs.mkdirSync(out, { recursive: true });
    const file = path.join(out, 'battery-probe.json');
    fs.writeFileSync(file, JSON.stringify(results, null, 2));

    const columns = [
        ['scenario', r => r.name], ['req/min', r => r.requestsPerMinute], ['KB/min', r => r.kbPerMinute],
        ['sse/min', r => r.sseEventsPerMinute], ['timers/min', r => r.timerFiresPerMinute], ['frames/min', r => r.framesPerMinute],
        ['main ms/min', r => r.mainThreadMsPerMinute], ['script ms/min', r => r.scriptMsPerMinute],
        ['layouts/min', r => r.layoutsPerMinute], ['styles/min', r => r.styleRecalcsPerMinute],
    ];
    const rows = [columns.map(c => c[0]), ...results.map(r => columns.map(c => String(c[1](r))))];
    const widths = columns.map((_, i) => Math.max(...rows.map(row => row[i].length)));
    console.log();
    for (const row of rows)
        console.log(row.map((cell, i) => i ? cell.padStart(widths[i]) : cell.padEnd(widths[i])).join('  '));
    console.log();
    for (const r of results) {
        console.log(`${r.name}:`);
        for (const [what, g] of Object.entries(r.requests))
            console.log(`  ${String(g.count).padStart(4)} × ${what}${g.every ? ` (every ${g.every} s)` : ''}`);
        for (const t of r.timers.filter(t => t.fired >= 2).slice(0, 6))
            console.log(`  ${String(t.fired).padStart(4)} × ${t.kind}${t.delay != null ? ` ${t.delay} ms` : ''}${t.hidden ? ` (${t.hidden} hidden)` : ''} ${t.where}`);
    }
    console.log(`\nsaved ${file}`);
} finally {
    app.kill();
}
