// The end-to-end tests (Playwright) with the JSON reporter, read back for the failed tests (spec, title, project) and
// the run's own errors (global setup, a spec that does not load). The config's HTML report is not written then.
import fs from 'node:fs';
import path from 'node:path';

function* specsOf(suite) {
    for (const spec of suite.specs ?? []) yield spec;
    for (const child of suite.suites ?? []) yield* specsOf(child);
}

function failureOf(spec, test) {
    const result = test.results?.at(-1) ?? {};
    const error = result.errors?.[0] ?? result.error ?? {};
    const name = `${spec.file}:${spec.line} > ${spec.title} [${test.projectName}]`;
    const stack = (error.stack ?? '').split('\n').filter(line => /^\s+at /.test(line)).join('\n');
    return { name, message: error.message ?? `status ${result.status ?? test.status}`, stack };
}

function readPlaywright(report) {
    const stats = report.stats ?? {};
    const counts = {
        passed: stats.expected ?? 0,
        failed: stats.unexpected ?? 0,
        skipped: stats.skipped ?? 0,
        flaky: stats.flaky ?? 0,
    };
    const failures = (report.errors ?? []).map(error => ({ name: 'run error', message: error.message, stack: '' }));
    const flaky = [];
    for (const spec of (report.suites ?? []).flatMap(suite => [...specsOf(suite)])) {
        for (const test of spec.tests ?? []) {
            if (test.status === 'unexpected') failures.push(failureOf(spec, test));
            if (test.status === 'flaky') flaky.push(`${spec.file}:${spec.line} > ${spec.title} [${test.projectName}]`);
        }
    }
    return { counts, failures, flaky };
}

export function e2eSuite({ root, folder, extra }) {
    const cwd = path.join(root, 'tests/e2e');
    const output = path.join(folder, 'playwright.json');
    return {
        command: process.execPath,
        args: [path.join(cwd, 'node_modules/@playwright/test/cli.js'), 'test', '--reporter=json', ...extra],
        cwd,
        env: { PLAYWRIGHT_JSON_OUTPUT_FILE: output },
        read() {
            if (!fs.existsSync(output)) return null;
            return readPlaywright(JSON.parse(fs.readFileSync(output, 'utf8')));
        },
    };
}
