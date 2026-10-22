// Regenerates the documentation screenshots (docs/images) from scratch, in one go:
//   build the app → sample library → start the app on it → seed (setup, tags, accounts, trickplay...) → shots → stop.
//
//   node run.mjs [--ffmpeg <path>] [--work <dir>] [--port 5093] [--skip-build] [--shots-only] [--stall 5] [scene...]
//
// --ffmpeg      ffmpeg for thumbnails, trickplay, similar videos and durations; else env FFMPEG, else `ffmpeg` on the
//               PATH, else none (video tiles show an icon, trickplay stays empty: avoid for real docs)
// --work        scratch folder for the build, library, database and caches (default <tmp>/sfe-docs); emptied unless
//               --shots-only
// --shots-only  reuses the library and database of an earlier run: starts the app and takes the shots only (refused
//               when that database belongs to an earlier version of SFE: the app would wait on its update page)
// --stall       minutes without a new screenshot, nor output, before seeding or a scene counts as stuck: the step is
//               stopped and the run fails, naming the last screenshot (default 5; 0: never)
// scene...      only these scenes of shots.mjs (default: all), or `update-page`: the update page, on a copy of the
//               database marked as belonging to an earlier version, served by a second app on the next port
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import { launch, sleep } from './cdp.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '../../..');
const args = process.argv.slice(2);
const option = name => { const i = args.indexOf(name); if (i < 0) return undefined; const [, value] = args.splice(i, 2); return value; };
const flag = name => { const i = args.indexOf(name); if (i >= 0) args.splice(i, 1); return i >= 0; };
const ffmpeg = option('--ffmpeg') ?? process.env.FFMPEG ?? onPath('ffmpeg');
const work = path.resolve(option('--work') ?? path.join(os.tmpdir(), 'sfe-docs'));
const port = option('--port') ?? '5093';
const skipBuild = flag('--skip-build');
const shotsOnly = flag('--shots-only');
const stallMinutes = Number(option('--stall') ?? '5');
const images = path.join(repo, 'docs/images');
const updatePage = args.length === 0 || args.includes('update-page');
const scenes = args.filter(scene => scene !== 'update-page');
const url = `http://localhost:${port}/`;
const setupCode = 'doc-screenshots-setup';
const library = path.join(work, 'library');
const data = path.join(work, 'data');
const build = path.join(work, 'build');

/** The full path of a program on the PATH, or undefined. */
function onPath(program) {
    const r = spawnSync(process.platform === 'win32' ? 'where' : 'which', [program], { encoding: 'utf8' });
    return r.status === 0 ? r.stdout.split(/\r?\n/)[0].trim() || undefined : undefined;
}
function step(title) { console.log(`\n== ${title}`); }

/** The screenshots written (or rewritten) since `since`, with their times. */
function writtenSince(since) {
    return fs.readdirSync(images).filter(name => name.endsWith('.png'))
        .map(name => ({ name, time: fs.statSync(path.join(images, name)).mtimeMs }))
        .filter(image => image.time >= since);
}

/**
 * Runs a script of this folder, watched: each screenshot it writes is printed as it lands (`  + name`), and a script
 * that writes no screenshot and prints nothing for `--stall` minutes is stopped (a page waiting for something that
 * never comes: a dialog, an update page, a selector that no longer matches) and the run fails.
 */
function node(script, ...scriptArgs) {
    return new Promise((resolve, reject) => {
        const started = Date.now();
        const child = spawn(process.execPath, [path.join(here, script), ...scriptArgs], {
            stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, SFE_URL: url, SFE_SETUP_CODE: setupCode, ...(ffmpeg ? { FFMPEG: ffmpeg } : {}) },
        });
        let active = Date.now();
        const seen = new Map();
        let last = null;
        const forward = target => chunk => { active = Date.now(); target.write(chunk); };
        child.stdout.on('data', forward(process.stdout));
        child.stderr.on('data', forward(process.stderr));
        const report = () => {
            for (const image of writtenSince(started)) {
                if (seen.get(image.name) === image.time)
                    continue;
                seen.set(image.name, image.time);
                last = image.name;
                active = Date.now();
                console.log(`  + ${image.name}`);
            }
        };
        const watch = setInterval(() => {
            report();
            if (stallMinutes > 0 && Date.now() - active > stallMinutes * 60_000) {
                clearInterval(watch);
                child.kill();
                reject(new Error(`${script} stalled: nothing for ${stallMinutes} minutes${last ? ` after ${last}` : ''}. `
                    + `See ${path.join(work, 'app.log')}; a page may wait for something that never comes.`));
            }
        }, 2000);
        child.on('exit', code => {
            clearInterval(watch);
            // Those written since the last look.
            report();
            if (code === 0)
                resolve();
            else
                reject(new Error(`${script} failed (exit ${code})`));
        });
    });
}

if (ffmpeg && !fs.existsSync(ffmpeg)) throw new Error(`no ffmpeg at ${ffmpeg}`);
console.log(ffmpeg ? `ffmpeg: ${ffmpeg}`
    : 'WARNING: no ffmpeg (--ffmpeg, env FFMPEG or PATH): video tiles show an icon, trickplay and similar videos stay empty');
if (!shotsOnly) {
    fs.rmSync(library, { recursive: true, force: true });
    fs.rmSync(data, { recursive: true, force: true });
}
fs.mkdirSync(data, { recursive: true });

if (!skipBuild) {
    step('build');
    const dotnetBuild = () => spawnSync('dotnet', ['build', path.join(repo, 'src/Sfe.Api'), '--artifacts-path', build, '-v', 'q', '-nologo'], { stdio: 'inherit', shell: true });
    // After a client change the first build fails once ("No file exists for the asset … wwwroot\chunk-….js": the static
    // web assets list still names the previous build's chunks); the second one works.
    if (dotnetBuild().status !== 0) {
        console.log('build failed: trying once more (stale static web assets after a client change)');
        if (dotnetBuild().status !== 0)
            throw new Error('build failed');
    }
}
const dll = path.join(build, 'bin/Sfe.Api/debug/Sfe.Api.dll');
if (!fs.existsSync(dll)) throw new Error(`no build at ${dll}: run without --skip-build`);

if (!shotsOnly) {
    step('sample library');
    await node('make-media.mjs', library, ...(ffmpeg ? [ffmpeg] : []));
}

/** The app on the sample library with the database, settings and caches of `folder`; resolves once it answers. */
async function startApp(appUrl, folder, log) {
    // The library folder, address and ffmpeg as the setup page saves them (not environment variables, which the
    // installation page would show locked).
    const installation = path.join(folder, 'installation.json');
    fs.writeFileSync(installation, JSON.stringify({
        Urls: appUrl.replace(/\/$/, ''),
        Sfe: { Mode: 'Local', Root: library, ...(ffmpeg ? { Thumbnails: { FfmpegPath: ffmpeg } } : {}) },
    }, null, 2));
    const app = spawn('dotnet', [dll, '--contentRoot', path.join(repo, 'src/Sfe.Api')], {
        cwd: work,
        stdio: ['ignore', fs.openSync(path.join(work, `${log}.log`), 'w'), fs.openSync(path.join(work, `${log}.err.log`), 'w')],
        env: {
            ...process.env,
            ASPNETCORE_ENVIRONMENT: 'Development',
            Sfe__InstallationFile: installation,
            Sfe__Setup__CodeFile: path.join(folder, 'setup-code.txt'),
            Sfe__Database__Provider: 'Sqlite',
            ConnectionStrings__Sfe: `Data Source=${path.join(folder, 'sfe.db')}`,
            Sfe__SettingsFile: path.join(folder, 'settings.json'),
            Sfe__Thumbnails__CacheDirectory: path.join(folder, 'cache'),
            Sfe__Security__KeysDirectory: path.join(folder, 'keys'),
            Sfe__Setup__Code: setupCode,
        },
    });
    for (let i = 0; i < 120; i++) {
        try { await fetch(appUrl + 'api/migration'); return app; } catch { await sleep(500); }
    }
    app.kill();
    throw new Error(`the app didn't start: see ${path.join(work, `${log}.err.log`)}`);
}

/**
 * The update page: a copy of the seeded database said to belong to 1.35.0 (the page lists the changes since), served
 * by a second app, which changes nothing until the update is confirmed (it never is here).
 */
async function shootUpdatePage() {
    const folder = path.join(work, 'update');
    fs.rmSync(folder, { recursive: true, force: true });
    fs.mkdirSync(folder, { recursive: true });
    const source = new DatabaseSync(path.join(data, 'sfe.db'));
    source.exec(`VACUUM INTO '${path.join(folder, 'sfe.db').replaceAll("'", "''")}'`);
    source.close();
    const copy = new DatabaseSync(path.join(folder, 'sfe.db'));
    copy.exec(`UPDATE ServerProperties SET Value = '1.35.0' WHERE Key = 'Version'`);
    copy.close();

    const updateUrl = `http://localhost:${Number(port) + 1}/`;
    const app = await startApp(updateUrl, folder, 'update');
    try {
        const b = await launch({ width: 1280, height: 1000 });
        await b.goto(updateUrl, 3000);
        await b.waitFor('.migration-version');
        await b.eval(`document.querySelector('#migration-code').focus(); 1`);
        await b.type(setupCode);
        await sleep(500);
        await b.shot(path.join(repo, 'docs/images/update-page.png'));
        await b.close();
    } finally {
        app.kill();
    }
}

step(`app on ${url}`);
const app = await startApp(url, data, 'app');
try {
    // An earlier run's database made by an earlier version: the app only shows its update page, and every scene waits.
    const migration = await (await fetch(url + 'api/migration')).json();
    if (migration.status !== 'none')
        throw new Error(`the database of the earlier run belongs to SFE ${migration.databaseVersion}, this is ${migration.appVersion}: run without --shots-only`);
    if (!shotsOnly) {
        step('seed');
        await node('seed.mjs', library);
    }
    if (scenes.length || args.length === 0) {
        step('screenshots');
        await node('shots.mjs', ...scenes);
    }
    if (updatePage) {
        step('update page');
        await shootUpdatePage();
    }
    step('links');
    await node('check-links.mjs');
} finally {
    app.kill();
}
console.log(`\ndone: ${path.join(repo, 'docs/images')} (work folder ${work})`);
