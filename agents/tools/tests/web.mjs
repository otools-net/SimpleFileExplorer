// The client's unit tests (`ng test`, Vitest) with the JSON reporter, read back for the failed tests and the spec files
// that failed as a whole (an import error, a failing `beforeAll`).
import fs from 'node:fs';
import path from 'node:path';

function relative(root, file) {
    return path.relative(root, file).replace(/\\/g, '/');
}

function readVitest(report, root) {
    const counts = {
        passed: report.numPassedTests ?? 0,
        failed: report.numFailedTests ?? 0,
        skipped: (report.numPendingTests ?? 0) + (report.numTodoTests ?? 0),
    };
    const failures = [];
    for (const file of report.testResults ?? []) {
        const spec = relative(root, file.name);
        const failed = (file.assertionResults ?? []).filter(test => test.status === 'failed');
        for (const test of failed) {
            const [message, ...stack] = (test.failureMessages ?? []).join('\n').split(/\n(?=\s+at )/);
            failures.push({ name: `${spec} > ${test.fullName}`, message, stack: stack.join('\n') });
        }
        if (failed.length === 0 && file.status === 'failed') {
            failures.push({ name: `${spec} (the file failed)`, message: file.message || 'no message' });
        }
    }
    return { counts, failures };
}

export function webSuite({ root, folder, extra }) {
    const cwd = path.join(root, 'src/Sfe.Web');
    const output = path.join(folder, 'vitest.json');
    return {
        command: process.execPath,
        args: [
            path.join(cwd, 'node_modules/@angular/cli/bin/ng.js'),
            'test',
            '--watch=false',
            '--reporters=json',
            `--output-file=${output}`,
            ...extra,
        ],
        cwd,
        env: { NG_CLI_ANALYTICS: 'false' },
        read() {
            if (!fs.existsSync(output)) return null;
            return readVitest(JSON.parse(fs.readFileSync(output, 'utf8')), cwd);
        },
    };
}
