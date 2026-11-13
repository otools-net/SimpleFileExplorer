#!/usr/bin/env node
// Breaks the lines of Angular templates (.html) over the limit without changing what they render:
// - a long start tag gets one attribute per line, its `>` at the start of the next line with the content right after
//   it (`>Edit space</button>`): whitespace between attributes is nothing in the DOM;
// - long text is wrapped at spaces outside tags and {{ }}: Angular collapses whitespace in text to one space.
// Lines of control flow (`@if (…) {`), attribute values too long for a line, and files with <pre>, <textarea> or
// ngPreserveWhitespaces are left (by hand). The check is the client build: identical. See README.md.
//
//   node agents/tools/readability/break-template-lines.mjs [--check] <file or folder>...
import { readFileSync, writeFileSync, statSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const LIMIT = 120;
const args = process.argv.slice(2);
const check = args.includes('--check');
const roots = args.filter((a) => a !== '--check');
if (roots.length === 0) {
    console.error('usage: break-template-lines.mjs [--check] <file or folder>...');
    process.exit(2);
}

function* files(path) {
    if (/node_modules/.test(path)) return;
    if (statSync(path).isDirectory()) {
        for (const name of readdirSync(path)) yield* files(join(path, name));
    } else if (
        (path.endsWith('.html') && !path.endsWith('index.html')) ||
        (path.endsWith('.ts') && !path.endsWith('.spec.ts'))
    ) {
        yield path;
    }
}

/**
 * The start tag beginning at `start` (a `<` followed by a name): its name, attributes as written, where it ends (after
 * `>`) and whether it closes itself; null when it does not end on this line.
 */
function startTag(line, start) {
    const name = /^<([a-zA-Z][\w:.-]*)/.exec(line.slice(start));
    if (!name) return null;
    const attributes = [];
    let i = start + name[0].length;
    for (;;) {
        while (line[i] === ' ') i++;
        if (i >= line.length) return null;
        if (line[i] === '>') return { name: name[1], attributes, end: i + 1, selfClosing: false };
        if (line.startsWith('/>', i)) return { name: name[1], attributes, end: i + 2, selfClosing: true };
        const from = i;
        while (i < line.length && !/[\s=>]/.test(line[i]) && !line.startsWith('/>', i)) i++;
        if (line[i] === '=') {
            i++;
            const quote = line[i];
            if (quote !== '"' && quote !== "'") return null;
            const close = line.indexOf(quote, i + 1);
            if (close < 0) return null;
            i = close + 1;
        }
        if (i === from) return null;
        attributes.push(line.slice(from, i));
    }
}

/** Where the end of `text` leaves the reader, starting from `state`: in a tag, in a quoted value, inside {{ }}. */
function scan(text, state) {
    let { inTag, quote, braces } = state;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quote) {
            if (c === quote) quote = null;
        } else if (inTag) {
            if (c === '"' || c === "'") quote = c;
            else if (c === '>') inTag = false;
        } else if (text.startsWith('{{', i)) {
            braces++;
            i++;
        } else if (text.startsWith('}}', i) && braces > 0) {
            braces--;
            i++;
        } else if (c === '<' && braces === 0 && /[a-zA-Z/!]/.test(text[i + 1] ?? '')) {
            inTag = true;
        }
    }
    return { inTag, quote, braces };
}

const inText = (state) => !state.inTag && !state.quote && state.braces === 0;

/** The line with its first long start tag (begun in text) broken, attributes one per line; null when none. */
function breakTag(line, state) {
    const indent = /^ */.exec(line)[0];
    for (let i = line.indexOf('<'); i >= 0; i = line.indexOf('<', i + 1)) {
        if (!inText(scan(line.slice(0, i), state))) continue;
        const tag = startTag(line, i);
        if (!tag || tag.attributes.length === 0) continue;
        // Worth breaking: the tag itself reaches past the limit, or the line does and the tag has several attributes.
        if (tag.end <= LIMIT && !(tag.attributes.length > 1 && line.length > LIMIT)) continue;
        const inner = indent + '    ';
        const head = line.slice(0, i) + '<' + tag.name;
        const close = tag.selfClosing ? '/>' : '>';
        return [head, ...tag.attributes.map((a) => inner + a), indent + close + line.slice(tag.end)];
    }
    return null;
}

/** Long text wrapped at a space in text (not in a tag, a quoted value or {{ }}); null when there is none. */
function breakText(line, state) {
    const indent = /^ */.exec(line)[0];
    let at = -1;
    for (let i = indent.length + 21; i <= Math.min(LIMIT, line.length - 1); i++) {
        if (line[i] === ' ' && inText(scan(line.slice(0, i), state))) at = i;
    }
    if (at < 0) return null;
    // A newline and at least two more spaces: Angular collapses a run of two or more whitespace characters into one
    // space, but keeps a lone newline as it is (text at indentation 0 then rendered "\n" instead of " ").
    const next = indent.length >= 2 ? indent : indent + '    ';
    return [line.slice(0, at), next + line.slice(at + 1)];
}

/**
 * When `line` (begun in text) leaves a tag open, and that tag's lines include one over the limit: the tag's lines
 * joined into one (`text`) and how many lines after `line` it took; null otherwise, or when a line break falls inside
 * a quoted value (joining it would change the value).
 */
function joinTag(line, pending, state) {
    let current = scan(line, state);
    if (!current.inTag || current.quote) return null;
    const lines = [line];
    while (current.inTag && lines.length <= 40 && lines.length - 1 < pending.length) {
        const next = pending[lines.length - 1];
        lines.push(next);
        current = scan(' ' + next, current);
        if (current.quote) return null;
    }
    if (current.inTag || !lines.some((l) => l.length > LIMIT)) return null;
    const text = [lines[0].trimEnd(), ...lines.slice(1).map((l) => l.trim())].join(' ');
    return { text, lines: lines.length - 1 };
}

/** The template text with its long lines broken, and how many breaks were made. */
function breakLines(source, textToo) {
    const out = [];
    let count = 0;
    let state = { inTag: false, quote: null, braces: 0 };
    const pending = source.split('\n');
    while (pending.length) {
        const line = pending.shift();
        const flow = /^\s*(@|\}|\{)/.test(line) && !/^\s*\}\s*$/.test(line);
        const joined = !flow && inText(state) ? joinTag(line, pending, state) : null;
        if (joined) {
            // A tag over several lines, one of them long: its lines joined, then one attribute per line. The broken
            // lines are final (a long attribute value stays long); what follows `>` is looked at again.
            const parts = breakTag(joined.text, state);
            if (parts) {
                pending.splice(0, joined.lines);
                count++;
                for (const part of parts.slice(0, -1)) {
                    out.push(part);
                    state = scan(part + '\n', state);
                }
                pending.unshift(parts.at(-1));
                continue;
            }
        }
        const parts =
            line.length > LIMIT && !flow && inText(state)
                ? (breakTag(line, state) ?? (textToo ? breakText(line, state) : null))
                : null;
        if (parts && parts.join('') !== line) {
            count++;
            pending.unshift(...parts);
        } else {
            out.push(line);
            state = scan(line + '\n', state);
        }
        if (count > 2000) throw new Error('does not settle');
    }
    return { text: out.join('\n'), count };
}

const keepsText = (text) => /<pre[\s>]|<textarea[\s>]|ngPreserveWhitespaces/.test(text);

/**
 * A component file with its inline templates' long lines broken: the lines between `template: \`` (ending its line)
 * and the closing backtick. A template beginning on the backtick's line is left (one-liners).
 */
function breakInlineTemplates(source) {
    let count = 0;
    const text = source.replace(/(template:\s*`\n)([\s\S]*?)(\n[ \t]*`)/g, (whole, open, body, close) => {
        if (/\$\{/.test(body)) return whole;
        const result = breakLines(body, !keepsText(body));
        count += result.count;
        return open + result.text + close;
    });
    return { text, count };
}

let total = 0;
for (const root of roots) {
    for (const file of files(root)) {
        const source = readFileSync(file, 'utf8');
        // Text keeps its whitespace in <pre> and <textarea>: only tags are broken there.
        const { text, count } = file.endsWith('.ts')
            ? breakInlineTemplates(source)
            : breakLines(source, !keepsText(source));
        // A tag whose only long line is an attribute value comes out as it went in: not a change.
        if (count === 0 || text === source) continue;
        total += count;
        console.log(`${file}: ${count}`);
        if (!check) writeFileSync(file, text);
    }
}
console.log(`${total} break(s) ${check ? 'to make' : 'made'}`);
process.exit(check && total > 0 ? 1 : 0);
