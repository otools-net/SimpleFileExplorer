// What a run prints: one summary line, then each failed test with its message and the first lines of its stack. When
// the runner failed without a failed test (a build error, a crash, a timeout), the log's error lines instead.

const MESSAGE_LINES = 20;
const STACK_LINES = 6;
const LOG_LINES = 40;

export function stripAnsi(text) {
    return String(text ?? '').replace(/\u001b\[[0-9;]*[A-Za-z]/g, '');
}

function firstLines(text, count) {
    const lines = stripAnsi(text).replace(/\r/g, '').split('\n').filter(line => line.trim() !== '');
    const kept = lines.slice(0, count);
    if (lines.length > count) kept.push(`… ${lines.length - count} more lines`);
    return kept;
}

// The test's own frames: the runner's (packages, Node's internals) say nothing about the failure.
function ownFrames(stack) {
    const frames = stripAnsi(stack).split('\n');
    const runner =
        /node_modules|node:internal|<anonymous>|Microsoft\.VisualStudio\.TestTools|System\.Reflection|InvokeStub_/;
    return frames.filter(line => !runner.test(line));
}

// Files locked by another test run (another session's `dotnet test`): said plainly, it is not this change's fault.
function printLockHint(log) {
    const holder = log.match(/The file is locked by: "([^"]+)"/);
    if (holder) console.log(`\n  hint: the build's files are locked by ${holder[1]}, another test run still going.`);
}

function printFailure(failure) {
    console.log(`\n  FAILED ${failure.name}`);
    for (const line of firstLines(failure.message, MESSAGE_LINES)) console.log(`    ${line}`);
    for (const line of firstLines(ownFrames(failure.stack ?? '').join('\n'), STACK_LINES)) {
        console.log(`      ${line.trim()}`);
    }
}

// Error lines, each with the `file:line:col:` line esbuild prints after it.
function errorLines(lines) {
    const found = [];
    lines.forEach((line, index) => {
        if (!/\berror\b|\[ERROR\]|✘/i.test(line)) return;
        found.push(line.trim());
        const next = lines.slice(index + 1, index + 3).find(other => other.trim() !== '');
        if (next && /:\d+:\d+:\s*$/.test(next)) found.push(`  at ${next.trim()}`);
    });
    return [...new Set(found)];
}

// Build errors (`error CS…`, `error TS…`, `✘ [ERROR]`) or else the end of the log: what stopped the runner.
function printLogErrors(log) {
    const lines = stripAnsi(log).replace(/\r/g, '').split('\n');
    const errors = errorLines(lines);
    const shown = errors.length > 0 ? errors.slice(0, LOG_LINES) : lines.filter(line => line.trim()).slice(-LOG_LINES);
    console.log(errors.length > 0 ? '\n  errors in the output:' : '\n  end of the output:');
    for (const line of shown) console.log(`    ${line}`);
}

function summary({ passed, failed, skipped, flaky }) {
    const parts = [`${passed} passed`, `${failed} failed`, `${skipped} skipped`];
    if (flaky) parts.push(`${flaky} flaky`);
    return parts.join(', ');
}

// Returns whether the suite passed.
export function printReport({ name, exitCode, result, log, logFile, seconds }) {
    const time = `${Math.round(seconds)} s`;
    if (!result) {
        console.log(`${name}: FAILED before reporting any result (exit code ${exitCode}, ${time})`);
        printLogErrors(log);
        printLockHint(log);
        console.log(`\n  full output: ${logFile}`);
        return false;
    }
    const ok = exitCode === 0 && result.failures.length === 0;
    console.log(`${name}: ${ok ? 'passed' : 'FAILED'}: ${summary(result.counts)} (${time})`);
    for (const failure of result.failures) printFailure(failure);
    for (const flaky of result.flaky ?? []) console.log(`  flaky (passed on retry): ${flaky}`);
    if (!ok && result.failures.length === 0) printLogErrors(log);
    if (!ok) console.log(`\n  full output: ${logFile}`);
    return ok;
}
