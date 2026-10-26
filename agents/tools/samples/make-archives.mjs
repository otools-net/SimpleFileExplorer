// Makes the backend tests' archives that need another program (tests/samples/README.md): 7z by 7-Zip. Zips and tars
// are written by the tests themselves (.NET writes them).
//
//   node agents/tools/samples/make-archives.mjs <output folder> <7z program>
//
// The content is a small tree of text files with fixed times, so that the plain archives come out the same at each
// run; the encrypted ones differ (a random salt), as videos do.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const [output, sevenZip] = process.argv.slice(2);
if (!output || !sevenZip)
    throw new Error('usage: make-archives.mjs <output folder> <7z program>');

/** The tree inside every archive: what ArchiveReaderTests expects to find. */
const tree = {
    'readme.txt': 'An archive made for the tests.\n',
    'Photos/2024/día uno.txt': 'Unicode names survive.\n',
    'Photos/2024/notes.txt': 'x'.repeat(4096) + '\n',
    'Deep/a/b/c/d/e/bottom.txt': 'Five folders down.\n',
};
const fixedTime = new Date('2024-05-06T07:08:10Z');

const content = fs.mkdtempSync(path.join(os.tmpdir(), 'sfe-archives-'));
try {
    for (const [name, text] of Object.entries(tree)) {
        const file = path.join(content, ...name.split('/'));
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, text);
    }
    // Folders after files: writing a file changes its folder's time.
    for (const entry of fs.readdirSync(content, { recursive: true, withFileTypes: true }).reverse())
        fs.utimesSync(path.join(entry.parentPath, entry.name), fixedTime, fixedTime);

    fs.mkdirSync(output, { recursive: true });
    const make = (name, ...switches) => {
        const target = path.resolve(output, name);
        fs.rmSync(target, { force: true });
        // -mtc=off -mta=off: only modified times (the same at each run); "*" from inside the folder: relative names.
        execFileSync(sevenZip, ['a', '-bd', '-bso0', '-t7z', '-mtc=off', '-mta=off', ...switches, target, '*'], { cwd: content, stdio: 'inherit' });
    };
    make('tree-solid.7z', '-mx=5', '-ms=on');
    make('tree-files-encrypted.7z', '-mx=5', '-psecret');
    make('tree-names-encrypted.7z', '-mx=5', '-psecret', '-mhe=on');
} finally {
    fs.rmSync(content, { recursive: true, force: true });
}
