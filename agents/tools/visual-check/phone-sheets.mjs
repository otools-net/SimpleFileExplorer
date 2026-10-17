// Phone width (390 px) with classic scrollbars, list view: the item menu sheet and a dialog sheet (Properties) must
// span the whole width without moving the page behind. Usage: node phone-sheets.mjs <out prefix>
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs); serves them on :5096.
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
const url = 'http://localhost:5096/';

const installation = path.join(data, 'installation-check.json');
fs.writeFileSync(installation, JSON.stringify({ Urls: url.replace(/\/$/, ''), Sfe: { Root: path.join(work, 'library') } }));
const app = spawn('dotnet', [path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll'), '--contentRoot', path.join(repo, 'src/Sfe.Api')], {
    cwd: work, stdio: 'ignore',
    env: {
        ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
        Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
        Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
        Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
    },
});
try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    const b = await launch({ width: 390, height: 700, scrollbars: true });
    await b.goto(url + 'login', 1500);
    await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
    await b.eval(`localStorage.setItem('sfe.view', 'list'); 1`);
    await b.goto(url + '?path=Photos%2F2024%20Beach%20trip', 4000);
    await b.eval('window.scrollTo(0, 300); 1');
    await sleep(800);
    const before = JSON.parse(await b.eval(`JSON.stringify({ scrollbar: window.innerWidth - document.documentElement.clientWidth, y: scrollY, row: document.querySelector('.row').getBoundingClientRect().width, var: getComputedStyle(document.documentElement).getPropertyValue('--scrollbar-w') })`));
    await b.eval(`document.querySelector('.row .item-menu-button:not([hidden])').click(); 1`);
    await sleep(800);
    const after = JSON.parse(await b.eval(`JSON.stringify((() => {
        const menu = document.querySelector('.item-menu')?.getBoundingClientRect();
        const backdrop = document.querySelector('.item-menu-backdrop')?.getBoundingClientRect();
        return { row: document.querySelector('.row').getBoundingClientRect().width, open: Boolean(menu), menuLeft: menu?.left, menuRight: menu?.right, backdropRight: backdrop?.right, width: window.innerWidth, y: scrollY };
    })())`));
    console.log(JSON.stringify({ before, after }));
    await b.shot(out + '-menu.png');
    // A dialog sheet: Properties from the menu.
    await b.eval(`document.querySelector('.item-menu [data-entry="properties"]').click(); 1`);
    await sleep(1500);
    const dialog = JSON.parse(await b.eval(`JSON.stringify((() => {
        const box = document.querySelector('dialog.sheet[open]')?.getBoundingClientRect();
        return { open: Boolean(box), left: box?.left, right: box?.right, bottom: box?.bottom, width: window.innerWidth, height: window.innerHeight,
                 scrollbar: window.innerWidth - document.documentElement.clientWidth };
    })())`));
    console.log(JSON.stringify({ dialog }));
    await b.shot(out + '-dialog.png');
    await b.eval(`document.querySelector('dialog.sheet[open]').close(); 1`);
    await sleep(500);
    console.log(JSON.stringify({ closed: JSON.parse(await b.eval(`JSON.stringify({ scrollbar: window.innerWidth - document.documentElement.clientWidth, y: scrollY })`)) }));
    await b.close();
} finally {
    app.kill();
}
