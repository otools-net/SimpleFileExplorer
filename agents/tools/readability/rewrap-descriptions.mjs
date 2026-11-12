#!/usr/bin/env node
// Rewraps the Markdown of OpenAPI descriptions written as C# raw strings (`.WithDescription("""…""")`, or assigned to
// a name ending in `Description`) where a line is over the limit. Only the paragraphs holding a long line move; the
// rendered Markdown stays the same (a line break inside a paragraph is a space). Words, `code` and `{holes}` are
// never split, and no line is made to start with what Markdown would read as a list, heading, quote or table. Other
// raw strings (SQL, test data, console text) are left alone. See README.md.
//
//   node agents/tools/readability/rewrap-descriptions.mjs [--check] <file or folder>...
import { readFileSync, writeFileSync, statSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const LIMIT = 120;
const SKIPPED = /(^|[\\/])(node_modules|bin|obj|Migrations|AccessMigrations|samples)([\\/]|$)/;
const OWNER = /\.WithDescription\(\s*$|\w*Description\s*=\s*$|\w*Description\s*(\+\s*)?$|\.WithDescription\(\s*\$*"""\s*$|\w*Description\s*=\s*\$*"""\s*$/;
const MARKDOWN_START = /^([-*+>#|]|\d+[.)])/;

const args = process.argv.slice(2);
const check = args.includes('--check');
const roots = args.filter((a) => a !== '--check');
if (roots.length === 0) {
    console.error('usage: rewrap-descriptions.mjs [--check] <file or folder>...');
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

/** Words of a paragraph: spaces inside `code` and `{holes}` do not split. */
function words(text) {
    const result = [];
    let current = '';
    let code = false;
    let holes = 0;
    for (const c of text) {
        if (c === '`') code = !code;
        else if (!code && c === '{') holes++;
        else if (!code && c === '}' && holes > 0) holes--;
        if (c === ' ' && !code && holes === 0) {
            if (current) result.push(current);
            current = '';
        } else {
            current += c;
        }
    }
    if (current) result.push(current);
    return result;
}

/** A paragraph's words in lines of at most LIMIT characters, none but the first starting like Markdown syntax. */
function wrap(indent, text) {
    const bullet = /^([-*+] |\d+[.)] )/.exec(text)?.[1] ?? '';
    const hanging = indent + ' '.repeat(bullet.length);
    const lines = [];
    let line = [];
    let width = indent.length;
    for (const word of words(text)) {
        const prefix = lines.length === 0 ? indent : hanging;
        if (line.length > 0 && width + 1 + word.length > LIMIT) {
            // Never begin a line with `-`, `1.`, `#`…: the word before it comes down too.
            const moved = MARKDOWN_START.test(word) && line.length > 1 ? [line.pop()] : [];
            lines.push(prefix + line.join(' '));
            line = [...moved, word];
            width = hanging.length + line.join(' ').length;
        } else {
            width += (line.length > 0 ? 1 : 0) + word.length;
            line.push(word);
        }
    }
    lines.push((lines.length === 0 ? indent : hanging) + line.join(' '));
    return lines;
}

function rewrap(source) {
    const eol = source.includes('\r\n') ? '\r\n' : '\n';
    const lines = source.split(eol);
    const out = [];
    let changed = 0;
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        out.push(line);
        const opens = /^\s*(\+\s*)?\$*"""\s*$/.test(line) || /(=|\()\s*\$*"""\s*$/.test(line);
        const before = [lines[i - 1] ?? '', line.replace(/\$*"""\s*$/, '')].join(' ').trim();
        if (!opens || !OWNER.test(before)) continue;
        // The content, up to the closing quotes.
        let end = i + 1;
        while (end < lines.length && !/^\s*"""/.test(lines[end])) end++;
        const content = lines.slice(i + 1, end);
        if (!content.some((l) => l.length > LIMIT)) continue;
        const result = [];
        let paragraph = [];
        const flush = () => {
            if (paragraph.length === 0) return;
            if (paragraph.some((l) => l.length > LIMIT)) {
                const indent = /^\s*/.exec(paragraph[0])[0];
                result.push(...wrap(indent, paragraph.map((l) => l.trim()).join(' ')));
                changed++;
            } else {
                result.push(...paragraph);
            }
            paragraph = [];
        };
        for (const l of content) {
            if (l.trim() === '') {
                flush();
                result.push(l);
            } else {
                if (/^\s*([-*+] |\d+[.)] |#|>|\|)/.test(l)) flush();
                paragraph.push(l);
            }
        }
        flush();
        out.push(...result);
        i = end - 1;
    }
    return { text: out.join(eol), changed };
}

let total = 0;
for (const root of roots) {
    for (const file of files(root)) {
        const source = readFileSync(file, 'utf8');
        const { text, changed } = rewrap(source);
        if (changed === 0 || text === source) continue;
        total += changed;
        console.log(`${file}: ${changed}`);
        if (!check) writeFileSync(file, text);
    }
}
console.log(`${total} description paragraph(s) ${check ? 'to rewrap' : 'rewrapped'}`);
process.exit(check && total > 0 ? 1 : 0);
