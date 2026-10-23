// Two SFE instances in one browser (two ports on localhost, each with its own database and keys, both with an account
// `admin`): signing in on one must not sign out of the other (cookies are kept per host, not per port). Also checks a
// session cookie of the old shared name (`sfe.session`, before 1 instance = 1 name) still works once, then is renamed.
// Usage: node two-instances.mjs. Needs a docs-screenshots run first (its build and seeded data in <tmp>/sfe-docs);
// serves copies of its data on :5097 and :5098.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = path.join(os.tmpdir(), 'sfe-docs');
const ports = [5097, 5098];

/** A copy of the seeded data (same accounts, new keys) served on `port`. */
function start(port) {
    const data = path.join(os.tmpdir(), 'sfe-two-instances', String(port));
    fs.rmSync(data, { recursive: true, force: true });
    fs.mkdirSync(data, { recursive: true });
    for (const file of ['sfe.db', 'sfe.db-wal', 'sfe.db-shm', 'settings.json'])
        if (fs.existsSync(path.join(work, 'data', file)))
            fs.copyFileSync(path.join(work, 'data', file), path.join(data, file));
    const installation = path.join(data, 'installation.json');
    fs.writeFileSync(installation, JSON.stringify({ Urls: `http://localhost:${port}`, Sfe: { Root: path.join(work, 'library') } }));
    return spawn('dotnet', [path.join(work, 'build/bin/Sfe.Api/debug/Sfe.Api.dll'), '--contentRoot', path.join(repo, 'src/Sfe.Api'), '--no-browser', '--no-logo'], {
        cwd: data, stdio: 'ignore',
        env: {
            ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Sfe__InstallationFile: installation,
            Sfe__Database__Provider: 'Sqlite', ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
            Sfe__SettingsFile: path.join(data, 'settings.json'), Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
            Sfe__Security__KeysDirectory: path.join(data, 'keys'), Sfe__Setup__Code: 'doc-screenshots-setup',
        },
    });
}

const results = [];
const check = (name, ok, detail = '') => {
    results.push({ name, ok });
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`);
};

const signIn = `fetch('api/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: JSON.stringify({ userName: 'admin', password: 'admin-password-1', remember: true }) }).then(r => r.status)`;
const who = `fetch('api/session', { headers: { 'X-SFE-Request': '1' } }).then(r => r.json()).then(s => s.user?.userName ?? null)`;

const apps = ports.map(start);
try {
    for (const port of ports)
        for (let i = 0; i < 120; i++) {
            try { await fetch(`http://localhost:${port}/api/migration`); break; } catch { await sleep(500); }
        }
    const b = await launch({ width: 1000, height: 700 });
    const [one, two] = ports.map(port => `http://localhost:${port}/`);

    await b.goto(one + 'login', 1500);
    check('instance 1: signed in', await b.eval(signIn) === 200 && await b.eval(who) === 'admin');
    await b.goto(two + 'login', 1500);
    check('instance 2: signed in', await b.eval(signIn) === 200 && await b.eval(who) === 'admin');
    await b.goto(one + 'login', 1500);
    check('instance 1: still signed in after signing in on instance 2', await b.eval(who) === 'admin');
    await b.goto(two + 'login', 1500);
    check('instance 2: still signed in', await b.eval(who) === 'admin');

    // Signing out of one leaves the other signed in.
    await b.eval(`fetch('api/session/logout', { method: 'POST', headers: { 'X-SFE-Request': '1' } }).then(r => r.status)`);
    check('instance 2: signed out', await b.eval(who) === null);
    await b.goto(one + 'login', 1500);
    check('instance 1: still signed in after signing out of instance 2', await b.eval(who) === 'admin');

    // A session of before (the shared name): read once, then given this instance's name.
    const cookies = (await b.send('Network.getCookies', { urls: [one] })).cookies;
    const own = cookies.find(c => c.name.startsWith('sfe.session'));
    check('instance 1: its cookie has a name of its own', own?.name === `sfe.session-${ports[0]}`, cookies.map(c => c.name).join(' '));
    if (own) {
        await b.send('Network.deleteCookies', { name: own.name, url: one });
        await b.send('Network.setCookie', { name: 'sfe.session', value: own.value, url: one, path: '/', httpOnly: true, expires: own.expires });
        check('instance 1: a session under the old name still works', await b.eval(who) === 'admin');
        const after = (await b.send('Network.getCookies', { urls: [one] })).cookies.map(c => c.name);
        check('instance 1: and is renamed', after.includes(`sfe.session-${ports[0]}`) && !after.includes('sfe.session'), after.join(' '));
    }
    await b.close();
} finally {
    for (const app of apps)
        app.kill();
}
console.log(results.every(r => r.ok) ? 'all ok' : `${results.filter(r => !r.ok).length} failed`);
process.exitCode = results.every(r => r.ok) ? 0 : 1;
