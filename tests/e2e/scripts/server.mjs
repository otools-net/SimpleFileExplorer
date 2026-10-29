// Started by Playwright (`webServer` in playwright.config.ts), stopped by it after the run:
//   build Sfe.Api (unless SFE_E2E_SKIP_BUILD=1 and a build is there) → fresh copy of tests/samples/library → the app
//   on it, External mode, SQLite, everything under the work folder (SFE_E2E_WORK, default <tmp>/sfe-e2e).
// Runs in the foreground until killed; the app's output goes to <work>/app.log.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = path.resolve(process.env.SFE_E2E_WORK ?? path.join(os.tmpdir(), 'sfe-e2e'));
const port = process.env.SFE_E2E_PORT ?? '5181';
const build = path.join(work, 'build');
const library = path.join(work, 'library');
const data = path.join(work, 'data');
const dll = path.join(build, 'bin/Sfe.Api/debug/Sfe.Api.dll');

if (process.env.SFE_E2E_SKIP_BUILD !== '1' || !fs.existsSync(dll)) {
    console.log(`[e2e] building Sfe.Api into ${build}`);
    const dotnetBuild = () => spawnSync('dotnet', ['build', path.join(repo, 'src/Sfe.Api'), '--artifacts-path', build, '-v', 'q', '-nologo'],
        { stdio: 'inherit', shell: true });
    // After a client change the first build can fail once on the previous build's chunk names (environment/build.md).
    if (dotnetBuild().status !== 0 && dotnetBuild().status !== 0) {
        console.error('[e2e] build failed');
        process.exit(1);
    }
}

for (const folder of [library, data, path.join(work, 'auth')])
    fs.rmSync(folder, { recursive: true, force: true });
fs.mkdirSync(data, { recursive: true });
fs.cpSync(path.join(repo, 'tests/samples/library'), library, { recursive: true });
// A folder the size users have (costs that grow with the listing): 600 copies of a 20 KB photo, made per run (12 MB)
// rather than committed.
const many = path.join(library, 'Many photos');
fs.mkdirSync(many);
const photo = path.join(library, 'Photos/Family/Birthday evening.jpg');
for (let i = 1; i <= 600; i++)
    fs.copyFileSync(photo, path.join(many, `Photo ${String(i).padStart(3, '0')}.jpg`));

const log = fs.openSync(path.join(work, 'app.log'), 'w');
const app = spawn('dotnet', [dll, '--contentRoot', path.join(repo, 'src/Sfe.Api'), '--no-browser', '--no-logo'], {
    cwd: work,
    stdio: ['ignore', log, log],
    env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        Urls: `http://localhost:${port}`,
        Sfe__Mode: 'External',
        Sfe__Root: library,
        Sfe__InstallationFile: path.join(data, 'installation.json'),
        Sfe__Database__Provider: 'Sqlite',
        ConnectionStrings__Sfe: `Data Source=${path.join(data, 'sfe.db')}`,
        Sfe__SettingsFile: path.join(data, 'settings.json'),
        Sfe__Thumbnails__CacheDirectory: path.join(data, 'cache'),
        Sfe__Security__KeysDirectory: path.join(data, 'keys'),
        Sfe__Setup__Code: 'e2e-setup-code-1',
        // Specs that sign in through the page, in parallel: the per-address limit would refuse them.
        Sfe__Security__SignInsPerMinute: '1000',
    },
});
console.log(`[e2e] app on http://localhost:${port}/ (log ${path.join(work, 'app.log')})`);

const stop = () => { app.kill(); process.exit(0); };
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
app.on('exit', code => { console.error(`[e2e] the app stopped (${code}): see ${path.join(work, 'app.log')}`); process.exit(1); });
