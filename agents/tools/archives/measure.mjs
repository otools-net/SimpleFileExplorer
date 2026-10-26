// Measures the archive viewer on big archives, in the real app (agents/docs/features/archive-reading.md, "Measured"):
// writes the archives (once, kept in <tmp>/sfe-archives), then for each one starts the app on them with an empty
// cache, and reports the catalog's build time, the server's peak memory, the catalog's size, and how long pages,
// Go to and Find take.
// Usage: node measure.mjs [--scale <factor>] [--only <name,...>] [--port 5097]
//   --scale 0.1 makes every archive ten times smaller (a quick run); names: zip-5m, zip-wide, zip-deep, tar-1m, 7z-1m.
// Needs a docs-screenshots run first (its build and data in <tmp>/sfe-docs, or env SFE_DOCS_WORK). Windows or Linux.
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { write7z, writeTar, writeZip } from './writers.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = process.env.SFE_DOCS_WORK ?? path.join(os.tmpdir(), 'sfe-docs');
const flag = name => {
    const at = process.argv.indexOf(name);
    return at > 0 ? process.argv[at + 1] : undefined;
};
const scale = Number(flag('--scale') ?? 1);
const only = flag('--only')?.split(',');
const port = Number(flag('--port') ?? 5097);
const url = `http://localhost:${port}/`;
const root = path.join(os.tmpdir(), 'sfe-archives');
const library = path.join(root, 'library');
const n = count => Math.max(1, Math.round(count * scale));
const pad = (value, length) => String(value).padStart(length, '0');
const one = Buffer.from('x');

/** Every scenario: its archive (written by `write`) and what is asked of it once read. */
const scenarios = [
    {
        name: 'zip-5m', file: `big-${n(5_000_000)}.zip`, what: `${n(5_000_000).toLocaleString('en')} files in folders of 1 000`,
        write: file => writeZip(file, function* () {
            for (let i = 0; i < n(5_000_000); i++)
                yield { name: `f${pad(Math.floor(i / 1000), 5)}/n${pad(i, 7)}.txt`, data: one };
        }),
        last: `n${pad(n(5_000_000) - 1, 7)}`, folder: 'f00000',
    },
    {
        name: 'zip-wide', file: `wide-${n(100_000)}.zip`, what: `${n(100_000).toLocaleString('en')} files in one folder`,
        write: file => writeZip(file, function* () {
            for (let i = 0; i < n(100_000); i++)
                yield { name: `all/n${pad(i, 7)}.txt`, data: one };
        }),
        last: `n${pad(n(100_000) - 1, 7)}`, folder: 'all', walk: true,
    },
    {
        name: 'zip-deep', file: `deep-${n(9_999)}.zip`, what: `${n(9_999).toLocaleString('en')} folders nested`,
        write: file => writeZip(file, function* () {
            const depth = n(9_999);
            for (let level = 100; level <= depth; level += 100)
                yield { name: `${'d/'.repeat(level)}at ${level}.txt`, data: one };
            yield { name: `${'d/'.repeat(depth)}bottom.txt`, data: one };
        }),
        last: 'bottom', deep: n(9_999),
    },
    {
        name: 'tar-1m', file: `big-${n(1_000_000)}.tar`, what: `${n(1_000_000).toLocaleString('en')} empty files in folders of 1 000`,
        write: file => writeTar(file, (function* () {
            for (let i = 0; i < n(1_000_000); i++)
                yield { name: `f${pad(Math.floor(i / 1000), 5)}/n${pad(i, 7)}.txt` };
        })()),
        last: `n${pad(n(1_000_000) - 1, 7)}`, folder: 'f00000',
    },
    {
        name: '7z-1m', file: `big-${n(1_000_000)}.7z`, what: `${n(1_000_000).toLocaleString('en')} empty files in folders of 1 000 (a header only)`,
        write: file => write7z(file, (function* () {
            for (let i = 0; i < n(1_000_000); i++)
                yield `f${pad(Math.floor(i / 1000), 5)}/n${pad(i, 7)}.txt`;
        })()),
        last: `n${pad(n(1_000_000) - 1, 7)}`, folder: 'f00000',
    },
].filter(scenario => !only || only.includes(scenario.name));

const dll = path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll');
if (!fs.existsSync(dll))
    throw new Error(`no build at ${dll}: run agents/tools/docs-screenshots/run.mjs first`);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

/** The process's peak memory (working set / resident), in MB. */
function peakMegabytes(pid) {
    if (process.platform === 'win32')
        return Number(execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${pid}).PeakWorkingSet64`], { encoding: 'utf8' }).trim()) / 1048576;
    const status = fs.readFileSync(`/proc/${pid}/status`, 'utf8');
    return Number(/VmHWM:\s+(\d+)/.exec(status)[1]) / 1024;
}

function folderSize(folder) {
    if (!fs.existsSync(folder))
        return 0;
    return fs.readdirSync(folder, { recursive: true, withFileTypes: true })
        .filter(entry => entry.isFile() && entry.name.endsWith('.db'))
        .reduce((sum, entry) => sum + fs.statSync(path.join(entry.parentPath, entry.name)).size, 0);
}

async function start(cache) {
    const data = path.join(work, 'data');
    const installation = path.join(root, 'installation.json');
    fs.writeFileSync(installation, JSON.stringify({ Urls: url.replace(/\/$/, ''), Sfe: { Root: library } }));
    const app = spawn('dotnet', [dll, '--contentRoot', path.join(repo, 'src/Sfe.Api')], {
        cwd: root, stdio: 'ignore',
        env: {
            ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
            Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
            Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: cache,
            Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
        },
    });
    for (let i = 0; i < 120; i++) {
        try {
            await fetch(url + 'api/migration');
            break;
        } catch {
            await sleep(500);
        }
    }
    const login = await fetch(url + 'api/session/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' },
        body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }),
    });
    if (!login.ok)
        throw new Error(`sign-in: ${login.status} ${await login.text()}`);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    return { app, cookie };
}

async function timed(cookie, route, params) {
    const started = performance.now();
    const response = await fetch(`${url}api/files/explore${route}?${new URLSearchParams(params)}`, { headers: { Cookie: cookie, 'X-SFE-Request': '1' } });
    const body = response.status === 200 || response.status === 202 ? await response.json() : await response.text();
    if (response.status !== 200 && response.status !== 202)
        throw new Error(`${route} ${JSON.stringify(params).slice(0, 300)}: ${response.status} ${String(body).slice(0, 300)}`);
    return { ms: performance.now() - started, status: response.status, body };
}

const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const results = [];
fs.mkdirSync(library, { recursive: true });
for (const scenario of scenarios) {
    const file = path.join(library, scenario.file);
    if (!fs.existsSync(file)) {
        // Older sizes of the same scenario go: the library holds only what is measured.
        for (const old of fs.readdirSync(library))
            if (old.startsWith(scenario.file.split('-')[0] + '-') && path.extname(old) === path.extname(scenario.file))
                fs.rmSync(path.join(library, old), { force: true });
        const started = performance.now();
        const written = scenario.write(file);
        console.log(`${scenario.name}: wrote ${written.entries.toLocaleString('en')} entries, ${(written.bytes / 1048576).toFixed(0)} MB in ${((performance.now() - started) / 1000).toFixed(1)} s`);
    }
    const cache = fs.mkdtempSync(path.join(root, 'cache-'));
    const { app, cookie } = await start(cache);
    try {
        const before = peakMegabytes(app.pid);
        const started = performance.now();
        let answer;
        for (;;) {
            answer = await timed(cookie, '', { path: scenario.file });
            if (answer.status === 200)
                break;
            await sleep(250);
        }
        const build = (performance.now() - started) / 1000;
        const peak = peakMegabytes(app.pid);
        const listing = answer.body;
        const root = [];
        for (let i = 0; i < 10; i++)
            root.push((await timed(cookie, '', { path: scenario.file })).ms);
        const row = {
            name: scenario.name, what: scenario.what, entries: listing.entries, complete: listing.complete, leftOut: listing.leftOut,
            archiveMB: fs.statSync(file).size / 1048576, buildS: build, memoryBeforeMB: before, peakMB: peak, catalogMB: folderSize(path.join(cache, 'archives')) / 1048576,
            rootPageMs: median(root),
        };
        if (!listing.complete) {
            // Refused or stopped (a limit): said so, nothing more to time.
            row.stopped = listing.stopped;
            results.push(row);
            console.log(JSON.stringify(row));
            continue;
        }
        if (scenario.folder) {
            const folder = await timed(cookie, '', { path: scenario.file, at: scenario.folder, limit: 1000 });
            row.folderPageMs = folder.ms;
            if (scenario.walk) {
                // Every page of the folder, as scrolling to its end asks them.
                const pages = [];
                for (let next = folder.body.next; next;) {
                    const page = await timed(cookie, '', { path: scenario.file, folder: folder.body.folder.id, after: next, limit: 1000 });
                    pages.push(page.ms);
                    next = page.body.next;
                }
                row.walk = pages.length ? `${pages.length + 1} pages, median ${median(pages).toFixed(1)} ms, last ${pages.at(-1).toFixed(1)} ms` : '1 page';
            }
            const findHere = { ms: 0, pages: 0, found: 0 };
            for (let next = null; ;) {
                const page = await timed(cookie, '/search', { path: scenario.file, q: 'n0000999', folder: folder.body.folder.id, ...(next ? { after: next } : {}) });
                findHere.ms += page.ms;
                findHere.pages++;
                findHere.found += page.body.items.length;
                if (!(next = page.body.next))
                    break;
            }
            row.findInFolder = `${findHere.ms.toFixed(0)} ms (${findHere.pages} page${findHere.pages === 1 ? '' : 's'}, ${findHere.found} found)`;
        }
        if (scenario.deep) {
            // Go to through `at`: as deep as an address holds (Kestrel's request line: 8 KB, each / sent as %2F; deeper is reached folder by folder).
            const levels = Math.min(scenario.deep, 1500);
            const deep = await timed(cookie, '', { path: scenario.file, at: Array.from({ length: levels }, () => 'd').join('/') });
            row.goToDeepMs = deep.ms;
            row.goToLevels = levels;
            row.trailSkipped = deep.body.trail.skipped;
        }
        // Find in archive: a name at the very end, every page asked until the search has looked through all of it.
        const find = { ms: 0, pages: 0, found: 0 };
        for (let next = null; ;) {
            const page = await timed(cookie, '/search', { path: scenario.file, q: scenario.last, ...(next ? { after: next } : {}) });
            find.ms += page.ms;
            find.pages++;
            find.found += page.body.items.length;
            if (!(next = page.body.next))
                break;
        }
        row.findAll = `${(find.ms / 1000).toFixed(1)} s (${find.pages} pages, ${find.found} found)`;
        results.push(row);
        console.log(JSON.stringify(row));
    } finally {
        app.kill();
        await sleep(1000);
        fs.rmSync(cache, { recursive: true, force: true });
    }
}

const fixed = (value, digits = 0) => value === undefined ? '' : value.toFixed(digits);
console.log(`\n${os.cpus()[0].model}, ${(os.totalmem() / 1073741824).toFixed(0)} GB, ${process.platform}, scale ${scale}\n`);
console.log('| Archive | Entries | Size | Read | Peak memory (before) | Catalog | Root page | Folder page | Find in archive | Other |');
console.log('|---|---|---|---|---|---|---|---|---|---|');
for (const row of results) {
    const other = [row.walk && `walk: ${row.walk}`, row.findInFolder && `find in folder: ${row.findInFolder}`,
        row.goToDeepMs !== undefined && `Go to ${row.goToLevels.toLocaleString('en')} folders down: ${fixed(row.goToDeepMs)} ms (${row.trailSkipped} skipped)`,
        !row.complete && `STOPPED: ${row.stopped}`, row.leftOut && `${row.leftOut} left out`].filter(Boolean).join('; ');
    console.log(`| ${row.name}: ${row.what} | ${row.entries.toLocaleString('en')} | ${fixed(row.archiveMB)} MB | ${fixed(row.buildS, 1)} s | ${fixed(row.peakMB)} MB (${fixed(row.memoryBeforeMB)}) | ${fixed(row.catalogMB)} MB | ${fixed(row.rootPageMs, 1)} ms | ${row.folderPageMs === undefined ? "" : `${fixed(row.folderPageMs, 1)} ms`} | ${row.findAll ?? ''} | ${other} |`);
}
