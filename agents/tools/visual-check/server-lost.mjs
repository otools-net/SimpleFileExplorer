// The ⋮ button's warning while the server doesn't answer: the page open, the server stopped (the yellow ! and the
// "Server not answering" line show after about 10 s), then started again (they go away). Usage:
// node server-lost.mjs [<screenshot path>] (default <tmp>/sfe-server-lost.png: the button and its open menu).
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
const shot = process.argv[2] ?? path.join(os.tmpdir(), 'sfe-server-lost.png');
const url = 'http://localhost:5097/';

const installation = path.join(data, 'installation-server-lost.json');
fs.writeFileSync(installation, JSON.stringify({ Urls: url.replace(/\/$/, ''), Sfe: { Root: path.join(work, 'library') } }));

function start() {
    return spawn('dotnet', [path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll'), '--contentRoot', path.join(repo, 'src/Sfe.Api'), '--no-browser', '--no-logo'], {
        cwd: work, stdio: 'ignore',
        env: {
            ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
            Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
            Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
            Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
        },
    });
}

async function ready() {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); return; } catch { await sleep(500); }
    }
}

const results = [];
const check = (name, ok, detail = '') => {
    results.push({ name, ok });
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`);
};
const state = `JSON.stringify((() => {
    const button = document.querySelector('.menu > .icon-button');
    return { label: button?.getAttribute('aria-label'), mark: button?.querySelector('.menu-keep-alive')?.textContent ?? null,
             line: document.querySelector('.menu-server-lost')?.textContent.trim() ?? null };
})())`;

let app = start();
try {
    await ready();
    const b = await launch({ width: 1280, height: 800 });
    await b.goto(url + 'login', 1500);
    await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
    await b.goto(url + '?path=Photos', 4000);
    let s = JSON.parse(await b.eval(state));
    check('server up: no warning', s.label === 'More' && s.mark !== '!', JSON.stringify(s));

    app.kill();
    await sleep(4000);
    s = JSON.parse(await b.eval(state));
    check('a short drop: no warning yet', s.mark !== '!', JSON.stringify(s));
    await sleep(10000);
    s = JSON.parse(await b.eval(state));
    check('server stopped: the yellow ! on the button', s.mark === '!' && s.label === 'More (server not answering)', JSON.stringify(s));
    await b.click('.menu > .icon-button');
    await sleep(400);
    s = JSON.parse(await b.eval(state));
    check('and the line in the menu', s.line?.endsWith('Server not answering'), JSON.stringify(s));
    const menu = await b.rect('#menu');
    fs.mkdirSync(path.dirname(shot), { recursive: true });
    await b.shot(shot, { x: menu.x - 20, y: 0, width: 1280 - menu.x + 20, height: Math.min(menu.y + 112, 800) });
    await b.key('Escape', 'Escape', 27);

    app = start();
    await ready();
    await sleep(8000);
    s = JSON.parse(await b.eval(state));
    check('server back: the warning goes', s.mark !== '!' && s.label === 'More', JSON.stringify(s));
    await b.close();
} finally {
    app.kill();
}
console.log(results.every(r => r.ok) ? 'all ok' : `${results.filter(r => !r.ok).length} failed`);
process.exitCode = results.every(r => r.ok) ? 0 : 1;
