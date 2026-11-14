// Generates the C# client library's raw layer (src/Sfe.Client/Generated) from the committed v1 document
// (tests/samples/openapi/v1.json) with Kiota, the local dotnet tool of dotnet-tools.json. See README.md.
//
//   node agents/tools/client-generation/generate.mjs           # rewrites src/Sfe.Client/Generated
//   node agents/tools/client-generation/generate.mjs --check   # exit 1 when regenerating would change it

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync, mkdirSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const documentPath = join(repo, 'tests/samples/openapi/v1.json');
const outputFolder = join(repo, 'src/Sfe.Client/Generated');

/** Kiota's options: the names the library's code uses, and JSON only (no text, form or multipart packages). */
const KIOTA_ARGUMENTS = [
    'generate',
    '--language', 'CSharp',
    '--class-name', 'SfeRawClient',
    '--namespace-name', 'Otools.Net.Sfe.Client.Generated',
    '--exclude-backward-compatible',
    '--serializer', 'Microsoft.Kiota.Serialization.Json.JsonSerializationWriterFactory',
    '--deserializer', 'Microsoft.Kiota.Serialization.Json.JsonParseNodeFactory',
    '--clean-output',
    '--log-level', 'Error',
];

/** Kiota without its hints, update check or other downloads: the document is a local file. */
const KIOTA_ENVIRONMENT = { ...process.env, KIOTA_TUTORIAL_ENABLED: 'false', KIOTA_OFFLINE_ENABLED: 'true' };

function generateInto(folder) {
    // Quick when the tools are there already; a fresh clone or CI runner fetches the pinned Kiota once.
    execFileSync('dotnet', ['tool', 'restore'], { cwd: repo, stdio: ['ignore', 'ignore', 'inherit'] });
    const kiota = ['tool', 'run', 'kiota', ...KIOTA_ARGUMENTS, '--openapi', documentPath, '--output', folder];
    execFileSync('dotnet', kiota, {
        cwd: repo,
        env: KIOTA_ENVIRONMENT,
        stdio: ['ignore', 'ignore', 'inherit'],
    });
    // The lock file names where the document was, relative to the (temporary) output: not reproducible.
    rmSync(join(folder, 'kiota-lock.json'), { force: true });
}

/** Every file under `folder`, as paths relative to it with `/`. */
function filesUnder(folder, base = folder) {
    return readdirSync(folder).flatMap(name => {
        const path = join(folder, name);
        return statSync(path).isDirectory() ? filesUnder(path, base) : [relative(base, path).replaceAll('\\', '/')];
    });
}

/** Kiota writes CRLF on Windows and LF elsewhere; the repository keeps LF (.editorconfig). */
function readNormalized(path) {
    return readFileSync(path, 'utf8').replaceAll('\r\n', '\n');
}

function differences(generated, committed) {
    const generatedFiles = new Set(filesUnder(generated));
    const committedFiles = new Set(safeFilesUnder(committed));
    const changes = [];
    for (const file of generatedFiles) {
        if (!committedFiles.has(file))
            changes.push(`added: ${file}`);
        else if (readNormalized(join(generated, file)) !== readNormalized(join(committed, file)))
            changes.push(`changed: ${file}`);
    }
    for (const file of committedFiles) {
        if (!generatedFiles.has(file))
            changes.push(`removed: ${file}`);
    }
    return changes;
}

function safeFilesUnder(folder) {
    try {
        return filesUnder(folder);
    } catch {
        return [];
    }
}

function copyNormalized(from, to) {
    rmSync(to, { recursive: true, force: true });
    for (const file of filesUnder(from)) {
        const target = join(to, file);
        mkdirSync(dirname(target), { recursive: true });
        writeFileSync(target, readNormalized(join(from, file)));
    }
}

function main(argv) {
    const check = argv.includes('--check');
    const temporary = mkdtempSync(join(tmpdir(), 'sfe-client-generation-'));
    try {
        generateInto(temporary);
        if (!check) {
            copyNormalized(temporary, outputFolder);
            console.log(`Generated ${filesUnder(outputFolder).length} files into ${relative(repo, outputFolder)}`);
            return 0;
        }
        const changes = differences(temporary, outputFolder);
        for (const change of changes)
            console.log(change);
        console.log(changes.length ? `${changes.length} files differ: run generate.mjs` : 'Up to date');
        return changes.length ? 1 : 0;
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
}

process.exitCode = main(process.argv.slice(2));
