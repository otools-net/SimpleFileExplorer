// Renames the C# namespaces Sfe.Core / Sfe.Api / Sfe.Tests to Otools.Net.Sfe.* in .cs files, touching code only:
// namespace and using lines, qualified names in code, and cref="…" in comments. String literals (data protection
// purposes, log categories, file names) and plain comment text are left as they are, except the entity names of the
// EF model snapshots (*ModelSnapshot.cs), which name types. Usage: node rename.mjs <folder>... [--dry-run]
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const NEW_PREFIX = 'Otools.Net.';
const BACKSLASH = String.fromCharCode(92);
const CODE_NAME = /(?<![\w.@"])Sfe\.(?=(?:Core|Api|Tests)\b)/g;
const CREF_NAME = /(cref="(?:[TMPFE]:)?)Sfe\.(?=(?:Core|Api|Tests)\b)/g;
const STRING_NAME = /^(@?")Sfe\.(?=Core\.)/;

const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const roots = args.filter(a => !a.startsWith('--'));
if (roots.length === 0) {
    console.error('Usage: node rename.mjs <folder>... [--dry-run]');
    process.exit(2);
}

let changed = 0;
for (const file of roots.flatMap(listSources)) {
    const text = readFileSync(file, 'utf8');
    const snapshot = file.endsWith('ModelSnapshot.cs');
    const segments = lex(text);
    if (segments.map(s => s.text).join('') !== text) throw new Error(`Lexing lost text in ${file}`);
    const result = segments.map(s => rewrite(s, snapshot)).join('');
    if (result !== text) {
        changed++;
        if (!dryRun) writeFileSync(file, result, 'utf8');
    }
}
console.log(`${changed} file(s) ${dryRun ? 'would change' : 'changed'}`);

function listSources(folder) {
    return readdirSync(folder).flatMap(name => {
        const path = join(folder, name);
        if (statSync(path).isDirectory()) return ['bin', 'obj', 'node_modules'].includes(name) ? [] : listSources(path);
        return name.endsWith('.cs') ? [path] : [];
    });
}

function rewrite(segment, snapshot) {
    if (segment.kind === 'code') return segment.text.replace(CODE_NAME, NEW_PREFIX + 'Sfe.');
    if (segment.kind === 'comment') return segment.text.replace(CREF_NAME, `$1${NEW_PREFIX}Sfe.`);
    if (snapshot) return segment.text.replace(STRING_NAME, `$1${NEW_PREFIX}Sfe.`);
    return segment.text;
}

/** Splits C# source into code, comment and string segments (interpolation holes are code). */
function lex(text) {
    const segments = [];
    let i = 0;
    let codeStart = 0;
    const flushCode = () => {
        if (i > codeStart) segments.push({ kind: 'code', text: text.slice(codeStart, i) });
    };
    const take = (kind, end) => {
        flushCode();
        segments.push({ kind, text: text.slice(i, end) });
        i = end;
        codeStart = i;
    };
    const lexCode = inHole => {
        let depth = 0;
        while (i < text.length) {
            const c = text[i];
            const next = text[i + 1];
            if (c === '/' && next === '/') {
                const end = text.indexOf('\n', i);
                take('comment', end < 0 ? text.length : end);
            } else if (c === '/' && next === '*') {
                const end = text.indexOf('*/', i + 2);
                take('comment', end < 0 ? text.length : end + 2);
            } else if (c === "'") {
                let j = i + 1;
                while (j < text.length && text[j] !== "'") j += text[j] === BACKSLASH ? 2 : 1;
                take('string', Math.min(j + 1, text.length));
            } else if (c === '"' || ((c === '$' || c === '@') && /^[$@]+"/.test(text.slice(i, i + 4)))) {
                lexString();
            } else if (inHole && c === '{') {
                depth++;
                i++;
            } else if (inHole && c === '}') {
                if (depth === 0) {
                    flushCode();
                    codeStart = i;
                    return;
                }
                depth--;
                i++;
            } else {
                i++;
            }
        }
        flushCode();
        codeStart = i;
    };
    const lexString = () => {
        const prefix = /^[$@]*/.exec(text.slice(i))[0];
        const interpolated = prefix.includes('$');
        const verbatim = prefix.includes('@');
        const quotes = /^"*/.exec(text.slice(i + prefix.length))[0].length;
        if (quotes >= 3) {
            const fence = '"'.repeat(quotes);
            const end = text.indexOf(fence, i + prefix.length + quotes);
            take('string', end < 0 ? text.length : end + quotes);
            return;
        }
        let j = i + prefix.length + 1;
        while (j < text.length) {
            const c = text[j];
            if (verbatim && c === '"' && text[j + 1] === '"') j += 2;
            else if (!verbatim && c === BACKSLASH) j += 2;
            else if (c === '"') break;
            else if (interpolated && c === '{' && text[j + 1] === '{') j += 2;
            else if (interpolated && c === '{') {
                take('string', j + 1);
                lexCode(true);
                j = i;
            } else j++;
        }
        take('string', Math.min(j + 1, text.length));
    };
    lexCode(false);
    return segments;
}
