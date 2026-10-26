// Where the committed sample files are, and how to tell whether they still match what makes them
// (tests/samples/README.md). Shared by update-samples.mjs and the docs screenshots' run.mjs.
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const repo = path.resolve(here, '../../..');
export const samples = path.join(repo, 'tests/samples');
export const libraryFolder = path.join(samples, 'library');
export const mediaFolder = path.join(samples, 'media');
export const archivesFolder = path.join(samples, 'archives');
export const manifestFile = path.join(samples, 'media.json');
export const lockFile = path.join(samples, 'samples.lock.json');

/** The scripts that make the sample library: a change to any of them means the library must be made again. */
export const libraryGenerators = ['make-media.mjs', 'make-epub.mjs', 'make-pdf.mjs', 'make-swf.mjs', 'make-zip.mjs']
    .map(name => path.join(repo, 'agents/tools/docs-screenshots', name));

/** The script that makes the tests' archives (7z by 7-Zip). */
export const archivesGenerator = path.join(repo, 'agents/tools/samples/make-archives.mjs');

/** Video files differ at each encoding (libvpx, libx264 threads): kept as they are unless forced. */
export const unstableExtensions = new Set(['.webm', '.mp4', '.mkv', '.avi', '.mov']);

const sha = text => crypto.createHash('sha256').update(text).digest('hex').slice(0, 16);

/** A fingerprint of the library's generators, whatever the checkout's line endings. */
export function generatorHash() {
    return sha(libraryGenerators.map(file => fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n')).join('\0'));
}

/** A fingerprint of the archives' generator. */
export function archivesHash() {
    return sha(fs.readFileSync(archivesGenerator, 'utf8').replace(/\r\n/g, '\n'));
}

/** A fingerprint of how a test medium is made. */
export function argsHash(entry) {
    return sha(JSON.stringify(entry.args));
}

export function readManifest() {
    return JSON.parse(fs.readFileSync(manifestFile, 'utf8')).media;
}

export function readLock() {
    try {
        return JSON.parse(fs.readFileSync(lockFile, 'utf8'));
    } catch {
        return { library: {}, media: {} };
    }
}

/** The committed sample library matches its generators: it can be copied instead of made. */
export function libraryIsCurrent() {
    return fs.existsSync(libraryFolder) && readLock().library?.generator === generatorHash();
}

/** Every file under `root`, relative, with `/`. */
export function listFiles(root) {
    if (!fs.existsSync(root))
        return [];
    return fs.readdirSync(root, { recursive: true, withFileTypes: true })
        .filter(entry => entry.isFile())
        .map(entry => path.relative(root, path.join(entry.parentPath, entry.name)).split(path.sep).join('/'))
        .sort();
}
