// Audit of every screen on narrow screens (agents/docs/features/overflow-menus.md): at the phone sizes (360×800,
// 390×844, 414×896 portrait, 800×360 landscape) each scene opens a screen and lists
// what looks broken: an element sticking out of the screen, a button cut off by a box that doesn't scroll, a row of
// three or more buttons on more than one line, the page wider than the screen. Prints the problems per scene and
// size, then a summary per scene (problems seen at several sizes once); saves `<prefix>-<scene>-<size>.png`.
// Usage: node narrow-audit.mjs <out prefix> [scene...]   (all scenes by default; the list: node narrow-audit.mjs - list)
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs); serves them on :5094.
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
const url = 'http://localhost:5094/';
const sizes = [[360, 800], [390, 844], [414, 896], [800, 360]];
const enc = value => encodeURIComponent(value);
const listing = (folder, view = 'grid') => async b => {
    await b.eval(`localStorage.setItem('sfe.view', '${view}'); 1`);
    await b.goto(url + '?path=' + enc(folder), 3500);
};
const viewer = (folder, title) => async b => {
    await listing(folder)(b);
    await b.eval(`document.querySelector('a.item-link[title^=${JSON.stringify(title)}]').click(); 1`);
    await sleep(3000);
};
const page = (address, wait = 3500) => async b => b.goto(url + address, wait);
const click = async (b, selector, wait = 800) => {
    await b.eval(`document.querySelector(${JSON.stringify(selector)})?.click(); 1`);
    await sleep(wait);
};

/** Each scene opens its screen in `b` (signed in as the administrator). */
const scenes = {
    'listing-grid': listing('Photos/2024 Beach trip'),
    'listing-list': listing('Photos/2024 Beach trip', 'list'),
    'listing-rows': listing('Photos/2024 Beach trip', 'rows'),
    'listing-folders': listing('Photos'),
    'documents-list': listing('Documents', 'list'),
    search: page('?q=' + enc('beach')),
    'main-menu': async b => { await listing('Photos')(b); await click(b, '.topbar button[aria-label="Main menu"], .topbar button[title^="Main menu"]'); },
    'item-menu': async b => { await listing('Photos/2024 Beach trip', 'list')(b); await click(b, '.row .item-menu-button:not([hidden])'); },
    'folder-tags': async b => { await listing('Photos/2024 Beach trip')(b); await click(b, 'button.scope-tags-button', 1500); },
    properties: async b => { await listing('Photos/2024 Beach trip', 'list')(b); await click(b, '.row .item-menu-button:not([hidden])'); await click(b, '.item-menu [data-entry="properties"]', 1500); },
    'viewer-picture': viewer('Photos/2025 Mountains', 'Summit'),
    'viewer-video': viewer('Videos/Holidays', 'Mountain drive'),
    'viewer-text': viewer('Documents', 'Photo backup.log'),
    'text-editor': async b => { await viewer('Documents', 'Trip diary.txt')(b); await click(b, '.viewer [data-edit]', 1500); },
    'viewer-markdown': viewer('Documents', 'Hiking guide.md'),
    'viewer-json': viewer('Documents', 'Trip plan.json'),
    'viewer-csv': viewer('Documents', 'Huts.csv'),
    'viewer-subtitles': viewer('Videos/Holidays', 'Campfire.en.srt'),
    'viewer-pdf': viewer('Documents', 'Hut handbook.pdf'),
    'viewer-epub': viewer('Documents', 'Hut stories.epub'),
    'picture-editor': async b => {
        await viewer('Photos/2025 Mountains', 'Summit')(b);
        await click(b, '.viewer button[aria-label="More actions"]', 400);
        await click(b, '.item-menu [data-entry="crop"]', 2500);
    },
    'player-page': page('player?path=' + enc('Videos/Holidays/Mountain drive.webm')),
    'view-page': page('view?path=' + enc('Documents/Hiking guide.md')),
    'admin-overview': page('admin'),
    'admin-logs': page('admin?section=logs'),
    'admin-tasks': page('admin?section=tasks&page=tasks'),
    'admin-accounts': page('admin?section=access&page=accounts'),
    'admin-tags': page('admin?section=tags&page=list'),
    'admin-duplicates': page('admin?section=tracking&page=duplicates'),
    // The last chip of a section: its line of chips scrolled to show it (phones).
    'admin-tracking-settings': page('admin?section=tracking&page=settings'),
    'admin-settings': page('admin?section=interface&page=appearance'),
    'admin-keep-alive': page('admin?section=keepAlive&page=status'),
    // Every other administration page (as the docs screenshots them).
    'admin-interface-browsing': page('admin?section=interface&page=browsing'),
    'admin-interface-search': page('admin?section=interface&page=search'),
    'admin-playback-player': page('admin?section=playback&page=player'),
    'admin-playback-streaming': page('admin?section=playback&page=streaming'),
    'admin-scanning-skipped': page('admin?section=scanning&page=skipped'),
    'admin-scanning-fileTypes': page('admin?section=scanning&page=fileTypes'),
    'admin-scanning-indexing': page('admin?section=scanning&page=indexing'),
    'admin-tags-settings': page('admin?section=tags&page=settings'),
    'admin-tracking-similar': page('admin?section=tracking&page=similar'),
    'admin-tracking-missing': page('admin?section=tracking&page=missing'),
    'admin-keepAlive-settings': page('admin?section=keepAlive&page=settings'),
    'admin-thumbnails': page('admin?section=thumbnails'),
    'admin-trickplay-activity': page('admin?section=trickplay&page=activity'),
    'admin-trickplay-settings': page('admin?section=trickplay&page=settings'),
    'admin-editor': page('admin?section=editor'),
    'admin-access-spaces': page('admin?section=access&page=spaces'),
    'admin-tasks-settings': page('admin?section=tasks&page=settings'),
    'admin-system': page('admin?section=system'),
    'admin-system-maintenance': page('admin?section=system&page=maintenance'),
    'admin-account-preferences': page('admin?section=account&page=preferences'),
    'admin-account-password': page('admin?section=account&page=password'),
    'admin-logs': page('admin?section=logs'),
    'admin-files-trash': page('admin?section=files&page=trash'),
    'admin-files-uploads': page('admin?section=files&page=uploads'),
    'admin-files-settings': page('admin?section=files&page=files'),
};

/** The problems on the page now (in the page; returns JSON). */
const audit = `JSON.stringify((() => {
    const problems = [];
    const width = innerWidth;
    const name = element => {
        const label = element.getAttribute('aria-label') || element.getAttribute('title') || (element.children.length ? '' : element.textContent.trim());
        const classes = [...element.classList].slice(0, 2).map(c => '.' + c).join('');
        // Two words of the label: the same button of every file ("Tags of Dunes.jpg") is one problem.
        return element.localName + classes + (label ? ' "' + label.split(' ').slice(0, 2).join(' ') + '"' : '');
    };
    const where = element => {
        const parts = [];
        for (let node = element; node && parts.length < 3 && node !== document.body; node = node.parentElement)
            if (node.classList.length || node.localName.startsWith('sfe-') || node.localName === 'dialog')
                parts.unshift(node.localName + [...node.classList].slice(0, 1).map(c => '.' + c).join(''));
        return parts.join(' > ');
    };
    // Behind a modal dialog (the viewer, a sheet) the page is inert: only the dialog and the menus over it count.
    const modal = document.querySelector('dialog:modal');
    const shown = element => {
        if (element.closest('[hidden], [inert], .visually-hidden'))
            return false;
        if (modal && !modal.contains(element) && !element.closest('[popover]'))
            return false;
        const style = getComputedStyle(element);
        if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0)
            return false;
        const box = element.getBoundingClientRect();
        return box.width > 1 && box.height > 1;
    };
    /** The nearest box that clips it sideways: [box, scrolls]. */
    const clipper = element => {
        for (let node = element.parentElement; node && node !== document.documentElement; node = node.parentElement) {
            const x = getComputedStyle(node).overflowX;
            if (x !== 'visible')
                return [node, x === 'auto' || x === 'scroll'];
        }
        return [null, false];
    };
    if (document.documentElement.scrollWidth > width + 1)
        problems.push('page wider than the screen: ' + document.documentElement.scrollWidth + ' px');
    const controls = [...document.querySelectorAll('button, a[href], input:not([type=hidden]), select, textarea, [role=button], label, h1, h2, h3, strong, .chip, sfe-tag-chip')].filter(shown);
    for (const element of controls) {
        const box = element.getBoundingClientRect();
        const [box2, scrolls] = clipper(element);
        if (box2 && !scrolls) {
            const clip = box2.getBoundingClientRect();
            if (box.right > clip.right + 1 || box.left < clip.left - 1)
                problems.push('cut off: ' + name(element) + ' in ' + where(element));
            else if (box.right > width + 1 || box.left < -1)
                problems.push('off screen: ' + name(element) + ' in ' + where(element));
        } else if (!box2 && (box.right > width + 1 || box.left < -1))
            problems.push('off screen: ' + name(element) + ' in ' + where(element));
    }
    // Buttons drawn over text that isn't theirs (the text's own boxes, not its element's: a line of text and a button
    // beside it may share a box).
    const texts = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, { acceptNode: text => text.textContent.trim() ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT });
    for (let text = walker.nextNode(); text; text = walker.nextNode()) {
        const owner = text.parentElement;
        if (!owner || !shown(owner) || owner.closest('button, select, option, [role=button], [popover], dialog:not([open])'))
            continue;
        const range = document.createRange();
        range.selectNodeContents(text);
        // Only the part of the text its scrolling boxes show (a dialog's body scrolls under its footer).
        const clips = [];
        for (let node = owner.parentElement; node && node !== document.documentElement; node = node.parentElement)
            if (getComputedStyle(node).overflowY !== 'visible' || getComputedStyle(node).overflowX !== 'visible')
                clips.push(node.getBoundingClientRect());
        for (const whole of range.getClientRects()) {
            const rect = clips.reduce((r, c) => ({ left: Math.max(r.left, c.left), right: Math.min(r.right, c.right), top: Math.max(r.top, c.top), bottom: Math.min(r.bottom, c.bottom) }),
                { left: whole.left, right: whole.right, top: whole.top, bottom: whole.bottom });
            if (rect.right - rect.left > 2 && rect.bottom - rect.top > 2)
                texts.push([owner, rect]);
        }
    }
    const overlaps = new Set();
    /** Over the page by design: menus and popovers, bars fixed or sticky (a save bar), the viewer's arrows. */
    const overlay = element => {
        if (element.closest('[popover], .viewer-nav'))
            return true;
        for (let node = element; node && node !== document.body; node = node.parentElement)
            if (['fixed', 'sticky'].includes(getComputedStyle(node).position))
                return true;
        return false;
    };
    for (const button of controls.filter(element => (element.localName === 'button' || element.getAttribute('role') === 'button') && !overlay(element))) {
        const box = button.getBoundingClientRect();
        for (const [owner, rect] of texts) {
            // A text in a bar over the page (the save bar) is over the button, not under it.
            if (button.contains(owner) || owner.contains(button) || overlay(owner))
                continue;
            const x = Math.min(box.right, rect.right) - Math.max(box.left, rect.left);
            const y = Math.min(box.bottom, rect.bottom) - Math.max(box.top, rect.top);
            if (x > 3 && y > 3)
                overlaps.add('over text: ' + name(button) + ' covers "' + owner.textContent.trim().slice(0, 30) + '" in ' + where(button));
        }
    }
    problems.push(...[...overlaps].slice(0, 10));
    // Rows of buttons on more than one line.
    const rows = new Set(controls.filter(element => element.localName === 'button').map(button => button.parentElement));
    for (const row of rows) {
        const style = getComputedStyle(row);
        if (!style.display.includes('flex') || style.flexDirection.startsWith('column'))
            continue;
        const buttons = [...row.children].filter(child => (child.localName === 'button' || child.querySelector(':scope > button')) && shown(child));
        if (buttons.length < 3)
            continue;
        const lines = new Set(buttons.map(button => { const b = button.getBoundingClientRect(); return Math.round((b.top + b.height / 2) / 12); }));
        if (lines.size > 1)
            problems.push('buttons on ' + lines.size + ' lines: ' + where(row) + ' (' + buttons.length + ' buttons)');
    }
    return [...new Set(problems)];
})())`;

if (process.argv[3] === 'list' || !out) {
    console.log(Object.keys(scenes).join('\n'));
    process.exit(out ? 0 : 2);
}
const chosen = process.argv.slice(3).length ? process.argv.slice(3) : Object.keys(scenes);
for (const name of chosen)
    if (!scenes[name])
        throw new Error(`No scene ${name}: node narrow-audit.mjs - list`);

const installation = path.join(data, 'installation-audit.json');
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
/** scene → problem → sizes. */
const found = new Map();
try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    for (const [width, height] of sizes) {
        const size = `${width}x${height}`;
        const b = await launch({ width, height, mobile: true, scrollbars: true });
        await b.goto(url + 'login', 1500);
        await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
        for (const name of chosen) {
            try {
                await scenes[name](b);
                const problems = JSON.parse(await b.eval(audit));
                console.log(`${problems.length ? 'FOUND' : 'ok   '} ${name} ${size}${problems.map(problem => '\n      ' + problem).join('')}`);
                for (const problem of problems) {
                    const byScene = found.get(name) ?? new Map();
                    found.set(name, byScene.set(problem, [...byScene.get(problem) ?? [], size]));
                }
                await b.shot(`${out}-${name}-${size}.png`);
            } catch (error) {
                console.log(`ERROR ${name} ${size}: ${error.message}`);
            }
            await b.eval(`localStorage.removeItem('sfe.view'); document.fullscreenElement && document.exitFullscreen(); 1`).catch(() => {});
        }
        await b.close();
    }
} finally {
    app.kill();
}
console.log('\n== summary');
for (const [name, problems] of found) {
    console.log(name);
    for (const [problem, at] of problems)
        console.log(`  ${problem}  [${at.join(', ')}]`);
}
if (!found.size)
    console.log('nothing found');
