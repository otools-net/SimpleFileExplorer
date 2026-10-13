// Documentation screenshots: node shots.mjs [scene...]
import { fileURLToPath } from 'node:url';
import { launch, sleep } from './cdp.mjs';

const BASE = process.env.SFE_URL ?? 'http://localhost:5093/';
const OUT = fileURLToPath(new URL('../../../docs/images/', import.meta.url));
const enc = encodeURIComponent;

async function session({ user = 'admin', password = 'admin-password-1', ...opts } = {}) {
    const b = await launch({ width: 1280, height: 800, ...opts });
    await b.goto(BASE + 'login', 1500);
    if (user) await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: '${user}', password: '${password}', remember: true }) }).then(r => r.status)`);
    b.open = async (url, wait = 3500) => {
        await b.goto(BASE + url, wait);
        await b.eval(`document.querySelector('button[title="Hide this panel"]')?.click(); 1`);
        await sleep(300);
    };
    b.clickText = async (selector, text) => {
        const ok = await b.eval(`(() => { const el = [...document.querySelectorAll(${JSON.stringify(selector)})].find(e => e.textContent.trim().includes(${JSON.stringify(text)})); if (!el) return false; el.click(); return true; })()`);
        if (!ok) throw new Error(`no ${selector} with ${text}`);
        await sleep(800);
    };
    b.clickTitle = async title => { await b.click(`[title^="${title}"]`); await sleep(800); };
    return b;
}

/** Replaces this machine's scratch library path with a neutral one, in text and inputs. */
async function mask(b) {
    await b.eval(`(() => {
        const re = /[A-Z]:[\\\\/]Users[\\\\/][^\\s"']*?[\\\\/]library/gi;
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        for (let n; (n = walker.nextNode());) n.nodeValue = n.nodeValue.replace(re, 'D:\\\\Media');
        for (const i of document.querySelectorAll('input, textarea')) i.value = i.value.replace(re, 'D:\\\\Media');
        return 1;
    })()`);
}

/** The admin page at a section and sub-page. */
const admin = (section, page) => `admin?section=${section}` + (page ? `&page=${page}` : '');
const api = (method, url, data) => `fetch('${url}', { method: '${method}', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: ${data === undefined ? 'undefined' : `JSON.stringify(${JSON.stringify(data)})`} }).then(r => r.status)`;

const scenes = {
    async login() {
        const b = await session({ user: null });
        await b.goto(BASE + 'login', 2500);
        await b.shot(OUT + 'login-page.png');
        await b.close();
    },
    async library() {
        const b = await session();
        await b.open('');
        await b.shot(OUT + 'library-root.png');
        await b.goto(BASE + '?path=Photos%2F2024%20Beach%20trip', 3000);
        await b.shot(OUT + 'scan-finished.png', { x: 0, y: 0, width: 1280, height: 120 });
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        await b.shot(OUT + 'library-grid.png');
        await b.clickTitle('List');
        await sleep(1500);
        await b.shot(OUT + 'library-list.png');
        await b.clickTitle('One per row');
        await sleep(2000);
        await b.shot(OUT + 'library-single.png');
        await b.clickTitle('Grid');
        await b.open('?path=Photos');
        await b.shot(OUT + 'library-folders.png');
        await b.close();
    },
    async menu() {
        const b = await session();
        await b.open('?path=Photos');
        await b.clickTitle('Scan this folder, administration');
        await b.shot(OUT + 'main-menu.png');
        await b.close();
    },
    async viewer() {
        const b = await session();
        await b.open('?path=Photos%2F2025%20Mountains');
        await b.click('a.item-link[href*="Summit"]');
        await sleep(2500);
        await b.shot(OUT + 'viewer.png');
        await b.clickTitle('Edit');
        await sleep(2000);
        await b.shot(OUT + 'image-editor.png');
        await b.clickText('button', '16:9');
        await sleep(800);
        await b.shot(OUT + 'image-editor-crop.png');
        await b.close();
    },
    async video() {
        const b = await session();
        await b.open('?path=Videos%2FHolidays');
        await b.click('a.item-link[href*="Mountain"]');
        await sleep(2500);
        await b.eval(`(async () => { const v = document.querySelector('video'); v.pause(); v.currentTime = 12.4; await new Promise(r => setTimeout(r, 800)); return v.currentTime; })()`);
        await b.mouse('mouseMoved', 640, 400);
        await sleep(400);
        await b.shot(OUT + 'video-viewer.png');
        // Trickplay above the seek bar (with ffmpeg: frames made by seed.mjs).
        const seek = await b.rect('input.player-seek');
        await b.mouse('mouseMoved', seek.x + seek.width * 0.62, seek.y + seek.height / 2);
        await sleep(1500);
        await b.shot(OUT + 'player-trickplay.png');
        await b.goto(BASE + 'player?path=' + enc('Videos/Timelapses/Sunset timelapse.webm') + '&t=14', 3000);
        await b.eval(`(async () => { const v = document.querySelector('video'); v.pause(); await new Promise(r => setTimeout(r, 500)); return 1; })()`);
        await b.mouse('mouseMoved', 640, 400);
        await sleep(400);
        await b.shot(OUT + 'player-page.png');
        await b.close();
    },
    async videos() {
        const b = await session();
        await b.open('?path=Videos%2FHolidays', 5000);
        await b.shot(OUT + 'library-videos.png');
        // Hover preview: trickplay frames played over the thumbnail.
        await b.hover('.files > *:nth-child(2) img, .files > *:nth-child(2)');
        await sleep(2600);
        const tile = await b.rect('.files > *:nth-child(2)');
        await b.shot(OUT + 'trickplay-hover.png', { x: Math.max(0, tile.x - 16), y: Math.max(0, tile.y - 16), width: tile.width + 32, height: tile.height + 32 });
        await b.open('?path=Music', 3000);
        await b.clickTitle('List');
        await sleep(1200);
        await b.shot(OUT + 'library-audio.png', { x: 0, y: 0, width: 1280, height: 360 });
        await b.clickTitle('Grid');
        await b.close();
    },
    async tags() {
        const b = await session();
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        await b.click('button.tag-button');
        await sleep(1500);
        await b.shot(OUT + 'file-tags-dialog.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(600);
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        for (const name of ['Dunes', 'Harbour', 'Night swim'])
            await b.realClick(`input[aria-label="Select ${name}.jpg"], input[title="Select ${name}.jpg"]`);
        await sleep(600);
        await b.shot(OUT + 'selection-bar.png');
        await b.clickText('.selection-bar button, [class*=selection] button', 'Tags');
        await sleep(1500);
        await b.shot(OUT + 'files-tags-dialog.png');
        await b.close();
    },
    async scopeTags() {
        const b = await session();
        await b.open('?path=Photos');
        await b.click('button.scope-tags-button');
        await sleep(2000);
        await b.shot(OUT + 'scope-tags-dialog.png');
        await b.close();
    },
    async search() {
        const b = await session();
        await b.open('');
        await b.realClick('input[aria-label="Search"], .search-box input');
        await b.type('t:places -e:png s:>50kb');
        await b.key('Enter', 'Enter', 13);
        await sleep(3000);
        await b.shot(OUT + 'search-results.png');
        await b.clickTitle('Explain');
        await sleep(800);
        await b.shot(OUT + 'search-explain.png');
        await b.close();
    },
    async searchBox() {
        const b = await session();
        await b.open('');
        await b.realClick('input[aria-label="Search"]');
        await b.type('sunset');
        await b.key('Enter', 'Enter', 13);
        await sleep(2000);
        await b.open('');
        await b.realClick('input[aria-label="Search"]');
        await b.type('t:mo');
        await sleep(1200);
        await b.shot(OUT + 'search-tag-suggestions.png', { x: 0, y: 0, width: 1280, height: 420 });
        await b.eval(`(() => { const i = document.querySelector('input[aria-label="Search"]'); i.value = ''; i.dispatchEvent(new Event('input', { bubbles: true })); return 1; })()`);
        await sleep(1000);
        await b.shot(OUT + 'search-history.png', { x: 0, y: 0, width: 1280, height: 420 });
        await b.key('Escape', 'Escape', 27);
        await b.open('');
        await b.clickTitle('Search syntax and tips');
        await sleep(800);
        await b.shot(OUT + 'search-help.png');
        await b.close();
    },
    async removeFromIndex() {
        const b = await session();
        await b.open('?path=Inbox');
        await b.clickTitle('Scan this folder, administration');
        await b.clickText('[role=menu] button, [role=menuitem]', 'Remove from index');
        await sleep(1500);
        await b.shot(OUT + 'remove-from-index.png');
        await b.close();
    },
    async history() {
        const b = await session();
        await b.eval(`localStorage.setItem('sfe.searchHistory', JSON.stringify(['t:beach -t:sea', 'd:>5s e:webm', 'sunset', 'r:hr', '*2024 lighthouse'])); 1`);
        await b.open('');
        await b.realClick('input[aria-label="Search"]');
        await sleep(1200);
        await b.shot(OUT + 'search-history.png', { x: 0, y: 0, width: 1280, height: 420 });
        await b.close();
    },
    async runLog() {
        const b = await session({ height: 900 });
        await b.open(admin('tasks', 'history'));
        await b.eval(`[...document.querySelectorAll('button.task-run-head')][1].click(); 1`);
        await sleep(1500);
        await mask(b);
        await b.shot(OUT + 'task-run-log.png');
        await b.close();
    },
    async dialogs() {
        const b = await session({ height: 900 });
        await b.open(admin('tags', 'list'));
        await b.clickTitle('Edit Sea');
        await b.shot(OUT + 'tag-editor.png');
        await b.open(admin('access', 'spaces'));
        await b.click('button.space-card');
        await sleep(1500);
        await mask(b);
        await b.shot(OUT + 'admin-access-space.png');
        await b.clickTitle('Change what Users may do');
        await sleep(800);
        await b.shot(OUT + 'group-editor.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(600);
        await b.clickTitle('Change sam');
        await b.shot(OUT + 'account-editor.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(600);
        await b.clickTitle('Rename the space');
        await mask(b);
        await b.shot(OUT + 'space-editor.png');
        await b.open(admin('access', 'spaces'));
        await b.clickTitle('Create a space on a folder');
        await sleep(1200);
        await mask(b);
        await b.shot(OUT + 'space-new.png');
        await b.open(admin('tasks', 'schedules'));
        await b.clickTitle('Add a schedule');
        await sleep(1500);
        await b.shot(OUT + 'schedule-form.png');
        await b.open(admin('tasks', 'history'));
        await b.eval(`[...document.querySelectorAll('button.task-run-head')][1].click(); 1`);
        await sleep(1500);
        await mask(b);
        await b.shot(OUT + 'task-run-log.png');
        await b.close();
    },
    async options() {
        const b = await session();
        await b.open('');
        const toggle = on => b.eval(`(async () => {
            const h = { 'Content-Type': 'application/json', 'X-SFE-Request': '1' };
            const view = await (await fetch('api/settings?level=tenant')).json();
            const values = view.values;
            values.browse.copyPaths = ${on}; values.browse.resolutionMark = ${on};
            const r = await fetch('api/settings?level=tenant', { method: 'PUT', headers: h, body: JSON.stringify(values) });
            return r.status + ' ' + (r.ok ? '' : await r.text());
        })()`);
        console.log(await toggle(true));
        await b.open('?path=Photos%2F2025%20Mountains');
        await b.hover('.files > *:nth-child(2)');
        await sleep(800);
        await mask(b);
        await b.shot(OUT + 'library-marks-copy-paths.png');
        console.log(await toggle(false));
        await b.close();
    },
    async phone() {
        const b = await session({ width: 390, height: 844, mobile: true });
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        await b.shot(OUT + 'phone-library.png');
        await b.eval('window.scrollTo(0, 700); 1');
        await sleep(1500);
        await b.shot(OUT + 'phone-scrolled.png');
        await b.open('admin');
        await b.shot(OUT + 'phone-admin.png');
        await b.close();
    },
    async light() {
        const b = await session({ colorScheme: 'light' });
        await b.eval(`localStorage.setItem('sfe.theme', 'light'); 1`);
        await b.open('?path=Photos');
        await b.shot(OUT + 'library-light.png');
        await b.close();
    },
    async admin() {
        const b = await session({ height: 900 });
        const pages = [
            ['interface', 'appearance'], ['interface', 'browsing'], ['interface', 'search'], ['playback'],
            ['scanning', 'skipped'], ['scanning', 'fileTypes'], ['scanning', 'indexing'],
            ['tags', 'list'], ['tags', 'settings'],
            ['tracking', 'duplicates'], ['tracking', 'similar'], ['tracking', 'missing'], ['tracking', 'settings'],
            ['thumbnails'], ['trickplay', 'activity'], ['trickplay', 'settings'], ['editor'],
            ['access', 'spaces'], ['access', 'accounts'],
            ['tasks', 'tasks'], ['tasks', 'settings'],
            ['system'], ['account', 'preferences'], ['account', 'password'],
        ];
        for (const [section, page] of pages) {
            await b.open(admin(section, page), 3000);
            await mask(b);
            await b.shot(OUT + `admin-${section}${page ? '-' + page : ''}.png`);
        }
        await b.close();
    },
};

const names = process.argv.slice(2);
for (const name of names.length ? names : Object.keys(scenes)) {
    console.log('--', name);
    try { await scenes[name](); } catch (e) { console.error(name, 'failed:', e.message); }
}
