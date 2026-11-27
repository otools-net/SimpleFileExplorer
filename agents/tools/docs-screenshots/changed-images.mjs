// Which re-saved docs images really changed: compares each modified docs/images/*.png with its committed version
// (ffmpeg's SSIM; a size change counts as changed) and, with --revert, puts back those that only differ by noise.
//
//   node agents/tools/docs-screenshots/changed-images.mjs [--ffmpeg <path>] [--threshold 0.995] [--revert]
//
// Prints one line per image: its SSIM (1 = same) and whether it is kept or reverted.
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const args = process.argv.slice(2);
const option = (name, fallback) => {
    const at = args.indexOf(name);
    return at >= 0 ? args[at + 1] : fallback;
};
const ffmpeg = option('--ffmpeg', process.env.FFMPEG || 'ffmpeg');
const threshold = Number(option('--threshold', '0.995'));
const revert = args.includes('--revert');

const git = (...parts) => execFileSync('git', parts, { encoding: 'buffer', maxBuffer: 1 << 28 });
const modified = git('diff', '--name-only', '--', 'docs/images/*.png')
    .toString()
    .split('\n')
    .filter(Boolean);

const work = mkdtempSync(join(tmpdir(), 'sfe-changed-images-'));
const reverted = [];
try {
    for (const file of modified) {
        const old = join(work, 'old.png');
        writeFileSync(old, git('show', `HEAD:${file}`));
        const ssim = similarity(old, file);
        const same = ssim !== null && ssim >= threshold;
        console.log(`${ssim === null ? 'size' : ssim.toFixed(5)}  ${same ? 'noise' : 'CHANGED'}  ${file}`);
        if (same) reverted.push(file);
    }
} finally {
    rmSync(work, { recursive: true, force: true });
}
if (revert && reverted.length) git('checkout', '--', ...reverted);
console.log(`${modified.length - reverted.length} changed, ${reverted.length} noise${revert ? ' (reverted)' : ''}`);

/** SSIM of two pictures, or null when their sizes differ (ffmpeg refuses them). */
function similarity(a, b) {
    const run = spawnSync(ffmpeg, ['-hide_banner', '-i', a, '-i', b, '-lavfi', 'ssim', '-f', 'null', '-'], {
        encoding: 'utf8',
    });
    const match = /All:([\d.]+)/.exec(run.stderr);
    return match ? Number(match[1]) : null;
}
