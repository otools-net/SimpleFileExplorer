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

/**
 * The operations panel asking about a name clash: a photo copied into Inbox twice (the second copy waits for the
 * answer), the page opened again to follow it, Skip; then the copy is deleted so that the library stays as it was.
 */
async function operations() {
    const b = await session();
    const post = (url, body) => `fetch('${url}', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify(${JSON.stringify(body)}) }).then(r => r.status)`;
    const copy = { kind: 'copy', paths: ['Photos/Sunrise.jpg'], target: 'Inbox' };
    await b.open('?path=Inbox');
    await b.eval(post('api/files/jobs', copy));
    await sleep(1500);
    await b.eval(post('api/files/jobs', copy));
    await b.open('?path=Inbox', 3000);
    await b.shot(OUT + 'operations-panel.png');
    await b.clickTitle('Leave this one out');
    await sleep(1000);
    await b.eval(post('api/files/delete', { paths: ['Inbox/Sunrise.jpg'], permanent: true }));
    await b.close();
}

/** Replaces this machine's scratch library path with a neutral one, in text and inputs. */
async function mask(b) {
    await b.eval(`(() => {
        const re = /[A-Z]:[\\\\/]Users[\\\\/][^\\s"']*?[\\\\/]library/gi;
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        for (let n; (n = walker.nextNode());) n.nodeValue = n.nodeValue.replace(re, 'D:\\\\Media');
        for (const i of document.querySelectorAll('input, textarea')) i.value = i.value.replace(re, 'D:\\\\Media');
        // The Logs page: the app's own folder (content root) on this machine.
        const app = /[A-Z]:[\\\\/][^\\s"']*?[\\\\/]src[\\\\/]Sfe\\.Api/gi;
        for (const e of document.querySelectorAll('.log-message, .log-entry-details')) e.textContent = e.textContent.replace(app, 'C:\\\\Program Files\\\\SFE');
        // System › About: this machine's ffmpeg.
        for (const c of document.querySelectorAll('.system-ffmpeg code')) c.textContent = 'C:\\\\Program Files\\\\ffmpeg\\\\bin\\\\ffmpeg.exe';
        for (const i of document.querySelectorAll('#installation-ffmpeg')) i.value = 'C:\\\\Program Files\\\\ffmpeg\\\\bin\\\\ffmpeg.exe';
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
        // Account recovery: the code is asked for (and shown in the app's console, app.log), the form waits for it.
        await b.click('a[title^="Recover a server"]');
        await sleep(300);
        await b.eval(`(() => { const i = document.querySelector('#recover-name'); i.value = 'admin'; i.dispatchEvent(new Event('input')); })()`);
        await sleep(200);
        await b.click('button[title^="Show a recovery code"]');
        await sleep(1000);
        await b.shot(OUT + 'login-recover.png');
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
    async quickLinks() {
        const b = await session();
        await b.open('?path=Photos');
        await b.shot(OUT + 'quick-links.png', { x: 0, y: 60, width: 260, height: 220 });
        // A folder card dragged between the first two links: the line shows where it goes.
        const second = await b.rect('li.quick-link:nth-child(2)');
        await b.drag('.folder-item[data-path="Photos/2024 Beach trip"] a.folder', second.x + 60, second.y + 4, { drop: false });
        await sleep(400);
        await b.shot(OUT + 'quick-links-drag.png');
        await b.send('Input.dispatchDragEvent', { type: 'dragCancel', x: 0, y: 0, data: { items: [], dragOperationsMask: 1 } }).catch(() => { });
        await b.close();
        // Phones: the drawer, in the edit mode (no drag and drop there).
        const phone = await session({ width: 390, height: 844, mobile: true });
        await phone.open('?path=Photos');
        await phone.clickTitle('Show the sidebar');
        await phone.clickTitle('Edit: move and remove');
        await sleep(400);
        await phone.shot(OUT + 'phone-quick-links.png');
        await phone.close();
    },
    async menu() {
        const b = await session();
        await b.open('?path=Photos');
        await b.clickTitle('Scan this folder, administration');
        await b.shot(OUT + 'main-menu.png');
        await b.close();
    },
    /**
     * The keep alive indicator of the ⋮ menu: keep alive turned on with a more folder that doesn't exist, so that its
     * ping fails (the yellow !); then turned off again for the other scenes.
     */
    async keepAliveMenu() {
        const b = await session();
        const settings = values => `fetch('api/settings?level=tenant', { method: 'PATCH', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ values: ${JSON.stringify(values)} }) }).then(r => r.status)`;
        await b.open('?path=Photos');
        await b.eval(settings({ 'keepAlive.mode': 'always', 'keepAlive.folders': ['Gone'] }));
        await sleep(2000);
        await b.open('?path=Photos', 3000);
        await b.clickTitle('Scan this folder, administration');
        await b.shot(OUT + 'main-menu-keep-alive.png', { x: 960, y: 0, width: 320, height: 200 });
        await b.eval(settings({ 'keepAlive.mode': 'off', 'keepAlive.folders': [] }));
        await b.close();
    },
    async viewer() {
        const b = await session();
        await b.open('?path=Photos%2F2025%20Mountains');
        await b.click('a.item-link[href*="Summit"]');
        await sleep(2500);
        await b.shot(OUT + 'viewer.png');
        // Actual size: a click on the summit, then a drag with the hand.
        const picture = await b.rect('.viewer-stage > img');
        const x = picture.x + picture.width * 0.6, y = picture.y + picture.height * 0.35;
        await b.mouse('mouseMoved', x, y);
        await b.mouse('mousePressed', x, y);
        await b.mouse('mouseReleased', x, y);
        await sleep(500);
        await b.mouse('mousePressed', x, y);
        await b.mouse('mouseMoved', x - 120, y - 60, { buttons: 1 });
        await sleep(300);
        await b.shot(OUT + 'viewer-actual-size.png');
        await b.mouse('mouseReleased', x - 120, y - 60);
        await b.key('z', 'KeyZ', 90);
        await sleep(500);
        await b.click('.viewer button[aria-label="More actions"]');
        await sleep(300);
        await b.click('.viewer-menu [data-entry="crop"]');
        await sleep(2000);
        await b.shot(OUT + 'image-editor.png');
        await b.clickText('button', '16:9');
        await sleep(800);
        await b.shot(OUT + 'image-editor-crop.png');
        await b.close();
    },
    /** Text files in the viewer: a script highlighted, then a log with the find bar. */
    async textViewer() {
        const b = await session();
        await b.open('?path=Documents');
        await b.click('a.item-link[title^="Rename photos.ps1"]');
        await sleep(2500);
        await b.shot(OUT + 'text-viewer.png');
        await b.close();
        const log = await session();
        await log.open('?path=Documents');
        await log.click('a.item-link[title^="Photo backup.log"]');
        await sleep(2500);
        await log.click('.viewer .text-toolbar button[aria-label="Find"]');
        await sleep(300);
        await log.type('IMG_2231');     // the find bar's field has the focus
        await log.key('Enter', 'Enter', 13);
        await sleep(600);
        await log.shot(OUT + 'text-viewer-find.png');
        await log.close();
        // Prose: the reading settings, in sepia.
        const diary = await session();
        await diary.open('?path=Documents');
        await diary.click('a.item-link[title^="Trip diary.txt"]');
        await sleep(2500);
        await diary.click('.viewer .text-toolbar button[aria-label="Reading settings"]');
        await sleep(300);
        await diary.click('.reading-theme[data-reader-theme="sepia"]');
        await sleep(1200);
        await diary.shot(OUT + 'text-reading-settings.png');
        await diary.close();
        // Editing (nothing saved: the sample stays as it is for the other shots).
        const notes = await session();
        await notes.open('?path=Documents');
        await notes.click('a.item-link[title^="Trip diary.txt"]');
        await sleep(2500);
        await notes.click('.viewer [data-edit]');
        await sleep(400);
        await notes.key('End', 'End', 35, 2);     // Ctrl+End: after the last line
        await notes.type('\n\nDay 6 — back home. Unpack, wash the boots, print the best photos.');
        await sleep(600);
        await notes.shot(OUT + 'text-editor.png');
        await notes.click('.viewer [data-save-copy]');
        await sleep(600);
        await notes.shot(OUT + 'text-editor-copy.png');
        await notes.close();
        // Markdown: rendered with its outline, then side by side with its source.
        const guide = await session();
        await guide.open('?path=Documents');
        await guide.click('a.item-link[title^="Hiking guide.md"]');
        await sleep(3000);
        await guide.shot(OUT + 'markdown-viewer.png');
        await guide.clickText('.markdown-mode', 'Split');
        await sleep(1500);
        await guide.shot(OUT + 'markdown-split.png');
        await guide.close();
        // JSON: the tree with a member selected, then side by side with its source, filtered.
        const plan = await session();
        await plan.open('?path=Documents');
        await plan.click('a.item-link[title^="Trip plan.json"]');
        await sleep(2500);
        await plan.click('.json-row[data-path="/days/1/route"]');
        await sleep(400);
        await plan.shot(OUT + 'json-viewer.png');
        await plan.clickText('.markdown-mode', 'Split');
        await plan.eval(`(() => { const box = document.querySelector('.json-filter'); box.value = 'chamonix'; box.dispatchEvent(new Event('input')); return 1; })()`);
        await sleep(1500);
        await plan.shot(OUT + 'json-split.png');
        await plan.close();
        // CSV: a table sorted by a column, a cell selected.
        const huts = await session();
        await huts.open('?path=Documents');
        await huts.click('a.item-link[title^="Huts.csv"]');
        await sleep(2500);
        await huts.click('.csv-sort[data-column="2"]');
        await sleep(300);
        await huts.click('.csv-sort[data-column="2"]');
        await sleep(300);
        await huts.click('.csv-body [data-record="4"] [data-column="0"]');
        await sleep(400);
        await huts.shot(OUT + 'csv-viewer.png');
        await huts.close();
        // Subtitles: the cues, one selected (Play: the video next to them).
        const subtitles = await session();
        await subtitles.open('?path=Videos%2FHolidays');
        await subtitles.click('a.item-link[title^="Campfire.en.srt"]');
        await sleep(2500);
        await subtitles.click('.subtitle-cue[data-cue="2"]');
        await sleep(400);
        await subtitles.shot(OUT + 'subtitles-viewer.png');
        await subtitles.close();
        // PDF: drawn by pdf.js (worker, standard fonts and styles served by the app), its outline beside it.
        const handbook = await session();
        await handbook.open('?path=Documents');
        await handbook.click('a.item-link[title^="Hut handbook.pdf"]');
        await sleep(4000);
        const drawn = await handbook.eval(`document.querySelectorAll('.pdfViewer .page canvas').length`);
        console.log(`pdf pages drawn: ${drawn}`);
        if (!drawn)
            throw new Error('pdf.js drew no page');
        await handbook.shot(OUT + 'pdf-viewer.png');
        await handbook.close();
        // EPUB: two pages side by side with the contents, then the same book scrolled.
        const book = await session();
        await book.open('?path=Documents');
        await book.click('a.item-link[title^="Hut stories.epub"]');
        await sleep(3500);
        await book.click('.viewer [data-toc]');
        await book.click('.viewer .markdown-mode[data-mode="spread"]');
        await sleep(2500);
        const frames = await book.eval(`document.querySelectorAll('.epub-stage iframe').length`);
        console.log(`epub frames: ${frames}`);
        if (!frames)
            throw new Error('epub.js showed no page');
        await book.shot(OUT + 'epub-reader.png');
        await book.click('.viewer .markdown-mode[data-mode="scroll"]');
        await sleep(2500);
        await book.shot(OUT + 'epub-scroll.png');
        // Back as the docs' other shots expect (one page, no contents).
        await book.click('.viewer .markdown-mode[data-mode="pages"]');
        await book.click('.viewer [data-toc]');
        await book.close();
        // A page of its own (a text file opened in a new tab).
        const page = await session();
        await page.open('view?path=Documents%2FRename%20photos.ps1');
        await sleep(1500);
        await page.shot(OUT + 'text-view-page.png');
        await page.close();
        // Phones: the settings as a sheet at the bottom.
        const phone = await session({ width: 390, height: 844, mobile: true });
        await phone.open('?path=Documents');
        await phone.click('a.item-link[title^="Trip diary.txt"]');
        await sleep(2500);
        await phone.click('.viewer .text-toolbar button[aria-label="Reading settings"]');
        await sleep(800);
        await phone.shot(OUT + 'text-reading-phone.png');
        await phone.close();
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
    async defaultTags() {
        const b = await session();
        await b.open('?path=Photos%2FFamily');
        await b.click('button.scope-tags-button');
        await sleep(2000);
        await b.realClick('.default-picker input');
        await b.type('Sunset');
        await sleep(1200);
        await b.click('.default-picker .tag-options li');
        await sleep(800);
        await b.shot(OUT + 'default-tags-dialog.png');
        await b.close();
        // The folder's icon, its panel opened by a click.
        const h = await session();
        await h.open('?path=Photos');
        await h.realClick('.folder-item[data-path="Photos/Family"] .default-tags-button');
        await sleep(800);
        const card = await h.rect('.folder-item[data-path="Photos/Family"]');
        const panel = await h.rect('.default-tags-popover');
        const x = Math.max(0, Math.min(card.x, panel.x) - 16), y = Math.max(0, card.y - 16);
        await h.shot(OUT + 'default-tags-popover.png', { x, y, width: Math.max(card.x + card.width, panel.x + panel.width) + 16 - x,
            height: Math.max(card.y + card.height, panel.y + panel.height) + 16 - y });
        await h.close();
        // On a phone, a tap shows it along the bottom of the screen.
        const phone = await session({ width: 390, height: 844, mobile: true });
        await phone.open('?path=Photos');
        await phone.realClick('.folder-item[data-path="Photos/Family"] .default-tags-button');
        await sleep(800);
        await phone.shot(OUT + 'phone-default-tags.png');
        await phone.close();
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
    /** Folders kept out of scans and previews: the chips in the listing, then the dialog; put back after. */
    async exclusions() {
        const b = await session();
        await b.goto(BASE, 1500);
        await b.eval(api('PUT', 'api/index/exclusions', { path: 'Inbox', scan: true }));
        await b.eval(api('PUT', 'api/index/exclusions', { path: 'Music', trickplay: true, thumbnails: true }));
        await b.open('');
        await b.shot(OUT + 'excluded-folders.png');
        await b.open('?path=Inbox');
        await b.clickTitle('Kept out of scans');
        await sleep(800);
        await b.shot(OUT + 'folder-exclusions.png');
        const page = await session({ height: 900 });
        await page.open(admin('scanning', 'exclusions'), 3000);
        await page.shot(OUT + 'admin-scanning-exclusions.png');
        await page.close();
        await b.eval(api('PUT', 'api/index/exclusions', { path: 'Inbox', scan: false }));
        await b.eval(api('PUT', 'api/index/exclusions', { path: 'Music', trickplay: false, thumbnails: false }));
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
        await b.eval(`document.querySelector('sfe-folder-access').scrollIntoView({ block: 'start' }); 1`);
        await sleep(400);
        await b.clickTitle('Who may see and change Documents');
        await sleep(800);
        // the page only grows long enough once the report is open
        await b.eval(`document.querySelector('sfe-folder-access').scrollIntoView({ block: 'start' }); 1`);
        await sleep(400);
        await mask(b);
        await b.shot(OUT + 'folder-access.png');
        await b.eval(`document.querySelector('.space-title, h3').scrollIntoView({ block: 'start' }); 1`);
        await sleep(300);
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
        await b.open(admin('tracking', 'missing'));
        await b.shot(OUT + 'phone-admin-section.png');
        // The item menu as a sheet at the bottom, from an item's ⋮ button.
        await b.open('?path=Photos');
        await b.eval(`document.querySelector('.folder-item[data-path="Photos/2024 Beach trip"] .item-menu-button').click(); 1`);
        await sleep(800);
        await b.shot(OUT + 'phone-item-menu.png');
        await b.close();
    },
    async batchRename() {
        const b = await session();
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        for (const name of ['Dunes', 'Harbour', 'Night swim'])
            await b.realClick(`input[aria-label="Select ${name}.jpg"], input[title="Select ${name}.jpg"]`);
        await sleep(400);
        await b.clickTitle('Rename the selected items together');
        await sleep(800);
        await b.eval(`(() => {
            document.querySelector('sfe-batch-rename-dialog input[value="number"]').click();
            return 1;
        })()`);
        await sleep(300);
        await b.eval(`(() => {
            const input = document.querySelector('sfe-batch-rename-dialog input[name="pattern"]');
            input.value = 'Beach trip ##';
            input.dispatchEvent(new Event('input'));
            return 1;
        })()`);
        await sleep(600);
        await b.shot(OUT + 'batch-rename-dialog.png');
        await b.close();
    },
    async light() {
        const b = await session({ colorScheme: 'light' });
        await b.eval(`localStorage.setItem('sfe.theme', 'light'); 1`);
        await b.open('?path=Photos');
        await b.shot(OUT + 'library-light.png');
        await b.close();
    },
    async uploads() {
        const b = await session();
        const call = (method, url, body) => `fetch('${url}', { method: '${method}', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: ${body ? `JSON.stringify(${JSON.stringify(body)})` : 'undefined'} }).then(r => r.status === 204 ? null : r.json())`;
        await b.open('?path=Photos');
        // Three pictures dropped from the computer onto the page, one of them with a name already taken there.
        await b.eval(`(() => {
            const data = new DataTransfer();
            const bytes = n => new Uint8Array(n).map((_, i) => i % 251);
            data.items.add(new File([bytes(3_500_000)], 'Sunrise.jpg', { type: 'image/jpeg' }));
            data.items.add(new File([bytes(2_400_000)], 'Harbour at night.jpg', { type: 'image/jpeg' }));
            data.items.add(new File([bytes(1_800_000)], 'Old town.jpg', { type: 'image/jpeg' }));
            const page = document.querySelector('main.page');
            page.dispatchEvent(new DragEvent('dragover', { dataTransfer: data, bubbles: true, cancelable: true }));
            page.dispatchEvent(new DragEvent('drop', { dataTransfer: data, bubbles: true, cancelable: true }));
            return 1;
        })()`);
        await sleep(2500);
        await b.shot(OUT + 'upload-panel.png');
        await b.clickTitle("Don't upload this one");
        await sleep(1500);
        await b.eval(call('POST', 'api/files/delete', { paths: ['Photos/Harbour at night.jpg', 'Photos/Old town.jpg'], permanent: true }));
        // An upload cut off halfway, for the administration's list.
        const started = await b.eval(call('POST', 'api/files/uploads', { target: 'Videos', name: 'Holiday 2025.webm', size: 48_000_000, modified: null }));
        await b.eval(`fetch('api/files/uploads/${started.id}?offset=0', { method: 'PUT', headers: { 'Content-Type': 'application/octet-stream', 'X-SFE-Request': '1' }, body: new Uint8Array(8_388_608) }).then(r => r.status)`);
        await b.close();
        const a = await session({ height: 900 });
        await a.open(admin('files', 'uploads'), 3000);
        await a.shot(OUT + 'admin-files-uploads.png');
        await a.eval(call('DELETE', `api/files/uploads/${started.id}`));
        await a.close();
    },
    async files() {
        const b = await session();
        await b.open('?path=Photos');
        // A folder's menu, opened by a right click on its card.
        const card = await b.rect('.folder-item[data-path="Photos/2024 Beach trip"] a.folder');
        const x = card.x + card.width / 2, y = card.y + card.height / 3;
        await b.mouse('mouseMoved', x, y);
        await b.mouse('mousePressed', x, y, { button: 'right' });
        await b.mouse('mouseReleased', x, y, { button: 'right' });
        await sleep(600);
        await b.shot(OUT + 'item-menu.png');
        await b.clickTitle('Give 2024 Beach trip another name');
        await sleep(600);
        await b.shot(OUT + 'rename-dialog.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(400);
        await b.clickTitle('New folder in this folder');
        await sleep(600);
        await b.shot(OUT + 'new-folder-dialog.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(400);
        // The folder listed's menu: a right click on the listing's background (here, its counts).
        const summary = await b.rect('.location-bar .summary');
        await b.mouse('mouseMoved', summary.x + 10, summary.y + summary.height / 2);
        await b.mouse('mousePressed', summary.x + 10, summary.y + summary.height / 2, { button: 'right' });
        await b.mouse('mouseReleased', summary.x + 10, summary.y + summary.height / 2, { button: 'right' });
        await sleep(600);
        await b.shot(OUT + 'background-menu.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(400);
        // A folder and a file selected: the bar counts both and offers what applies.
        await b.realClick('.folder-item[data-path="Photos/2025 Mountains"] .select-box input');
        await b.realClick('input[aria-label="Select Sunrise.jpg"]');
        await sleep(600);
        await b.shot(OUT + 'selection-folders.png');
        // Move: the folder picker, from the selection bar.
        await b.clickTitle('Move the selected items to another folder');
        await sleep(1000);
        await b.clickTitle('Open Family');
        await sleep(1000);
        await b.shot(OUT + 'move-dialog.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(400);
        // Deleted to the trash (the selection), then put back below so that the other scenes find them.
        await b.clickTitle('Move the selected items to the trash');
        await sleep(600);
        await b.shot(OUT + 'delete-dialog.png');
        await b.eval(`[...document.querySelectorAll('sfe-delete-dialog button')].find(x => x.textContent.trim() === 'Delete').click(); 1`);
        await sleep(1500);
        await b.close();
        const a = await session({ height: 900 });
        await a.open(admin('files', 'trash'), 3000);
        await a.shot(OUT + 'admin-files-trash.png');
        for (let i = 0; i < 2; i++) {
            await a.eval(`document.querySelector('.trash-section button[title^="Put it back"]')?.click(); 1`);
            await sleep(1500);
        }
        await a.open(admin('files', 'settings'), 3000);
        await a.shot(OUT + 'admin-files.png');
        await a.close();
        await operations();
    },
    async properties() {
        const b = await session();
        /** Right click on the item `selector`, then Properties in its menu. */
        const properties = async selector => {
            const box = await b.rect(selector);
            const x = box.x + box.width / 2, y = box.y + box.height / 3;
            await b.mouse('mouseMoved', x, y);
            await b.mouse('mousePressed', x, y, { button: 'right' });
            await b.mouse('mouseReleased', x, y, { button: 'right' });
            await sleep(500);
            await b.eval(`document.querySelector('.item-menu [data-entry="properties"]').click(); 1`);
            await sleep(1500);
        };
        // A picture, with its checksum.
        await b.open('?path=Photos%2F2024%20Beach%20trip');
        await properties('[data-path="Photos/2024 Beach trip/Dunes.jpg"]');
        await b.eval(`document.querySelector('sfe-properties-dialog .checksum-button').click(); 1`);
        await sleep(2000);
        await b.shot(OUT + 'properties-file.png');
        await b.key('Escape', 'Escape', 27);
        await sleep(400);
        // A folder: what the index knows, then counted on the disk.
        await b.open('?path=Photos');
        await properties('.folder-item[data-path="Photos/2024 Beach trip"] a.folder');
        await b.eval(`document.querySelector('sfe-properties-dialog .count-button').click(); 1`);
        await sleep(2000);
        await b.shot(OUT + 'properties-folder.png');
        await b.close();
    },
    async admin() {
        const b = await session({ height: 900 });
        const pages = [
            ['interface', 'appearance'], ['interface', 'browsing'], ['interface', 'search'], ['playback', 'player'], ['playback', 'streaming'],
            ['scanning', 'skipped'], ['scanning', 'fileTypes'], ['scanning', 'indexing'],
            ['tags', 'list'], ['tags', 'settings'],
            ['tracking', 'duplicates'], ['tracking', 'similar'], ['tracking', 'missing'], ['tracking', 'settings'], ['keepAlive', 'settings'],
            ['thumbnails'], ['trickplay', 'activity'], ['trickplay', 'settings'], ['editor'],
            ['access', 'spaces'], ['access', 'accounts'],
            ['tasks', 'tasks'], ['tasks', 'settings'],
            ['system'], ['system', 'maintenance'], ['account', 'preferences'], ['account', 'password'],
        ];
        for (const [section, page] of pages) {
            await b.open(admin(section, page), 3000);
            await mask(b);
            await b.shot(OUT + `admin-${section}${page ? '-' + page : ''}.png`);
        }
        // Keep alive's status after a ping (the sample library is local: "Not on a network share").
        await b.open(admin('keepAlive', 'status'), 3000);
        await b.clickTitle('Ping the library folder now');
        await sleep(1000);
        await b.shot(OUT + 'admin-keepAlive-status.png');
        // The overview of the sections, which /admin opens on.
        await b.open('admin', 3000);
        await b.shot(OUT + 'admin-overview.png');
        await b.close();
    },
    async adminSearch() {
        const b = await session({ height: 900 });
        await b.open('admin', 3000);
        await b.eval(`document.querySelector('.admin-search input').focus(); 1`);
        await b.type('high resolution');
        await sleep(600);
        // The second one found opened: its setting is brought into view and flashes.
        await b.eval(`document.querySelectorAll('.admin-search-results a')[1].click(); 1`);
        await sleep(700);
        await b.shot(OUT + 'admin-search.png');
        await b.close();
    },
    async logs() {
        const b = await session({ height: 900 });
        // A wrong password, logged as a security warning (the session stays: a failed sign-in changes no cookie).
        await b.eval(api('POST', 'api/session/login', { userName: 'sam', password: 'not-sams-password' }));
        await sleep(2500);
        await b.open(admin('logs'), 2500);
        await mask(b);
        await b.shot(OUT + 'admin-logs.png');
        // Warnings, security warnings and errors only, the first one opened (on its error when it has one).
        await b.eval(`[...document.querySelectorAll('.logs-level-chip')].filter(l => /Warning|warning|Error/.test(l.textContent)).forEach(l => l.querySelector('input').click()); 1`);
        await sleep(1500);
        await b.eval(`(document.querySelector('.log-entry.level-error .log-entry-head') ?? document.querySelector('.log-entry-head'))?.click(); 1`);
        await sleep(800);
        await mask(b);
        await b.shot(OUT + 'admin-logs-problems.png');
        await b.open(admin('logs', 'settings'), 2000);
        // Both settings are the whole server's: shown on its tab.
        await b.eval(`document.querySelector('#settings-level-server').click(); 1`);
        await sleep(1200);
        await b.shot(OUT + 'admin-logs-settings.png');
        await b.close();
    },
    async installation() {
        const b = await session({ height: 900 });
        await b.open(admin('system', 'installation'), 2000);
        await b.eval(`document.querySelector('#installation-code').focus(); 1`);
        await b.type(process.env.SFE_SETUP_CODE ?? 'doc-screenshots-setup');
        await b.clickTitle('Check the setup code');
        await b.waitFor('sfe-installation-form .folder-picker li');
        await sleep(1500);
        await mask(b);
        await b.shot(OUT + 'admin-system-installation.png');
        await b.close();
    },
    async startup() {
        const b = await session({ height: 900 });
        await b.open(admin('system', 'startup'), 2000);
        // Both settings are the whole server's: shown on its tab.
        await b.eval(`document.querySelector('#settings-level-server').click(); 1`);
        await sleep(1200);
        await b.shot(OUT + 'admin-system-startup.png');
        await b.close();
    },
    async streams() {
        const b = await session({ height: 900 });
        await b.open(admin('playback', 'streams'), 2000);
        // Two conversions started from this page (leaving it would end them). The sample clips convert in a moment:
        // a slow download makes ffmpeg wait, so that they stay listed until the next refresh shows them.
        await b.send('Network.enable');
        await b.send('Network.emulateNetworkConditions', { offline: false, latency: 0, downloadThroughput: 4000, uploadThroughput: -1 });
        await b.eval(`window.held = [['Videos/Holidays/Beach walk.webm', 0], ['Videos/Timelapses/Clouds.webm', 2]].map(([path, t], i) =>
            fetch('api/streaming/play?' + new URLSearchParams({ path, t: String(t), id: 'doc' + i })).then(r => r.status)); 1`);
        await b.eval(`new Promise(resolve => setTimeout(() => resolve(1), 4500))`);
        await b.shot(OUT + 'admin-playback-streams.png');
        await b.close();
    },
};

const names = process.argv.slice(2);
for (const name of names.length ? names : Object.keys(scenes)) {
    console.log('--', name);
    try { await scenes[name](); } catch (e) { console.error(name, 'failed:', e.message); }
}
