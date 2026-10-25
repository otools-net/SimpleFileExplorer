// The viewer covers the whole screen: no page scrollbar (nor its room) beside it, at desktop and phone widths, and
// the listing behind it neither moves nor loses its scroll position. Classic scrollbars, a listing scrolled down.
// Usage: node viewer-scrollbar.mjs [<out prefix>]   (prints ok/FAIL per check; exit code 1 on a failure)
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs, or env SFE_DOCS_WORK); serves
// them on :5095.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = process.env.SFE_DOCS_WORK ?? path.join(os.tmpdir(), 'sfe-docs');
const data = path.join(work, 'data');
const out = process.argv[2];
const url = 'http://localhost:5095/';

const installation = path.join(data, 'installation-viewer-check.json');
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
let failed = false;
const check = (ok, what) => {
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${what}`);
    failed ||= !ok;
};
const measure = `JSON.stringify({
    scrollbar: window.innerWidth - document.documentElement.clientWidth,
    y: Math.round(scrollY),
    right: document.querySelector('dialog.viewer[open]')?.getBoundingClientRect().right ?? null,
    width: window.innerWidth,
    row: Math.round(document.querySelector('.row')?.getBoundingClientRect().width ?? 0),
})`;

try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    for (const [width, height] of [[1280, 360], [390, 500]]) {
        const b = await launch({ width, height, scrollbars: true });
        await b.goto(url + 'login', 1500);
        await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
        await b.eval(`localStorage.setItem('sfe.view', 'list'); 1`);
        await b.goto(url + '?path=Photos%2F2024%20Beach%20trip', 4000);
        await b.eval('window.scrollTo(0, 120); 1');
        await sleep(600);
        const before = JSON.parse(await b.eval(measure));
        await b.click('a.item-link[title^="Night swim.jpg"]');
        await sleep(1500);
        const open = JSON.parse(await b.eval(measure));
        if (out)
            await b.shot(`${out}-${width}.png`);
        await b.key('Escape', 'Escape', 27);
        await sleep(800);
        const after = JSON.parse(await b.eval(measure));
        console.log(`${width}px: before ${JSON.stringify(before)}, open ${JSON.stringify(open)}, after ${JSON.stringify(after)}`);
        check(before.scrollbar > 0 && before.y > 0, `${width}px: the listing has a scrollbar and is scrolled (else nothing is tested)`);
        check(open.scrollbar === 0, `${width}px: no page scrollbar while the viewer is open`);
        check(open.right === open.width, `${width}px: the viewer reaches the right edge`);
        check(open.row === before.row, `${width}px: the listing behind keeps its width`);
        check(after.right === null && after.y === before.y && after.scrollbar === before.scrollbar, `${width}px: closed, the scrollbar and the scroll position are back`);
        await b.close();
    }
} finally {
    app.kill();
    await sleep(500);
    fs.rmSync(installation, { force: true });
}
process.exit(failed ? 1 : 0);
