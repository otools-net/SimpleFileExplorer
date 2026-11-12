#!/usr/bin/env node
// Splits TypeScript string literals that make a line too long into `'a ' + 'b'` at a space, the text unchanged:
// '…', "…" and the literal parts of `…${…}…` templates, found with the compiler's syntax tree. Left: imports, object
// keys, literal types, `as`/`satisfies`, tagged templates, a member access on the literal, enum members, and
// Angular's inline `template:` / `styles:` (moved to .html files, readability plan phase 4). Prettier lays the `+`
// chains out after it; the two run in turns until nothing changes. See README.md.
//
//   node agents/tools/readability/split-strings-ts.mjs [--check] <file or folder>...
import { readFileSync, writeFileSync, statSync, readdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const LIMIT = 120;
const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const web = join(repo, 'src', 'Sfe.Web');
const ts = createRequire(join(web, 'package.json'))('typescript');
const SKIPPED = /(^|[\\/])(node_modules|dist|out-tsc|public)([\\/]|$)/;

const args = process.argv.slice(2);
const check = args.includes('--check');
const roots = args.filter((a) => a !== '--check');
if (roots.length === 0) {
    console.error('usage: split-strings-ts.mjs [--check] <file or folder>...');
    process.exit(2);
}

function* files(path) {
    if (SKIPPED.test(path)) return;
    if (statSync(path).isDirectory()) {
        for (const name of readdirSync(path)) yield* files(join(path, name));
    } else if (/\.(ts|mts)$/.test(path) && !path.endsWith('.d.ts')) {
        yield path;
    }
}

/** Whether `'a b'` may become `'a ' + 'b'` where the node stands. */
function splittable(node) {
    const parent = node.parent;
    if (ts.isImportDeclaration(parent) || ts.isExportDeclaration(parent) || ts.isExternalModuleReference(parent))
        return false;
    if (ts.isLiteralTypeNode(parent) || ts.isEnumMember(parent) || ts.isTaggedTemplateExpression(parent)) return false;
    if (ts.isAsExpression(parent) || ts.isSatisfiesExpression(parent)) return false;
    if ((ts.isPropertyAccessExpression(parent) || ts.isElementAccessExpression(parent)) && parent.expression === node)
        return false;
    if (ts.isPropertyAssignment(parent) && parent.name === node) return false;
    if (ts.isPropertyAssignment(parent) && /^(template|styles?)$/.test(parent.name.getText())) return false;
    if (ts.isCallExpression(parent) && parent.expression.getText() === 'require') return false;
    // A string inside a template's `${…}`: its line stays long whatever it does (the template around it is split).
    for (let up = parent; up; up = up.parent) if (ts.isTemplateSpan(up)) return false;
    return true;
}

/**
 * The literal pieces of a string node where a split may go: `[start, end]` offsets of text between the quotes, or
 * between a template's `${…}` holes.
 */
function pieces(node, source) {
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))
        return [[node.getStart() + 1, node.getEnd() - 1]];
    const parts = [node.head, ...node.templateSpans.map((s) => s.literal)];
    // head: `…${   middle: }…${   tail: }…`
    return parts.map((p) => [p.getStart() + 1, p.getEnd() - (ts.isTemplateTail(p) ? 1 : 2)]);
}

/** One split in the file (the first long line that can take one), or null. */
function splitOnce(file, source) {
    const sf = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true);
    const lineStarts = sf.getLineStarts();
    const longLines = new Set();
    source.split('\n').forEach((l, i) => l.replace(/\r$/, '').length > LIMIT && longLines.add(i));
    if (longLines.size === 0) return null;
    let result = null;
    const visit = (node) => {
        if (result) return;
        const isString =
            ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateExpression(node);
        if (isString && splittable(node)) {
            // Spaces of every literal piece on a long line, from column 50 to where the first part still fits (its
            // space, a quote and ` +`): the split goes at the last one, or after a punctuation mark close to it.
            const spaces = [];
            for (const [start, end] of pieces(node, source)) {
                const line = sf.getLineAndCharacterOfPosition(start).line;
                if (!longLines.has(line) || sf.getLineAndCharacterOfPosition(end).line !== line) continue;
                const lineStart = lineStarts[line];
                for (let p = start + 1; p < end - 1; p++) {
                    const column = p - lineStart;
                    if (source[p] === ' ' && column >= 50 && column + 4 <= LIMIT) spaces.push(p);
                }
            }
            if (spaces.length > 0) {
                const last = spaces[spaces.length - 1];
                const at = spaces.filter((s) => /[;,.:)?!]/.test(source[s - 1]) && last - s <= 30).pop() ?? last;
                const quote = ts.isStringLiteral(node) ? source[node.getStart()] : '`';
                result = source.slice(0, at + 1) + quote + ' + ' + quote + source.slice(at + 1);
                return;
            }
        }
        ts.forEachChild(node, visit);
    };
    visit(sf);
    return result;
}

const prettier = join(web, 'node_modules', '.bin', process.platform === 'win32' ? 'prettier.cmd' : 'prettier');
let total = 0;
for (const root of roots) {
    for (const file of files(root)) {
        let source = readFileSync(file, 'utf8');
        let count = 0;
        for (;;) {
            const next = splitOnce(file, source);
            if (!next || count > 200) break;
            count++;
            source = next;
            if (check) break;
            writeFileSync(file, source);
            execFileSync(prettier, ['--write', '--log-level', 'warn', resolve(file)], { cwd: web, shell: true });
            source = readFileSync(file, 'utf8');
        }
        if (count === 0) continue;
        total += count;
        console.log(`${file}: ${count}`);
    }
}
console.log(`${total} split(s) ${check ? 'to make' : 'made'}`);
process.exit(check && total > 0 ? 1 : 0);
