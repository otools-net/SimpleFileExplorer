#!/usr/bin/env node
// Runs SFE's test suites and prints only what failed (README.md). The runner's whole output goes to a log file in the
// temp folder; the report is read from the runner's structured results (TRX, Vitest JSON, Playwright JSON), so a
// failing test is named with its message on the first run, never cut off by `tail`.
//
//   node agents/tools/tests/run.mjs [dotnet|web|e2e|all] [-- <arguments for the runner>]
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { dotnetSuite } from './dotnet.mjs';
import { webSuite } from './web.mjs';
import { e2eSuite } from './e2e.mjs';
import { printReport } from './report.mjs';

// The drive letter in capitals: from `c:\…` (VS Code's, this script started as `node c:/…/run.mjs`) every spec file of
// `ng test` fails with "Vitest failed to find the runner" (README.md).
const root = path
    .resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
    .replace(/^[a-z]:/, drive => drive.toUpperCase());
const suites = { dotnet: dotnetSuite, web: webSuite, e2e: e2eSuite };

function parseArguments(argv) {
    const split = argv.indexOf('--');
    const own = split < 0 ? argv : argv.slice(0, split);
    const extra = split < 0 ? [] : argv.slice(split + 1);
    const names = own.length === 0 || own.includes('all') ? ['dotnet', 'web'] : own;
    const unknown = names.filter(name => !suites[name]);
    if (unknown.length > 0 || (extra.length > 0 && names.length > 1)) {
        console.error('usage: run.mjs [dotnet|web|e2e|all] [-- <arguments for one runner>]');
        process.exit(2);
    }
    return { names, extra };
}

function runToLog(command, args, cwd, env, logFile) {
    return new Promise(resolve => {
        const log = fs.createWriteStream(logFile);
        const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, windowsHide: true });
        child.stdout.pipe(log, { end: false });
        child.stderr.pipe(log, { end: false });
        child.on('error', error => log.write(`\n${error.message}\n`));
        child.on('close', code => log.end(() => resolve(code ?? 1)));
    });
}

async function runSuite(name, extra, work) {
    const folder = path.join(work, name);
    fs.mkdirSync(folder, { recursive: true });
    const suite = suites[name]({ root, folder, extra });
    const logFile = path.join(folder, 'output.log');
    console.log(`${name}: running (full output: ${logFile})`);
    const started = Date.now();
    const exitCode = await runToLog(suite.command, suite.args, suite.cwd, suite.env, logFile);
    const result = suite.read();
    const log = fs.readFileSync(logFile, 'utf8');
    return printReport({ name, exitCode, result, log, logFile, seconds: (Date.now() - started) / 1000 });
}

// A folder per run under the temp folder (not `sfe-tests`: the backend tests' libraries are there); the last 10 kept.
function newRunFolder() {
    const runs = path.join(os.tmpdir(), 'sfe-test-runs');
    fs.mkdirSync(runs, { recursive: true });
    for (const old of fs.readdirSync(runs).sort().slice(0, -9)) {
        fs.rmSync(path.join(runs, old), { recursive: true, force: true });
    }
    return path.join(runs, new Date().toISOString().replace(/[:.]/g, '-'));
}

const { names, extra } = parseArguments(process.argv.slice(2));
const work = newRunFolder();
let failed = false;
for (const name of names) {
    failed = !(await runSuite(name, extra, work)) || failed;
}
process.exit(failed ? 1 : 0);
