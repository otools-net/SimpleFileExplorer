// The archive viewer in the real app: a zip with a big folder and a deep chain of folders, opened on its own page at
// desktop and phone widths. Checks the rows drawn (only those on screen), more pages loaded while scrolling, the way to
// a deep folder collapsed, Go to, Up, and an entry opened from the archive; passwords: a 7z whose list is encrypted
// (asked, a wrong one refused, the right one lists it) and an AES zip (a protected file opened once the password is
// given); extracting (the archive's menu, the dialog with the password, the job, the folder written and listed);
// ok/FAIL per check (exit code 1 on a failure).
// Usage: node archive-viewer.mjs [<out prefix>]   (saves <prefix>-desktop.png, -deep.png, -password.png, -extract.png, -phone.png)
// Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs, or env SFE_DOCS_WORK); serves
// them on :5096 and writes Archives/Check.zip into that copy of the library.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';
import { writeZip } from '../docs-screenshots/make-zip.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = process.env.SFE_DOCS_WORK ?? path.join(os.tmpdir(), 'sfe-docs');
const data = path.join(work, 'data');
const out = process.argv[2];
const url = 'http://localhost:5096/';
const BIG = 3000;
const DEPTH = 300;

const photo = fs.readdirSync(path.join(work, 'library', 'Photos'), { recursive: true }).find(name => /\.jpe?g$/i.test(name));
const entries = [
    { name: 'readme.txt', data: Buffer.from('Made by agents/tools/visual-check/archive-viewer.mjs\n') },
    { name: 'Pictures/photo.jpg', data: fs.readFileSync(path.join(work, 'library', 'Photos', photo)) },
];
for (let i = 0; i < BIG; i++)
    entries.push({ name: `Big folder/entry ${String(i).padStart(5, '0')}.txt`, data: Buffer.from(`entry ${i}\n`) });
entries.push({ name: Array.from({ length: DEPTH }, (_, i) => `level ${i + 1}`).join('/') + '/bottom.txt', data: Buffer.from('the bottom\n') });
writeZip(path.join(work, 'library', 'Archives', 'Check.zip'), entries);
// Archives with a password (made by 7-Zip: tests/samples/archives; password "secret").
fs.copyFileSync(path.join(repo, 'tests/samples/archives/tree-names-encrypted.7z'), path.join(work, 'library', 'Archives', 'Hidden names.7z'));
fs.copyFileSync(path.join(repo, 'tests/samples/archives/tree-aes256.zip'), path.join(work, 'library', 'Archives', 'Secret.zip'));
// What an earlier run extracted.
fs.rmSync(path.join(work, 'library', 'Archives', 'Secret'), { recursive: true, force: true });

const installation = path.join(data, 'installation-archive-check.json');
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
const page = url + 'view?path=' + encodeURIComponent('Archives/Check.zip');
const until = async (b, expression, ms = 15000) => {
    for (let waited = 0; waited < ms; waited += 250) {
        if (await b.eval(`Boolean(${expression})`))
            return true;
        await sleep(250);
    }
    return false;
};

try {
    for (let i = 0; i < 120; i++) {
        try { await fetch(url + 'api/migration'); break; } catch { await sleep(500); }
    }
    for (const [name, width, height] of [['desktop', 1280, 800], ['phone', 390, 844]]) {
        const b = await launch({ width, height, mobile: width < 600 });
        await b.goto(url + 'login', 1500);
        await b.eval(`fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`);
        await b.goto(page, 1500);
        check(await until(b, `document.querySelectorAll('.archive-body .archive-row:not(.placeholder)').length >= 3`), `${name}: the top of the archive listed`);
        const top = JSON.parse(await b.eval(`JSON.stringify([...document.querySelectorAll('.archive-body .archive-open')].map(e => e.textContent.trim()))`));
        check(JSON.stringify(top) === JSON.stringify(['Big folder', 'level 1', 'Pictures', 'readme.txt']), `${name}: folders first, then files: ${top.join(', ')}`);
        const wide = await b.eval(`document.documentElement.scrollWidth <= window.innerWidth`);
        check(wide, `${name}: the page is no wider than the screen`);
        if (out)
            await b.shot(`${out}-${name}.png`);

        if (name === 'desktop') {
            await b.eval(`[...document.querySelectorAll('.archive-open')].find(e => e.textContent.trim() === 'Big folder').click(); 1`);
            check(await until(b, `document.querySelector('[data-counts]')?.textContent.includes('${BIG.toLocaleString('en')}')`), 'the big folder: its count before its rows');
            const drawn = await b.eval(`document.querySelectorAll('.archive-body .archive-row').length`);
            check(drawn > 0 && drawn < 200, `only the rows on screen are drawn (${drawn} of ${BIG})`);
            const height = await b.eval(`parseInt(document.querySelector('.archive-body').style.height)`);
            check(height === BIG * 32, `the scroll height is the whole folder's (${height} px)`);
            await b.eval(`const s = document.querySelector('.archive-scroller'); s.scrollTop = s.scrollHeight; 1`);
            check(await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'entry ${String(BIG - 1).padStart(5, '0')}.txt')`, 20000),
                'scrolled to the end: the last rows loaded page by page');

            const deepPath = Array.from({ length: DEPTH }, (_, i) => `level ${i + 1}`).join('/');
            await b.eval(`{ const box = document.querySelector('[data-go]'); box.value = ${JSON.stringify(deepPath)}; box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); } 1`);
            check(await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'bottom.txt')`), `Go to: ${DEPTH} folders down`);
            const skipped = await b.eval(`document.querySelector('[data-skipped]')?.getAttribute('title') ?? ''`);
            check(skipped.startsWith(`${DEPTH - 4}`), `the way there collapsed: "${skipped}"`);
            if (out)
                await b.shot(`${out}-deep.png`);
            await b.eval(`document.querySelector('[data-up]').click(); 1`);
            check(await until(b, `document.querySelector('.archive-crumb.current')?.textContent.trim() === 'level ${DEPTH - 1}'`), 'Up: the folder above');

            await b.goto(page, 1500);
            await until(b, `document.querySelector('.archive-open')`);
            const readme = await b.eval(`[...document.querySelectorAll('a.archive-open')].find(e => e.textContent.trim() === 'readme.txt')?.getAttribute('href') ?? ''`);
            const text = await b.eval(`fetch(${JSON.stringify(readme)}).then(r => r.text())`);
            check(text.includes('archive-viewer.mjs'), 'an entry opened from the archive');

            const submit = password => b.eval(`{ const box = document.querySelector('[data-password-box]'); box.value = ${JSON.stringify(password)};
                document.querySelector('[data-password]').requestSubmit(); } 1`);
            await b.goto(url + 'view?path=' + encodeURIComponent('Archives/Hidden names.7z'), 1500);
            check(await until(b, `document.querySelector('[data-password-box]')`), 'a list encrypted: its password asked');
            await submit('wrong');
            check(await until(b, `document.querySelector('[data-password-problem]')?.textContent.includes("doesn't open")`), 'a wrong password: said so');
            if (out)
                await b.shot(`${out}-password.png`);
            await submit('secret');
            check(await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'readme.txt')`), 'the right password: the list shown');
            const hidden = await b.eval(`fetch([...document.querySelectorAll('a.archive-open')].find(e => e.textContent.trim() === 'readme.txt').getAttribute('href')).then(r => r.text())`);
            check(hidden.includes('An archive made for the tests'), 'a file of the hidden list opened');

            await b.goto(url + 'view?path=' + encodeURIComponent('Archives/Secret.zip'), 1500);
            check(await until(b, `document.querySelector('[data-protected]')`), 'a zip with protected files: said so, its names listed');
            await b.eval(`[...document.querySelectorAll('button.archive-open')].find(e => e.textContent.trim() === 'readme.txt').click(); 1`);
            check(await until(b, `document.querySelector('[data-password]')?.textContent.includes('names stay readable')`), "a protected file: its password asked, with what a zip's protects");
            await submit('secret');
            check(await until(b, `[...document.querySelectorAll('a.archive-open')].some(e => e.textContent.trim() === 'readme.txt')`), 'the right password: the file opens');
            const secret = await b.eval(`fetch([...document.querySelectorAll('a.archive-open')].find(e => e.textContent.trim() === 'readme.txt').getAttribute('href')).then(r => r.text())`);
            check(secret.includes('An archive made for the tests'), 'the AES file decrypted');

            // Extracting: the archive's menu in the listing, the dialog with the password, the job in the panel.
            await b.goto(url + '?path=Archives', 1500);
            await until(b, `document.querySelector('[data-path="Archives/Secret.zip"]')`);
            await b.eval(`document.querySelector('[data-path="Archives/Secret.zip"]').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 300 })); 1`);
            check(await until(b, `document.querySelector('[data-entry="extractBeside"]')?.textContent.includes('Extract to “Secret”')`), "the archive's menu: Extract to “Secret”");
            await b.eval(`document.querySelector('[data-entry="extract"]').click(); 1`);
            check(await until(b, `document.querySelector('[data-extract-password]') && document.querySelector('[data-extract]') && !document.querySelector('[data-extract]').disabled`), "Extract…: the dialog, ready in the archive's folder");
            await b.eval(`{ const box = document.querySelector('[data-extract-password]'); box.value = 'secret'; box.dispatchEvent(new Event('input')); } 1`);
            if (out)
                await b.shot(`${out}-extract.png`);
            await b.eval(`document.querySelector('[data-extract]').click(); 1`);
            const extracted = path.join(work, 'library', 'Archives', 'Secret', 'Deep', 'a', 'b', 'c', 'd', 'e', 'bottom.txt');
            check(await until(b, `[...document.querySelectorAll('.operation-title')].some(e => e.textContent.includes('extracted to'))`), 'the panel: extracted');
            check(fs.existsSync(extracted), "the archive's files written into Archives/Secret");
            check(await until(b, `[...document.querySelectorAll('[data-path]')].some(e => e.getAttribute('data-path') === 'Archives/Secret')`), 'the new folder listed');
        }
        await b.close();
    }
} finally {
    app.kill();
    fs.rmSync(installation, { force: true });
}
process.exit(failed ? 1 : 0);
