#!/usr/bin/env node
// Rewraps comments with a line over the limit: C# `///` and `//`, TypeScript `//` and the ` * ` lines of `/** */`
// and `/* */` blocks, CSS ` * ` lines. Only the paragraph holding a long line is reflowed, with its own prefix; words
// are never split, nor `<tags with attributes>` or `inline code`. Code lines ending in a comment, `<code>` and
// fenced blocks are left alone (by hand). See README.md.
//
//   node agents/tools/readability/rewrap-comments.mjs [--check] <file or folder>...
import { readFileSync, writeFileSync, statSync, readdirSync } from 'node:fs';
import { join, extname } from 'node:path';

const LIMIT = 120;
const EXTENSIONS = new Set(['.cs', '.ts', '.mts', '.mjs', '.js', '.css']);
const SKIPPED = /(^|[\\/])(node_modules|bin|obj|Migrations|AccessMigrations|samples|wwwroot|public)([\\/]|$)/;

const args = process.argv.slice(2);
const check = args.includes('--check');
const roots = args.filter((a) => a !== '--check');
if (roots.length === 0) {
    console.error('usage: rewrap-comments.mjs [--check] <file or folder>...');
    process.exit(2);
}

/** Files under the roots, by extension, outside generated and vendored folders. */
function* files(path) {
    if (SKIPPED.test(path)) return;
    if (statSync(path).isDirectory()) {
        for (const name of readdirSync(path)) yield* files(join(path, name));
    } else if (EXTENSIONS.has(extname(path))) {
        yield path;
    }
}

/**
 * The comment prefix of a line (indent and marker with its space) and its text, or null for a line that is not a
 * comment alone on its line. `inBlock` says whether the line is inside a block comment.
 */
function split(line, inBlock, cs) {
    const m = cs
        ? /^(\s*\/\/\/? ?)(.*)$/.exec(line)
        : inBlock
          ? /^(\s*\* ?)(.*)$/.exec(line)
          : /^(\s*\/\/ ?)(.*)$/.exec(line);
    if (!m || (cs && m[2].startsWith('/'))) return null;
    return { prefix: m[1], text: m[2] };
}

/** A line that starts a paragraph of its own: a tag line, a bullet, a JSDoc tag, a heading, or empty. */
function startsParagraph(text) {
    return text.trim() === '' || /^(<\/?\w|[-*•] |\d+[.)] |@\w|#)/.test(text.trimStart());
}

/** A line after which a new paragraph starts: one ending a block tag. */
function endsParagraph(text) {
    return /<\/(summary|remarks|param|typeparam|returns|exception|value|example|para)>\s*$|^\s*<(summary|remarks|para)>\s*$/.test(
        text,
    );
}

/** Words of a paragraph: spaces inside `<…>` and `` `…` `` do not split. */
function words(text) {
    const result = [];
    let current = '';
    let tag = false;
    let code = false;
    for (const c of text) {
        if (c === '`') code = !code;
        else if (c === '<' && !code) tag = true;
        else if (c === '>' && !code) tag = false;
        if (c === ' ' && !tag && !code) {
            if (current) result.push(current);
            current = '';
        } else {
            current += c;
        }
    }
    if (current) result.push(current);
    return result;
}

/**
 * The paragraph's words in lines of at most LIMIT characters; a bullet's next lines are indented under its text. A
 * `<summary>…</summary>` (or remarks) on one line takes the block form: its tags on lines of their own.
 */
function wrap(prefix, text) {
    const block = /^<(summary|remarks)>\s*(.*?)\s*<\/\1>$/.exec(text);
    if (block) return [`${prefix}<${block[1]}>`, ...wrapWords(prefix, block[2]), `${prefix}</${block[1]}>`];
    return wrapWords(prefix, text);
}

function wrapWords(prefix, text) {
    const bullet = /^([-*•] |\d+[.)] )/.exec(text)?.[1] ?? '';
    const hanging = prefix + ' '.repeat(bullet.length);
    const lines = [];
    let line = prefix;
    let empty = true;
    for (const word of words(text)) {
        const candidate = empty ? line + word : `${line} ${word}`;
        if (!empty && candidate.length > LIMIT) {
            lines.push(line);
            line = hanging + word;
        } else {
            line = candidate;
        }
        empty = false;
    }
    lines.push(line);
    return lines;
}

function rewrap(source, cs) {
    const eol = source.includes('\r\n') ? '\r\n' : '\n';
    const lines = source.split(eol);
    const out = [];
    let inBlock = false;
    let fenced = false;
    let preformatted = false;
    let changed = 0;
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const opensBlock = !cs && /^\s*\/\*/.test(line) && !line.includes('*/');
        const comment = split(line, inBlock && !opensBlock, cs);
        if (comment) {
            if (/^\s*```/.test(comment.text)) fenced = !fenced;
            if (/<code>/.test(comment.text)) preformatted = true;
        }
        const reflow = comment && line.length > LIMIT && !fenced && !preformatted && !/^\s*```/.test(comment.text);
        if (!reflow) {
            if (comment && /<\/code>/.test(comment.text)) preformatted = false;
            out.push(line);
            if (opensBlock) inBlock = true;
            if (inBlock && line.includes('*/')) inBlock = false;
            continue;
        }
        // The paragraph: back to its first line (already written), forward to its last.
        let start = out.length;
        while (start > 0) {
            const previous = split(out[start - 1], inBlock, cs);
            if (!previous || previous.prefix !== comment.prefix || endsParagraph(previous.text)) break;
            if (startsParagraph(out[start - 1].slice(previous.prefix.length))) {
                start--;
                break;
            }
            start--;
        }
        if (startsParagraph(comment.text)) start = out.length;
        const texts = out.splice(start).map((l) => l.slice(comment.prefix.length));
        texts.push(comment.text);
        while (i + 1 < lines.length && !endsParagraph(texts[texts.length - 1])) {
            const next = split(lines[i + 1], inBlock, cs);
            if (!next || next.prefix !== comment.prefix || startsParagraph(next.text) || lines[i + 1].includes('*/'))
                break;
            texts.push(next.text);
            i++;
        }
        out.push(...wrap(comment.prefix, texts.join(' ')));
        changed++;
    }
    return { text: out.join(eol), changed };
}

let total = 0;
for (const root of roots) {
    for (const file of files(root)) {
        const source = readFileSync(file, 'utf8');
        const { text, changed } = rewrap(source, file.endsWith('.cs'));
        if (changed === 0 || text === source) continue;
        total += changed;
        console.log(`${file}: ${changed}`);
        if (!check) writeFileSync(file, text);
    }
}
console.log(`${total} comment paragraph(s) ${check ? 'to rewrap' : 'rewrapped'}`);
process.exit(check && total > 0 ? 1 : 0);
