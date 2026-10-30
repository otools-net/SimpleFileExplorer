// Builds the web client as shipped (production: minified) but with source maps, into <work>/profile-client, for the
// @diagnose profiles (specs/diagnose/menu-profile.spec.ts), which serve it in place of the app's own client.
//   node scripts/profile-client.mjs        then   SFE_E2E_DIAGNOSE=1 npm run e2e -- specs/diagnose/menu-profile.spec.ts --project=android-slow
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const work = path.resolve(process.env.SFE_E2E_WORK ?? path.join(os.tmpdir(), 'sfe-e2e'));
const out = path.join(work, 'profile-client');
fs.rmSync(out, { recursive: true, force: true });

const result = spawnSync('npm run build -- --source-map --output-path "' + out + '"', {
    cwd: path.join(repo, 'src/Sfe.Web'), stdio: 'inherit', shell: true,
});
if (result.status !== 0)
    process.exit(result.status ?? 1);
// A plain output path puts the files in browser/: keep one layout.
const browser = path.join(out, 'browser');
if (fs.existsSync(path.join(browser, 'index.html'))) {
    for (const name of fs.readdirSync(browser))
        fs.renameSync(path.join(browser, name), path.join(out, name));
    fs.rmdirSync(browser);
}
console.log(`profiling client in ${out}`);
