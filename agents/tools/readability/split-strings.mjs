#!/usr/bin/env node
// Splits C# string literals that make a line too long into `"…" + "…"` at a space, the text unchanged: regular and
// interpolated strings (`$"…"`), never inside an interpolation hole or an escape. Verbatim (`@"`), raw (`"""`) and
// UTF-8 (`"…"u8`) literals, comments and lines a split cannot bring under the limit are left (by hand). Run CSharpier
// after it: it lays out the `+` chains. See README.md.
//
//   node agents/tools/readability/split-strings.mjs [--check] <file or folder>...
import { readFileSync, writeFileSync, statSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const LIMIT = 120;
const SKIPPED = /(^|[\\/])(node_modules|bin|obj|Migrations|AccessMigrations|samples)([\\/]|$)/;
const args = process.argv.slice(2);
const check = args.includes('--check');
const roots = args.filter((a) => a !== '--check');
if (roots.length === 0) {
    console.error('usage: split-strings.mjs [--check] <file or folder>...');
    process.exit(2);
}

function* files(path) {
    if (SKIPPED.test(path)) return;
    if (statSync(path).isDirectory()) {
        for (const name of readdirSync(path)) yield* files(join(path, name));
    } else if (path.endsWith('.cs')) {
        yield path;
    }
}

/**
 * The string literals of a line: `{ start, end, prefix, splittable, spaces }` with `end` after the closing quote and
 * `spaces` the offsets (in the line) of the spaces where a split keeps the text: outside holes and escapes. Stops at
 * a `//` comment; null when the line opens or closes a raw string, or a literal does not end on it.
 */
function literals(line, context = '') {
    const found = [];
    let i = 0;
    while (i < line.length) {
        const c = line[i];
        if (c === '/' && line[i + 1] === '/') break;
        if (c === "'") {
            // A char literal: '"' must not open a string.
            const close = line.indexOf("'", line[i + 1] === '\\' ? i + 3 : i + 2);
            if (close < 0) return null;
            i = close + 1;
            continue;
        }
        if (c !== '"') {
            i++;
            continue;
        }
        if (line.startsWith('"""', i)) return null;
        let p = i;
        while (p > 0 && (line[p - 1] === '$' || line[p - 1] === '@')) p--;
        const prefix = line.slice(p, i);
        const verbatim = prefix.includes('@');
        const interpolated = prefix.includes('$');
        const spaces = [];
        let depth = 0;
        let j = i + 1;
        let closed = false;
        while (j < line.length) {
            const d = line[j];
            if (depth === 0 && !verbatim && d === '\\') {
                j += 2;
                continue;
            }
            if (interpolated && d === '{') {
                if (depth === 0 && line[j + 1] === '{') {
                    j += 2;
                    continue;
                }
                depth++;
            } else if (interpolated && d === '}' && depth > 0) {
                depth--;
            } else if (depth > 0 && d === '"') {
                // A string inside a hole: skip it whole.
                const close = line.indexOf('"', j + 1);
                if (close < 0) return null;
                j = close + 1;
                continue;
            } else if (depth === 0 && d === '"') {
                if (verbatim && line[j + 1] === '"') {
                    j += 2;
                    continue;
                }
                closed = true;
                break;
            } else if (depth === 0 && d === ' ') {
                spaces.push(j);
            }
            j++;
        }
        if (!closed) return null;
        const end = j + 1;
        const splittable = !verbatim && keepsMeaning(line, p, end) && !(interpolated && SQL.test(context + line));
        found.push({ start: p, end, prefix, splittable, spaces });
        i = end;
    }
    return found;
}

/**
 * Calls taking a `FormattableString`: `$"a" + $"b"` is a plain string there, which does not compile, or, where a
 * `string` overload exists, would send the values inside the SQL text. Interpolated strings near them are left.
 */
const SQL = /SqlQuery|FromSql|ExecuteSql|SqlInterpolated|FormattableString/;

/**
 * Whether `"a b"` can become `"a " + "b"` where it stands: not when something binds tighter than `+` to it (a member
 * access or index after it, a cast before it), not in a pattern (`is`, `case`, `or`, `and`, `not`, a switch arm), not
 * a UTF-8 literal.
 */
function keepsMeaning(line, start, end) {
    const after = line.slice(end).trimStart();
    const before = line.slice(0, start).trimEnd();
    if (/^(u8|\.|\[|\?\.|=>|!)/.test(after) || /^[\w(]/.test(after)) return false;
    if (/\)$/.test(before) || /\b(is|case|or|and|not)$/.test(before)) return false;
    return true;
}

/** The line split once at the last space that fits, or null. The rest goes on a new line, `+ ` first. */
function splitOnce(line, context) {
    if (line.length <= LIMIT || /^\s*\/\//.test(line)) return null;
    const all = literals(line, context);
    if (!all) return null;
    const indent = /^\s*/.exec(line)[0];
    for (const literal of all.filter((l) => l.splittable && l.end > LIMIT - 10).reverse()) {
        // The first part ends with its space and a closing quote: at most LIMIT characters.
        const fits = literal.spaces.filter((s) => s + 2 <= LIMIT && s > literal.start + literal.prefix.length + 10);
        if (fits.length === 0) continue;
        // After a punctuation mark when one is near the end (a sentence or a CSP directive stays whole), else the last.
        const last = fits[fits.length - 1];
        const at = fits.filter((s) => /[;,.:)]/.test(line[s - 1]) && last - s <= 30).pop() ?? last;
        const head = line.slice(0, at + 1) + '"';
        const tail = `${indent}    + ${literal.prefix}"` + line.slice(at + 1);
        return [head, tail];
    }
    return null;
}

function split(source) {
    const eol = source.includes('\r\n') ? '\r\n' : '\n';
    const out = [];
    let raw = false;
    let count = 0;
    for (const line of source.split(eol)) {
        const quotes = (line.match(/"""/g) ?? []).length;
        let pending = [line];
        if (!raw) {
            const done = [];
            while (pending.length) {
                const next = pending.shift();
                // The lines before it: the call taking the string may be a few lines up.
                const parts = splitOnce(next, out.slice(-4).join('\n'));
                if (parts) {
                    count++;
                    done.push(parts[0]);
                    pending.unshift(parts[1]);
                } else {
                    done.push(next);
                }
            }
            pending = done;
        }
        out.push(...pending);
        if (quotes % 2 === 1) raw = !raw;
    }
    return { text: out.join(eol), count };
}

let total = 0;
for (const root of roots) {
    for (const file of files(root)) {
        const source = readFileSync(file, 'utf8');
        const { text, count } = split(source);
        if (count === 0) continue;
        total += count;
        console.log(`${file}: ${count}`);
        if (!check) writeFileSync(file, text);
    }
}
console.log(`${total} split(s) ${check ? 'to make' : 'made'}`);
process.exit(check && total > 0 ? 1 : 0);
