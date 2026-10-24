// Rows of buttons on narrow screens (overflow menus epic, agents/docs/development/overflow-menus-plan.md): at the
// target sizes (360×800, 390×844, 414×896 portrait, 800×360 landscape; 1280×800 for comparison) each scene opens a
// screen, and every `[sfeOverflowRow]` (or other row listed in the scene) must stay on one line, inside itself and the
// screen; prints `ok`/`FAIL` per check with what left the row (exit code 1 on a failure), saves
// `<prefix>-<scene>-<size>.png` and, where something left, `-menu.png` with the "⋯" menu open.
// Usage: node overflow.mjs <out prefix> [scene...]   (scenes: see `scenes` below; all by default)
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs); serves them on :5095.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = path.join(os.tmpdir(), 'sfe-docs');
const data = path.join(work, 'data');
const out = process.argv[2];
if (!out) {
    console.error('Usage: node overflow.mjs <out prefix> [scene...]');
    process.exit(2);
}
const url = 'http://localhost:5095/';
const sizes = [[360, 800], [390, 844], [414, 896], [800, 360], [1280, 800]];
const enc = value => encodeURIComponent(value);

/** The first list rows: the name's height, the line under it (null when hidden), whether the size column shows. */
const listProbe = `JSON.stringify([...document.querySelectorAll('.files.list .row')].slice(0, 3).map(row => ({
    name: Math.round(row.querySelector('.item-name').getBoundingClientRect().height),
    meta: getComputedStyle(row.querySelector('.row-meta')).display === 'none' ? null : row.querySelector('.row-meta').textContent,
    sizeColumn: getComputedStyle(row.querySelector('.size')).display !== 'none',
})))`;

/**
 * Each scene opens its screen in `b` (signed in) and names the rows to check (CSS selectors); `probe`, if any, is an
 * expression evaluated in the page whose value is printed at each size (what a layout shows).
 */
const scenes = {
    'viewer-video': {
        rows: ['.player-row'],
        async open(b) {
            await b.goto(url + '?path=' + enc('Videos/Holidays'), 3000);
            await b.eval(`document.querySelector('a.item-link[href*="Mountain"]').click(); 1`);
            await sleep(2500);
            await pauseVideo(b);
        },
    },
    'viewer-fullscreen': {
        rows: ['.player-row'],
        async open(b) {
            await scenes['viewer-video'].open(b);
            // A real click: fullscreen needs a user gesture. The "⋯" menu must show above the fullscreen player.
            await b.realClick('.player-row button[title="Fullscreen (F)"]');
            await sleep(1200);
            await pauseVideo(b);
            console.log(`     fullscreen: ${await b.eval('Boolean(document.fullscreenElement)')}`);
        },
    },
    selection: {
        rows: ['.selection-bar'],
        async open(b) {
            // A folder and files selected (every button of the bar), list view.
            await b.eval(`localStorage.setItem('sfe.view', 'list'); 1`);
            await b.goto(url + '?path=' + enc('Photos'), 3000);
            await b.eval(`document.querySelector('.select-box input').click(); 1`);
            await sleep(400);
            await b.eval(`[...document.querySelectorAll('.selection-bar button')].find(button => button.textContent.trim() === 'Select all').click(); 1`);
            await sleep(800);
        },
    },
    // List rows (step 10): a narrow list shows the name on its own line, type · size · resolution under it.
    'list-view': {
        rows: ['.files.list .row'],
        probe: listProbe,
        async open(b) {
            await b.eval(`localStorage.setItem('sfe.view', 'list'); 1`);
            await b.goto(url + '?path=' + enc('Photos'), 3000);
        },
    },
    'list-search': {
        rows: ['.files.list .row'],
        probe: listProbe,
        async open(b) {
            await b.eval(`localStorage.setItem('sfe.view', 'list'); 1`);
            await b.goto(url + '?q=' + enc('e'), 3500);
        },
    },
    // The document viewers' toolbars (step 5).
    'text-viewer': {
        rows: ['.text-toolbar'],
        open: b => openDocument(b, 'Documents', 'Photo backup.log'),
        // Long lines (step 12): wrapped or not, and whether the text scrolls sideways to their end.
        probe: `JSON.stringify((() => {
            const scroller = document.querySelector('.viewer .cm-scroller');
            const style = getComputedStyle(scroller);
            const before = scroller.scrollLeft;
            scroller.scrollLeft = 10000;
            const reached = scroller.scrollLeft;
            scroller.dispatchEvent(new Event('scroll'));
            const fadeAtEnd = document.querySelector('.viewer .cm-editor').classList.contains('cm-line-continues');
            scroller.scrollLeft = before;
            scroller.dispatchEvent(new Event('scroll'));
            return {
                wrap: Boolean(document.querySelector('.viewer .cm-lineWrapping')),
                fade: document.querySelector('.viewer .cm-editor').classList.contains('cm-line-continues'),
                overflowX: style.overflowX, touchAction: style.touchAction,
                scrollWidth: scroller.scrollWidth, clientWidth: scroller.clientWidth, scrolledTo: reached, fadeAtEnd,
                widest: Math.max(...[...document.querySelectorAll('.viewer .cm-line')].map(line => line.scrollWidth)),
            };
        })())`,
    },
    // A prose file (its profile has a width) with wrapping turned off.
    'text-nowrap': {
        rows: ['.text-toolbar'],
        async open(b) {
            await openDocument(b, 'Documents', 'Trip diary.txt');
            await b.eval(`document.querySelector('.viewer .text-toolbar button[aria-label="Wrap long lines"]').click(); 1`);
            await sleep(800);
        },
        get probe() { return scenes['text-viewer'].probe; },
    },
    // A finger dragged right to left over a long line: the line should scroll, the viewer stay on the file.
    'text-swipe': {
        rows: ['.text-toolbar'],
        async open(b) {
            await scenes['text-nowrap'].open(b);
            const { width } = await b.rect('.viewer .cm-scroller');
            const y = 210;
            const touch = (type, x) => b.send('Input.dispatchTouchEvent', { type, touchPoints: type === 'touchEnd' ? [] : [{ x, y }] });
            await touch('touchStart', width - 40);
            for (let x = width - 60; x >= 40; x -= 20) {
                await touch('touchMove', x);
                await sleep(16);
            }
            await touch('touchEnd', 40);
            await sleep(1200);
        },
        probe: `JSON.stringify({
            file: document.querySelector('.viewer-title strong')?.textContent,
            scrollLeft: Math.round(document.querySelector('.viewer .cm-scroller')?.scrollLeft ?? -1),
        })`,
    },
    'text-page': {
        rows: ['.text-toolbar'],
        open: b => b.goto(url + 'view?path=' + enc('Documents/Photo backup.log'), 3500),
        probe: `JSON.stringify((() => {
            const scroller = document.querySelector('.cm-scroller');
            scroller.scrollLeft = 10000;
            return { scrollWidth: scroller.scrollWidth, clientWidth: scroller.clientWidth, scrolledTo: scroller.scrollLeft, page: document.documentElement.scrollWidth };
        })())`,
    },
    'text-editor': {
        rows: ['.text-toolbar'],
        async open(b) {
            await openDocument(b, 'Documents', 'Trip diary.txt');
            await b.eval(`document.querySelector('.viewer [data-edit]').click(); 1`);
            await sleep(1500);
        },
    },
    'json-viewer': { rows: ['.text-toolbar'], open: b => openDocument(b, 'Documents', 'Trip plan.json') },
    'csv-viewer': { rows: ['.text-toolbar'], open: b => openDocument(b, 'Documents', 'Huts.csv') },
    'subtitles-viewer': { rows: ['.text-toolbar'], open: b => openDocument(b, 'Videos/Holidays', 'Campfire.en.srt') },
    'pdf-viewer': { rows: ['.text-toolbar'], open: b => openDocument(b, 'Documents', 'Hut handbook.pdf') },
    'markdown-viewer': { rows: ['.text-toolbar'], open: b => openDocument(b, 'Documents', 'Hiking guide.md') },
    'markdown-page': { rows: ['.text-toolbar'], open: b => b.goto(url + 'view?path=' + enc('Documents/Hiking guide.md'), 3500) },
    'player-page': {
        rows: ['.player-row'],
        async open(b) {
            await b.goto(url + 'player?path=' + enc('Videos/Holidays/Mountain drive.webm'), 3000);
            await pauseVideo(b);
        },
    },
};

/** A file of `folder` opened in the viewer, from the listing. */
async function openDocument(b, folder, title) {
    await b.goto(url + '?path=' + enc(folder), 3000);
    await b.eval(`document.querySelector('a.item-link[title^=${JSON.stringify(title)}]').click(); 1`);
    await sleep(3000);
}

/** Paused, so the controls stay shown. */
async function pauseVideo(b) {
    await b.eval(`(async () => { const v = document.querySelector('video'); v?.pause(); await new Promise(r => setTimeout(r, 600)); return 1; })()`);
}

/** What a row looks like: one line, its children inside it, the page not wider than the screen, what left it. */
function measure(selector) {
    return `JSON.stringify((() => {
        const row = document.querySelector(${JSON.stringify(selector)});
        if (!row)
            return { missing: true };
        const box = row.getBoundingClientRect();
        const shown = [...row.children].filter(child => {
            const style = getComputedStyle(child);
            return style.display !== 'none' && style.visibility !== 'hidden';
        }).map(child => child.getBoundingClientRect()).filter(rect => rect.width > 0);
        const name = element => element.getAttribute('aria-label') || element.title || element.className;
        return {
            width: Math.round(box.width),
            lines: new Set(shown.map(rect => Math.round(rect.top + rect.height / 2) >> 3)).size,
            inside: shown.every(rect => rect.left >= box.left - 1 && rect.right <= box.right + 1),
            onScreen: box.left >= -1 && box.right <= innerWidth + 1,
            pageFits: document.documentElement.scrollWidth <= innerWidth,
            left: [...row.querySelectorAll(':scope > [data-overflowed]')].map(name),
            more: Boolean(row.querySelector(':scope > .overflow-more.shown')),
        };
    })())`;
}

const installation = path.join(data, 'installation-overflow.json');
fs.writeFileSync(installation, JSON.stringify({ Urls: url.replace(/\/$/, ''), Sfe: { Root: path.join(work, 'library') } }));
const app = spawn('dotnet', [path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll'), '--contentRoot', path.join(repo, 'src/Sfe.Api'), '--no-browser'], {
    cwd: work, stdio: 'ignore',
    env: {
        ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
        Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
        Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
        Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
    },
});
let failed = false;
try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    const chosen = process.argv.slice(3).length ? process.argv.slice(3) : Object.keys(scenes);
    for (const name of chosen) {
        const scene = scenes[name];
        if (!scene)
            throw new Error(`No scene ${name}: ${Object.keys(scenes).join(', ')}`);
        for (const [width, height] of sizes) {
            const b = await launch({ width, height, mobile: width < 1000, scrollbars: true });
            await b.goto(url + 'login', 1500);
            await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
            await scene.open(b);
            const size = `${width}x${height}`;
            let withMore = null;
            for (const selector of scene.rows) {
                const result = JSON.parse(await b.eval(measure(selector)));
                const ok = !result.missing && result.lines === 1 && result.inside && result.onScreen && result.pageFits;
                failed ||= !ok;
                withMore ??= result.more ? selector : null;
                console.log(`${ok ? 'ok  ' : 'FAIL'} ${name} ${size} ${selector} ${JSON.stringify(result)}`);
            }
            if (scene.probe)
                console.log(`     probe: ${await b.eval(scene.probe)}`);
            await b.shot(`${out}-${name}-${size}.png`);
            if (withMore) {
                // The "⋯" of that row (a row hidden elsewhere on the page may have one too).
                await b.eval(`document.querySelector(${JSON.stringify(withMore + ' > .overflow-more.shown')}).click(); 1`);
                await sleep(600);
                const menu = await b.eval(`JSON.stringify([...document.querySelectorAll('.item-menu .item-menu-entry')].map(entry => entry.dataset.entry))`);
                console.log(`     menu: ${menu}`);
                await b.shot(`${out}-${name}-${size}-menu.png`);
            }
            await b.close();
        }
    }
} finally {
    app.kill();
}
process.exit(failed ? 1 : 0);
