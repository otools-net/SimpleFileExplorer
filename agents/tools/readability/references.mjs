// Writes what a refactoring must not change into a folder: the OpenAPI document, whether the EF model differs from
// its migrations, and the client build's files, sizes and content hashes. Run it before and after, then compare the
// two folders. See README.md.
//
//   node agents/tools/readability/references.mjs <out-folder> [--only openapi,ef,client]

import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const STEPS = ['openapi', 'routes', 'ef', 'client'];
const CONTEXTS = ['SfeDbContext', 'AccessDbContext'];

function parseArguments(argv) {
    const options = { out: null, only: STEPS };
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === '--only')
            options.only = argv[++i].split(',');
        else if (!options.out && !argv[i].startsWith('--'))
            options.out = resolve(argv[i]);
        else
            throw new Error(`Unknown argument ${argv[i]} (<out-folder> [--only ${STEPS.join(',')}])`);
    }
    if (!options.out)
        throw new Error('Name the folder to write to (a temp folder, not the repository)');
    const unknown = options.only.filter(step => !STEPS.includes(step));
    if (unknown.length)
        throw new Error(`Unknown step ${unknown.join(', ')}: ${STEPS.join(', ')}`);
    return options;
}

/** Runs a command in the repository, its output shown; throws when it fails. */
function run(command, args, { cwd = repo, env = process.env } = {}) {
    const result = spawnSync(command, args, { cwd, env, stdio: 'inherit', shell: process.platform === 'win32' });
    if (result.status !== 0)
        throw new Error(`${command} ${args.join(' ')} failed (exit ${result.status})`);
}

/** The OpenAPI document, from the test host (`OpenApiExportTests`), the client build skipped. */
function writeOpenApi(out) {
    const target = join(out, 'openapi.json');
    const env = { ...process.env, SFE_OPENAPI_OUT: target };
    const args = ['test', 'tests/Sfe.Tests', '-p:SfeSkipWebClient=true', '--filter', 'OpenApiExportTests'];
    run('dotnet', args, { env });
    console.log(`Wrote ${target}`);
}

/** Every endpoint with its rights, folder scope, policies and metadata, from the test host (`RouteExportTests`). */
function writeRoutes(out) {
    const target = join(out, 'routes.txt');
    const env = { ...process.env, SFE_ROUTES_OUT: target };
    const args = ['test', 'tests/Sfe.Tests', '-p:SfeSkipWebClient=true', '--filter', 'RouteExportTests'];
    run('dotnet', args, { env });
    console.log(`Wrote ${target}`);
}

/** EF's own check of each context: "No changes" when the model matches the last migration's snapshot. */
function writeModelCheck(out) {
    run('dotnet', ['tool', 'restore']);
    const lines = CONTEXTS.map(context => {
        const args = ['ef', 'migrations', 'has-pending-model-changes', '--context', context, '--project', 'src/Sfe.Core'];
        const result = spawnSync('dotnet', args, { cwd: repo, encoding: 'utf8', shell: process.platform === 'win32' });
        const last = (result.stdout + result.stderr).trim().split('\n').at(-1).trim();
        return `${context}: exit ${result.status}: ${last}`;
    });
    writeFileSync(join(out, 'ef-model.txt'), lines.join('\n') + '\n');
    lines.forEach(line => console.log(line));
}

function filesUnder(folder) {
    return readdirSync(folder, { recursive: true })
        .map(name => join(folder, name))
        .filter(path => statSync(path).isFile());
}

const HASHED_NAME = /-[A-Za-z0-9_-]{8}(?=\.(?:js|mjs|css)\b)/g;

/**
 * A file's content hash, the hashed names of the chunks it refers to left out (they change whenever any chunk does):
 * two builds compiling to the same code give the same hashes.
 */
function contentHash(path) {
    const bytes = readFileSync(path);
    const text = /\.(js|mjs|css|html|json|txt)$/.test(path) ? bytes.toString('utf8').replace(HASHED_NAME, '') : bytes;
    return createHash('sha256').update(text).digest('hex').slice(0, 12);
}

/**
 * The client built into the out folder (not wwwroot): each file's size and content hash, hashes left out of the names,
 * and the total.
 */
function writeClientBuild(out) {
    const build = join(out, 'client-build');
    run('npx', ['ng', 'build', '--output-path', build], { cwd: join(repo, 'src/Sfe.Web') });
    const files = filesUnder(build).map(path => ({
        name: relative(build, path).replaceAll('\\', '/').replace(/-[A-Za-z0-9_-]{8}(?=\.[\w.]+$)/, ''),
        size: statSync(path).size,
        hash: contentHash(path),
    }));
    files.sort((a, b) => a.name.localeCompare(b.name) || a.hash.localeCompare(b.hash));
    const total = files.reduce((sum, file) => sum + file.size, 0);
    const lines = [
        `total ${total} bytes in ${files.length} files`,
        ...files.map(file => `${file.hash}\t${file.size}\t${file.name}`),
    ];
    writeFileSync(join(out, 'client-build.txt'), lines.join('\n') + '\n');
    // Only the list is compared: the build itself (tens of MB) would bury the comparison of the folders.
    rmSync(build, { recursive: true, force: true });
    console.log(lines[0]);
}

function main() {
    const options = parseArguments(process.argv.slice(2));
    if (!relative(repo, options.out).startsWith('..'))
        throw new Error('Write the references outside the repository (a temp folder)');
    mkdirSync(options.out, { recursive: true });
    const steps = { openapi: writeOpenApi, routes: writeRoutes, ef: writeModelCheck, client: writeClientBuild };
    for (const step of options.only)
        steps[step](options.out);
    console.log(`Compare with an earlier run: git diff --no-index <before> ${options.out}`);
}

main();
