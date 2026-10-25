// Brings tests/samples/ up to date with what makes its files (tests/samples/README.md):
//   library/  the docs screenshots' sample library, made by agents/tools/docs-screenshots/make-media.mjs
//   media/    the backend tests' media, made by ffmpeg from tests/samples/media.json
//
//   node agents/tools/samples/update-samples.mjs [--ffmpeg <path>] [--force <prefix>]... [--check]
//
// --ffmpeg  ffmpeg (else env FFMPEG, SFE_TEST_FFMPEG, or `ffmpeg` on the PATH); needed unless --check
// --force   makes these again even when nothing changed, videos included: a path under tests/samples
//           (`library/Videos/Holidays/Beach walk.webm`, `media/life-20s.mp4`) or a prefix (`library/`, `media/`)
// --check   changes nothing: lists what is out of date and exits 1 if anything is (no ffmpeg needed)
//
// Only what is out of date is made again: the library when one of its generators changed (then every file that came
// out different replaces the old one, except videos, which differ at each encoding and are kept unless forced), a
// medium when its arguments changed or its file is missing. Files nothing makes any more are deleted.
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
    argsHash, generatorHash, libraryFolder, listFiles, lockFile, mediaFolder, readLock, readManifest, repo, unstableExtensions,
} from './samples.mjs';

const args = process.argv.slice(2);
const option = name => { const i = args.indexOf(name); if (i < 0) return undefined; const [, value] = args.splice(i, 2); return value; };
const flag = name => { const i = args.indexOf(name); if (i >= 0) args.splice(i, 1); return i >= 0; };
const forced = [];
for (let value; (value = option('--force')) !== undefined;)
    forced.push(value.replace(/\\/g, '/'));
const check = flag('--check');
const ffmpeg = option('--ffmpeg') ?? process.env.FFMPEG ?? process.env.SFE_TEST_FFMPEG ?? (onPath('ffmpeg') ? 'ffmpeg' : undefined);
if (args.length)
    throw new Error(`unknown arguments: ${args.join(' ')}`);
if (!check && !ffmpeg)
    throw new Error('no ffmpeg: pass --ffmpeg <path> (the samples are the ffmpeg variant: WebM clips, MP3)');

function onPath(program) {
    return spawnSync(process.platform === 'win32' ? 'where' : 'which', [program]).status === 0;
}

const isForced = relative => forced.some(prefix => relative === prefix || relative.startsWith(prefix.endsWith('/') ? prefix : prefix + '/'));
const lock = readLock();
const next = { library: { ...lock.library }, media: {} };
const report = [];

// ---------- The sample library ----------

const generator = generatorHash();
const libraryForced = forced.some(prefix => prefix === 'library' || prefix.startsWith('library/'));
if (lock.library?.generator !== generator || libraryForced || !fs.existsSync(libraryFolder)) {
    if (check) {
        report.push(`library: ${lock.library?.generator === generator ? 'forced' : 'its generators changed'}: make it again`);
    } else {
        const made = fs.mkdtempSync(path.join(os.tmpdir(), 'sfe-samples-'));
        const fresh = path.join(made, 'library');
        console.log('making the sample library (make-media.mjs)…');
        execFileSync(process.execPath, [path.join(repo, 'agents/tools/docs-screenshots/make-media.mjs'), fresh, ffmpeg], { stdio: 'inherit' });
        const before = new Set(listFiles(libraryFolder));
        for (const relative of listFiles(fresh)) {
            const target = path.join(libraryFolder, relative);
            const source = path.join(fresh, relative);
            if (!before.has(relative)) {
                copy(source, target);
                report.push(`library: added ${relative}`);
            } else if (!fs.readFileSync(source).equals(fs.readFileSync(target))) {
                if (unstableExtensions.has(path.extname(relative).toLowerCase()) && !isForced(`library/${relative}`)) {
                    report.push(`library: kept ${relative} (a video: --force library/${relative} to replace it)`);
                } else {
                    copy(source, target);
                    report.push(`library: replaced ${relative}`);
                }
            }
            before.delete(relative);
        }
        for (const relative of before) {
            fs.rmSync(path.join(libraryFolder, relative));
            report.push(`library: deleted ${relative} (no longer made)`);
        }
        removeEmptyFolders(libraryFolder);
        fs.rmSync(made, { recursive: true, force: true });
        next.library.generator = generator;
    }
}

// ---------- The tests' media ----------

const manifest = readManifest();
const names = new Set(manifest.map(entry => entry.name));
const made = new Set();
for (const entry of manifest) {
    const target = path.join(mediaFolder, entry.name);
    const hash = argsHash(entry);
    const source = entry.args.map(arg => /\{sample:([^}]+)\}/.exec(arg)?.[1]).find(name => name && made.has(name));
    const why = !fs.existsSync(target) ? 'missing' : lock.media?.[entry.name] !== hash ? 'its arguments changed'
        : source ? `${source} was made again` : isForced(`media/${entry.name}`) ? 'forced' : null;
    if (why && check) {
        report.push(`media: ${entry.name}: ${why}`);
    } else if (why) {
        fs.mkdirSync(mediaFolder, { recursive: true });
        const resolved = entry.args.map(arg => arg.replace(/\{sample:([^}]+)\}/g, (_, name) => {
            if (!names.has(name))
                throw new Error(`${entry.name}: no sample ${name} in media.json`);
            return path.join(mediaFolder, name);
        }));
        execFileSync(ffmpeg, ['-hide_banner', '-loglevel', 'error', '-nostdin', '-y', ...resolved, target], { stdio: 'inherit' });
        made.add(entry.name);
        report.push(`media: made ${entry.name} (${why})`);
    }
    next.media[entry.name] = hash;
}
for (const relative of listFiles(mediaFolder).filter(name => !names.has(name))) {
    report.push(`media: ${check ? 'not in media.json' : 'deleted'} ${relative}`);
    if (!check)
        fs.rmSync(path.join(mediaFolder, relative));
}

if (check) {
    console.log(report.length ? report.join('\n') : 'samples up to date');
    process.exit(report.length ? 1 : 0);
}
fs.writeFileSync(lockFile, JSON.stringify(next, null, 2) + '\n');
console.log(report.length ? report.join('\n') : 'samples up to date: nothing made');

function copy(source, target) {
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(source, target);
}

function removeEmptyFolders(folder) {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        if (!entry.isDirectory())
            continue;
        const inner = path.join(folder, entry.name);
        removeEmptyFolders(inner);
        if (fs.readdirSync(inner).length === 0)
            fs.rmdirSync(inner);
    }
}
