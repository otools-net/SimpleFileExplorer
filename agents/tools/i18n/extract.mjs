#!/usr/bin/env node
// Extracts the client's texts marked for translation (`i18n` in templates, `$localize` in code) into the English
// source file src/Sfe.Web/src/locale/messages.xlf (XLIFF 2.0, with the descriptions for translators), or checks that
// the committed file is up to date (--check). See README.md and agents/docs/development/translations.md.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const web = resolve(import.meta.dirname, '../../../src/Sfe.Web');
const sourceFile = join(web, 'src/locale/messages.xlf');
const check = process.argv.includes('--check');

const outputPath = check ? mkdtempSync(join(tmpdir(), 'sfe-i18n-')) : join(web, 'src/locale');
try {
    extract(outputPath);
    if (check) process.exitCode = compare(join(outputPath, 'messages.xlf'));
    else console.log(`Written: ${sourceFile}`);
} finally {
    if (check) rmSync(outputPath, { recursive: true, force: true });
}

function extract(folder) {
    const ng = join(web, 'node_modules/@angular/cli/bin/ng.js');
    const args = ['extract-i18n', '--format', 'xlf2', '--output-path', folder, '--out-file', 'messages.xlf'];
    execFileSync(process.execPath, [ng, ...args, '--progress', 'false'], {
        cwd: web,
        stdio: ['ignore', 'ignore', 'inherit'],
    });
}

function compare(extracted) {
    const committed = existsSync(sourceFile) ? readFileSync(sourceFile, 'utf8') : '';
    if (normalise(committed) === normalise(readFileSync(extracted, 'utf8'))) {
        console.log('The source file of the texts is up to date.');
        return 0;
    }
    console.error(`${sourceFile} is out of date: run node agents/tools/i18n/extract.mjs and commit it.`);
    return 1;
}

/** Line endings and the locations (file and line, moved by any edit above a text) don't count. */
function normalise(text) {
    return text
        .replace(/\r\n/g, '\n')
        .replace(/^\s*<note category="location">.*<\/note>\n/gm, '')
        .trim();
}
