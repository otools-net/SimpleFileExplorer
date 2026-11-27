// `dotnet test` with a TRX file per test project, read back for the failed tests (class.method, message, stack) and the
// run's own errors (a test host that crashed, a run aborted by `--blame-hang`).
import fs from 'node:fs';
import path from 'node:path';

function unescapeXml(text) {
    return text
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
        .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
        .replace(/&amp;/g, '&');
}

function attribute(tag, name) {
    const match = tag.match(new RegExp(`\\s${name}="([^"]*)"`));
    return match ? unescapeXml(match[1]) : '';
}

function element(xml, name) {
    const match = xml.match(new RegExp(`<${name}>([\\s\\S]*?)</${name}>`));
    return match ? unescapeXml(match[1]) : '';
}

// testId -> ClassName (without the namespace), from the TestDefinitions.
function classNames(xml) {
    const names = new Map();
    for (const match of xml.matchAll(/<UnitTest\s[^>]*?id="([^"]+)"[\s\S]*?<TestMethod\s([^>]*)>/g)) {
        const className = attribute(` ${match[2]}`, 'className');
        names.set(match[1], className.split('.').pop().split(',')[0]);
    }
    return names;
}

function readTrx(xml, counts, failures) {
    const classes = classNames(xml);
    for (const match of xml.matchAll(/<UnitTestResult\s([^>]*?)(\/>|>([\s\S]*?)<\/UnitTestResult>)/g)) {
        const tag = ` ${match[1]}`;
        const outcome = attribute(tag, 'outcome');
        if (outcome === 'Passed') counts.passed++;
        else if (outcome === 'Failed' || outcome === 'Error' || outcome === 'Timeout' || outcome === 'Aborted') {
            counts.failed++;
            const owner = classes.get(attribute(tag, 'testId'));
            const kind = outcome === 'Failed' ? '' : ` (${outcome})`;
            const name = `${owner ? `${owner}.` : ''}${attribute(tag, 'testName')}${kind}`;
            const body = match[3] ?? '';
            failures.push({ name, message: element(body, 'Message'), stack: element(body, 'StackTrace') });
        } else counts.skipped++;
    }
    for (const match of xml.matchAll(/<RunInfo\s[^>]*outcome="(Error|Warning)"[^>]*>([\s\S]*?)<\/RunInfo>/g)) {
        if (match[1] === 'Error') failures.push({ name: 'test run error', message: element(match[2], 'Text') });
    }
}

export function dotnetSuite({ root, folder, extra }) {
    const results = path.join(folder, 'results');
    return {
        command: 'dotnet',
        args: ['test', '--logger', 'trx', '--results-directory', results, ...extra],
        cwd: root,
        env: { DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' },
        read() {
            const files = fs.existsSync(results) ? fs.readdirSync(results).filter(file => file.endsWith('.trx')) : [];
            if (files.length === 0) return null;
            const counts = { passed: 0, failed: 0, skipped: 0 };
            const failures = [];
            for (const file of files) readTrx(fs.readFileSync(path.join(results, file), 'utf8'), counts, failures);
            return { counts, failures };
        },
    };
}
