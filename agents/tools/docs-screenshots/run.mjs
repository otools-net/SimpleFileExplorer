// Regenerates the documentation screenshots (docs/images) from scratch, in one go:
//   build the app → sample library → start the app on it → seed (setup, tags, accounts, trickplay...) → shots → stop.
//
//   node run.mjs [--ffmpeg <path>] [--work <dir>] [--port 5093] [--skip-build] [--shots-only] [scene...]
//
// --ffmpeg      ffmpeg for thumbnails, trickplay, similar videos and durations; else env FFMPEG, else `ffmpeg` on the
//               PATH, else none (video tiles show an icon, trickplay stays empty: avoid for real docs)
// --work        scratch folder for the build, library, database and caches (default <tmp>/sfe-docs); emptied unless
//               --shots-only
// --shots-only  reuses the library and database of an earlier run: starts the app and takes the shots only
// scene...      only these scenes of shots.mjs (default: all)
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { sleep } from './cdp.mjs';

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
const scenes = args;
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
function node(script, ...scriptArgs) {
    const r = spawnSync(process.execPath, [path.join(here, script), ...scriptArgs], {
        stdio: 'inherit', env: { ...process.env, SFE_URL: url, SFE_SETUP_CODE: setupCode, ...(ffmpeg ? { FFMPEG: ffmpeg } : {}) },
    });
    if (r.status !== 0) throw new Error(`${script} failed`);
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
    const r = spawnSync('dotnet', ['build', path.join(repo, 'src/Sfe.Api'), '--artifacts-path', build, '-v', 'q', '-nologo'], { stdio: 'inherit', shell: true });
    if (r.status !== 0) throw new Error('build failed');
}
const dll = path.join(build, 'bin/Sfe.Api/debug/Sfe.Api.dll');
if (!fs.existsSync(dll)) throw new Error(`no build at ${dll}: run without --skip-build`);

if (!shotsOnly) {
    step('sample library');
    node('make-media.mjs', library, ...(ffmpeg ? [ffmpeg] : []));
}

step(`app on ${url}`);
const app = spawn('dotnet', [dll, '--contentRoot', path.join(repo, 'src/Sfe.Api')], {
    cwd: work,
    stdio: ['ignore', fs.openSync(path.join(work, 'app.log'), 'w'), fs.openSync(path.join(work, 'app.err.log'), 'w')],
    env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        Urls: url,
        Sfe__Root: library,
        Sfe__Database__Provider: 'Sqlite',
        ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
        Sfe__SettingsFile: path.join(data, 'settings.json'),
        Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
        Sfe__Security__KeysDirectory: path.join(data, 'keys'),
        Sfe__Setup__Code: setupCode,
        Sfe__System__EnableShutdown: 'true',
        ...(ffmpeg ? { Sfe__Thumbnails__FfmpegPath: ffmpeg } : {}),
    },
});
try {
    let up = false;
    for (let i = 0; i < 120 && !up; i++) {
        try { await fetch(url + 'api/config'); up = true; } catch { await sleep(500); }
    }
    if (!up) throw new Error(`the app didn't start: see ${path.join(work, 'app.err.log')}`);
    if (!shotsOnly) {
        step('seed');
        node('seed.mjs', library);
    }
    step('screenshots');
    node('shots.mjs', ...scenes);
    step('links');
    node('check-links.mjs');
} finally {
    app.kill();
}
console.log(`\ndone: ${path.join(repo, 'docs/images')} (work folder ${work})`);
