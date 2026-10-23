// Rows of buttons on narrow screens (overflow menus epic, agents/docs/development/overflow-menus-plan.md): at the
// target sizes (360×800, 390×844, 414×896 portrait, 800×360 landscape; 1280×800 for comparison) each scene opens a
// screen, and every `[sfeOverflowRow]` (or other row listed in the scene) must stay on one line, inside itself and the
// screen; prints `ok`/`FAIL` per check with what left the row (exit code 1 on a failure), saves
// `<prefix>-<scene>-<size>.png` and, where something left, `-menu.png` with the "⋯" menu open.
// Usage: node overflow.mjs <out prefix> [scene...]   (scenes: viewer-video, viewer-fullscreen, player-page; all by default)
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

/** Each scene opens its screen in `b` (signed in) and names the rows to check (CSS selectors). */
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
    'player-page': {
        rows: ['.player-row'],
        async open(b) {
            await b.goto(url + 'player?path=' + enc('Videos/Holidays/Mountain drive.webm'), 3000);
            await pauseVideo(b);
        },
    },
};

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
            let something = false;
            for (const selector of scene.rows) {
                const result = JSON.parse(await b.eval(measure(selector)));
                const ok = !result.missing && result.lines === 1 && result.inside && result.onScreen && result.pageFits;
                failed ||= !ok;
                something ||= result.more;
                console.log(`${ok ? 'ok  ' : 'FAIL'} ${name} ${size} ${selector} ${JSON.stringify(result)}`);
            }
            await b.shot(`${out}-${name}-${size}.png`);
            if (something) {
                await b.eval(`document.querySelector('.overflow-more.shown').click(); 1`);
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
