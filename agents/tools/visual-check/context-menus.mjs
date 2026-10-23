// The app's context menu in the real page: real right clicks (CDP mouse events) on a file, a folder, a breadcrumb and
// the listing's background, the ContextMenu key, the browser's menu kept (Shift, text field), Open in new tab opening
// a tab, and the menu as a sheet at phone width. Usage: node context-menus.mjs <out prefix>
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs); serves them on :5097.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = path.join(os.tmpdir(), 'sfe-docs');
const data = path.join(work, 'data');
const out = process.argv[2] ?? path.join(os.tmpdir(), 'sfe-context-menus', 'menu');
const url = 'http://localhost:5097/';

const installation = path.join(data, 'installation-menus.json');
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

const results = [];
const check = (name, ok, detail = '') => {
    results.push({ name, ok });
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`);
};

/** Every contextmenu event reaching the page, and whether something prevented the browser's menu. */
const recorder = `window.__menus = []; document.addEventListener('contextmenu', e => setTimeout(() => __menus.push({ prevented: e.defaultPrevented, shift: e.shiftKey })), true); 1`;
const menuState = `JSON.stringify((() => {
    const menu = document.querySelector('.item-menu:not(.viewer-menu)');
    const box = menu?.getBoundingClientRect();
    return { kind: menu?.dataset.menu ?? null, entries: [...(menu?.querySelectorAll('[data-entry]') ?? [])].map(e => e.dataset.entry),
             newTab: menu?.querySelector('[data-entry="newTab"]')?.getAttribute('target') ?? null,
             left: box?.left, right: box?.right, bottom: box?.bottom, width: innerWidth, height: innerHeight, events: __menus.splice(0) };
})())`;

async function rightClick(b, selector, modifiers = 0) {
    const r = await b.rect(selector);
    const x = r.x + Math.min(r.width / 2, 20), y = r.y + r.height / 2;
    await b.mouse('mouseMoved', x, y);
    await b.mouse('mousePressed', x, y, { button: 'right', modifiers });
    await b.mouse('mouseReleased', x, y, { button: 'right', modifiers });
    await sleep(400);
    return JSON.parse(await b.eval(menuState));
}

async function signIn(b) {
    await b.goto(url + 'login', 1500);
    await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
}

async function closeMenu(b) {
    await b.key('Escape', 'Escape', 27);
    await sleep(200);
}

try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    const b = await launch({ width: 1280, height: 800 });
    await signIn(b);
    await b.eval(`localStorage.setItem('sfe.view', 'grid'); 1`);
    await b.goto(url + '?path=Photos', 4000);
    await b.waitFor('.folder-item, .tile');
    await b.eval(recorder);

    let state = await rightClick(b, '.tile .item-name');
    check('file: app menu', state.kind === 'file' && state.events[0]?.prevented === true, state.entries.join(' '));
    check('file: Open in new tab is a link to a new tab', state.newTab === '_blank');
    await b.shot(out + '-file.png');
    await closeMenu(b);

    state = await rightClick(b, '.folder-item .folder');
    check('folder: app menu', state.kind === 'folder' && state.entries[0] === 'open' && state.entries[1] === 'newTab', state.entries.join(' '));
    // Open in new tab opens a tab.
    const before = (await b.send('Target.getTargets')).targetInfos.filter(t => t.type === 'page').length;
    await b.realClick('.item-menu [data-entry="newTab"]');
    await sleep(1500);
    const after = (await b.send('Target.getTargets')).targetInfos.filter(t => t.type === 'page');
    check('folder: Open in new tab opens a tab', after.length === before + 1, after.map(t => t.url).join(' '));

    state = await rightClick(b, '.crumbs a');
    check('breadcrumb: folder link menu', state.kind === 'folderLink', state.entries.join(' '));
    await closeMenu(b);

    state = await rightClick(b, '.location-bar .summary');
    check('background: folder menu', state.kind === 'background', state.entries.join(' '));
    await b.shot(out + '-background.png');
    await closeMenu(b);

    state = await rightClick(b, '.tile .item-name', 8 /* Shift */);
    check('Shift: browser menu', state.kind === null && state.events[0]?.prevented === false);
    state = await rightClick(b, 'form.search input');
    check('text field: browser menu', state.kind === null && state.events[0]?.prevented === false);
    await b.eval(`document.activeElement.blur(); 1`);

    // The ContextMenu key on a focused tile: the app's menu, and the browser's own event that follows swallowed.
    await b.eval(`document.querySelector('.tile a.item-link').focus(); __menus.length = 0; 1`);
    await b.key('ContextMenu', 'ContextMenu', 93);
    await sleep(600);
    state = JSON.parse(await b.eval(menuState));
    check('ContextMenu key: menu of the focused file', state.kind === 'file' && state.events.every(e => e.prevented), JSON.stringify(state.events));
    await closeMenu(b);
    await b.eval(`document.querySelector('.tile a.item-link').focus(); 1`);
    await b.key('F10', 'F10', 121, 8);
    await sleep(600);
    state = JSON.parse(await b.eval(menuState));
    check('Shift+F10: menu of the focused file', state.kind === 'file' && state.events.every(e => e.prevented), JSON.stringify(state.events));
    await b.close();

    // Phone width: the background menu is a sheet across the bottom.
    const p = await launch({ width: 390, height: 700, scrollbars: true });
    await signIn(p);
    await p.goto(url + '?path=Photos', 4000);
    await p.waitFor('.folder-item, .tile');
    await p.eval(recorder);
    state = await rightClick(p, '.location-bar .summary');
    check('phone: sheet across the bottom', state.kind === 'background' && state.left === 0 && state.right === state.width && Math.abs(state.bottom - state.height) < 1,
        `${state.left}-${state.right} of ${state.width}, bottom ${state.bottom} of ${state.height}`);
    await p.shot(out + '-phone.png');
    await p.close();
} finally {
    app.kill();
}
const failed = results.filter(r => !r.ok).length;
console.log(failed ? `${failed} failed` : 'all ok');
process.exitCode = failed ? 1 : 0;
