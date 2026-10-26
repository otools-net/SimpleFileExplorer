// The archive viewer in the real app: a zip with a big folder and a deep chain of folders, opened on its own page at
// desktop and phone widths. Checks the rows drawn (only those on screen), more pages loaded while scrolling, the way to
// a deep folder collapsed, Go to, Up, Find (a name among thousands, a result's folder opened, in this folder only), an entry opened from the
// archive, entries shown in place (text, Markdown, PDF, a picture), Open as archive (a book); passwords: a 7z whose list is encrypted
// (asked, a wrong one refused, the right one lists it) and an AES zip (a protected file opened once the password is
// given); extracting (the archive's menu, the dialog with the password, the job, the folder written and listed);
// compressing that folder again (its menu, the dialog with a password, the zip made, tested by 7-Zip when installed);
// ok/FAIL per check (exit code 1 on a failure).
// Usage: node archive-viewer.mjs [<out prefix>]   (saves <prefix>-desktop.png, -deep.png, -find.png, -entry-text.png, -entry-pdf.png, -as-archive.png, -password.png, -extract.png, -compress.png, -phone.png)
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
    { name: 'Notes/notes.md', data: Buffer.from('# Trip notes\n\nA [link to the photo](../Pictures/photo.jpg), and the photo itself:\n\n![The photo](../Pictures/photo.jpg)\n') },
    { name: 'Notes/handbook.pdf', data: fs.readFileSync(path.join(repo, 'tests/samples/library/Documents/Hut handbook.pdf')) },
    // Stored (writeZip stores everything): served as a slice of the zip, with Range.
    { name: 'Pictures/campfire.webm', data: fs.readFileSync(path.join(work, 'library', 'Videos', 'Holidays', 'Campfire.webm')) },
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
fs.rmSync(path.join(work, 'library', 'Archives', 'Secret again.zip'), { force: true });

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
        check(JSON.stringify(top) === JSON.stringify(['Big folder', 'level 1', 'Notes', 'Pictures', 'readme.txt']), `${name}: folders first, then files: ${top.join(', ')}`);
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

            // Find: names of the whole archive, each with its folder; the folder of one opened from it.
            const find = text => b.eval(`{ const box = document.querySelector('[data-find]'); box.value = ${JSON.stringify(text)}; box.dispatchEvent(new Event('input')); } 1`);
            await find('entry 02999');
            check(await until(b, `[...document.querySelectorAll('.archive-open')].map(e => e.textContent.trim()).join() === 'entry 02999.txt'
                && document.querySelector('[data-where]')?.textContent.trim() === 'in Big folder'`), 'Find: one name among 3 000, with its folder');
            await find('bottom');
            check(await until(b, `document.querySelector('[data-where]')?.textContent.trim() === 'in …/level ${DEPTH - 1}/level ${DEPTH}'`), `Find: a file ${DEPTH} folders down, the end of its way shown`);
            const nameShown = await b.eval(`(() => { const n = document.querySelector('.archive-body .archive-open'); return n.scrollWidth <= n.clientWidth; })()`);
            check(nameShown, 'Find: the name shown whole, its folder giving way');
            if (out)
                await b.shot(`${out}-find.png`);
            await b.eval(`document.querySelector('[data-where]').click(); 1`);
            check(await until(b, `document.querySelector('.archive-crumb.current')?.textContent.trim() === 'level ${DEPTH}' && !document.querySelector('[data-found]')`),
                "Find: a result's folder opened, the search over");
            // Find in this folder: only what is right inside the folder shown.
            await b.eval(`document.querySelector('[data-find-here]').click(); 1`);
            await find('entry');
            check(await until(b, `document.querySelector('[data-found-none]')?.textContent.includes('in this folder')`), 'Find in this folder: the big folder\'s names not found here');
            await find('bottom');
            check(await until(b, `[...document.querySelectorAll('.archive-open')].map(e => e.textContent.trim()).join() === 'bottom.txt' && !document.querySelector('[data-where]')`),
                'Find in this folder: its file, without a folder shown');
            await b.eval(`document.querySelector('[data-find-here]').click(); 1`);
            check(await until(b, `!!document.querySelector('[data-where]')`), 'Find in archive again: the folder of each shown');
            await b.eval(`document.querySelector('[data-find]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); 1`);

            await b.goto(page, 1500);
            await until(b, `document.querySelector('.archive-open')`);
            const readme = await b.eval(`[...document.querySelectorAll('a.archive-open')].find(e => e.textContent.trim() === 'readme.txt')?.getAttribute('href') ?? ''`);
            const text = await b.eval(`fetch(${JSON.stringify(readme)}).then(r => r.text())`);
            check(text.includes('archive-viewer.mjs'), 'an entry opened from the archive');

            // Files shown in place: text, Markdown, PDF, a picture; Back to the list as it was.
            const openName = name => b.eval(`[...document.querySelectorAll('.archive-body .archive-open')].find(e => e.textContent.trim() === ${JSON.stringify(name)}).click(); 1`);
            await openName('readme.txt');
            check(await until(b, `document.querySelector('[data-entry] .cm-content')?.textContent.includes('archive-viewer.mjs')`), 'in place: a text file read from the archive');
            check(await b.eval(`!document.querySelector('[data-entry] [data-follow]')`), 'in place: no Follow for an entry');
            if (out)
                await b.shot(`${out}-entry-text.png`);
            await b.eval(`document.querySelector('[data-back]').click(); 1`);
            check(await until(b, `!document.querySelector('[data-entry]') && document.querySelector('.archive-row.selected')?.textContent.includes('readme.txt')`), 'Back: the list, the file selected');
            await openName('Notes');
            await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'notes.md')`);
            await openName('notes.md');
            check(await until(b, `[...document.querySelectorAll('[data-entry] h1')].some(h => h.textContent.includes('Trip notes'))`), 'in place: Markdown rendered');
            check(await b.eval(`[...document.querySelectorAll('[data-entry] a[href]')].some(a => a.textContent.includes('link to the photo') && a.getAttribute('href').includes('/at/Pictures/photo.jpg'))`), 'in place: a link of a document leads to the file of the archive');
            check(await until(b, `document.querySelector('[data-entry] .markdown-content img')?.naturalWidth > 0`), 'in place: a picture of a document, from the archive');
            await b.eval(`document.querySelector('[data-previous]').click(); 1`);
            check(await until(b, `!!document.querySelector('[data-entry] canvas')`, 20000), 'The file before: the PDF drawn');
            if (out)
                await b.shot(`${out}-entry-pdf.png`);
            await b.eval(`document.querySelector('[data-entry]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); 1`);
            check(await until(b, `!document.querySelector('[data-entry]')`), 'Esc: back to the list');
            await b.eval(`document.querySelector('[data-crumb-root]').click(); 1`);
            await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'Pictures')`);
            await openName('Pictures');
            await until(b, `[...document.querySelectorAll('.archive-open')].some(e => e.textContent.trim() === 'photo.jpg')`);
            await openName('photo.jpg');
            check(await until(b, `document.querySelector('[data-entry-picture]')?.naturalWidth > 0`), 'in place: a picture of the archive');
            await b.eval(`document.querySelector('[data-previous]').click(); 1`);
            check(await until(b, `document.querySelector('[data-entry-video] video')?.readyState >= 1`), 'in place: a video of the archive, the one before');
            const seek = await b.eval(`(async () => { const v = document.querySelector('[data-entry-video] video'); v.muted = true;
                const end = v.duration; v.currentTime = end / 2; await new Promise(r => setTimeout(r, 1500));
                return JSON.stringify({ end, at: v.currentTime, seekable: v.seekable.length ? v.seekable.end(0) : 0 }); })()`);
            const sought = JSON.parse(seek);
            check(sought.seekable > 0 && sought.at >= sought.end / 2 - 0.5 && sought.at < sought.end / 2 + 3, `in place: the video seeks (Range on the stored zip entry): ${seek}`);
            await b.eval(`document.querySelector('[data-back]').click(); 1`);

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

            // Compressing it again, with a password: the folder's menu, the dialog, the zip made beside it.
            await b.eval(`document.querySelector('[data-path="Archives/Secret"]').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 300 })); 1`);
            check(await until(b, `document.querySelector('[data-entry="compress"]')`), "the folder's menu: Compress…");
            await b.eval(`document.querySelector('[data-entry="compress"]').click(); 1`);
            check(await until(b, `document.querySelector('[data-zip-name]')?.value === 'Secret'`), 'Compress…: the dialog, named after the folder');
            await b.eval(`{ const name = document.querySelector('[data-zip-name]'); name.value = 'Secret again'; name.dispatchEvent(new Event('input'));
                const level = document.querySelector('[data-level]'); level.value = '9'; level.dispatchEvent(new Event('change'));
                const box = document.querySelector('[data-zip-password]'); box.value = 'again-secret'; box.dispatchEvent(new Event('input')); } 1`);
            await until(b, `document.querySelector('[data-zip-confirm]')`);
            await b.eval(`{ const box = document.querySelector('[data-zip-confirm]'); box.value = 'again-secret'; box.dispatchEvent(new Event('input')); } 1`);
            check(await until(b, `document.querySelector('[data-password-notes]')?.textContent.includes('File names stay visible') && !document.querySelector('[data-compress]').disabled`),
                'the password typed twice, with what it protects');
            if (out)
                await b.shot(`${out}-compress.png`);
            await b.eval(`document.querySelector('[data-compress]').click(); 1`);
            check(await until(b, `[...document.querySelectorAll('.operation-title')].some(e => e.textContent.includes('“Secret again.zip” made in'))`), 'the panel: the zip made');
            const zip = path.join(work, 'library', 'Archives', 'Secret again.zip');
            check(fs.existsSync(zip), 'Archives/Secret again.zip written');
            const sevenZip = process.env.SEVENZIP ?? (fs.existsSync('C:/Program Files/7-Zip/7z.exe') ? 'C:/Program Files/7-Zip/7z.exe' : null);
            if (sevenZip && fs.existsSync(zip)) {
                const { spawnSync } = await import('node:child_process');
                const tested = spawnSync(sevenZip, ['t', '-pagain-secret', zip], { encoding: 'utf8' });
                check(tested.status === 0 && tested.stdout.includes('Everything is Ok'), "7-Zip opens it with the password");
            }

            // Open as archive: a book (a zip underneath) explored rather than read.
            await b.goto(url + '?path=Documents', 1500);
            await until(b, `document.querySelector('[data-path="Documents/Hut stories.epub"]')`);
            await b.eval(`document.querySelector('[data-path="Documents/Hut stories.epub"]').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 300 })); 1`);
            check(await until(b, `!!document.querySelector('[data-entry="openAsArchive"]')`), "a book's menu: Open as archive");
            await b.eval(`document.querySelector('[data-entry="openAsArchive"]').click(); 1`);
            check(await until(b, `[...document.querySelectorAll('.archive-body .archive-open')].some(e => e.textContent.trim() === 'mimetype')`), 'Open as archive: the files of the book');
            if (out)
                await b.shot(`${out}-as-archive.png`);
        }
        await b.close();
    }
} finally {
    app.kill();
    fs.rmSync(installation, { force: true });
}
process.exit(failed ? 1 : 0);
