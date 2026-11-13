#!/usr/bin/env node
// Moves Angular inline templates over 40 lines into `.html` files next to their component (`templateUrl`), the
// template unchanged: the file holds the template string's value (escapes such as \` resolved), its common
// indentation removed unless it has <pre>, <textarea> or ngPreserveWhitespaces (Angular collapses the rest of the
// whitespace anyway). Templates with ${…} are reported and left. See README.md.
//
//   node agents/tools/readability/move-templates.mjs [--dry-run] [--min <lines>] <file or folder>...
import { readFileSync, writeFileSync, existsSync, statSync, readdirSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const ts = createRequire(join(repo, 'src', 'Sfe.Web', 'package.json'))('typescript');

const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const minAt = args.indexOf('--min');
const MIN_LINES = minAt >= 0 ? Number(args[minAt + 1]) : 41;
const roots = args.filter((a, i) => !a.startsWith('--') && (minAt < 0 || i !== minAt + 1));
if (roots.length === 0) {
    console.error('usage: move-templates.mjs [--dry-run] [--min <lines>] <file or folder>...');
    process.exit(2);
}

function* files(path) {
    if (/node_modules/.test(path)) return;
    if (statSync(path).isDirectory()) {
        for (const name of readdirSync(path)) yield* files(join(path, name));
    } else if (path.endsWith('.ts') && !path.endsWith('.spec.ts')) {
        yield path;
    }
}

const kebab = (name) => name.replace(/Component$/, '').replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

/**
 * The text without the indentation all its non-empty lines share; leading and trailing blank lines dropped. Angular
 * collapses a run of two or more whitespace characters into one space but keeps a lone newline: where text meets a
 * line that would start at column 0 (`themselves.` then `</p>`), 4 spaces of indentation stay, so that the run between
 * them still collapses to a space.
 */
function dedent(text) {
    const lines = text.replace(/^\s*\n/, '').replace(/\n\s*$/, '').split('\n');
    const indentOf = (l) => /^ */.exec(l)[0].length;
    const common = Math.min(...lines.filter((l) => l.trim()).map(indentOf));
    const textAtColumnZero = lines.some(
        (l, i) =>
            i > 0 &&
            l.trim() &&
            indentOf(l) === common &&
            (!/^\s*[<@}]/.test(l) || !/(>|^\s*)$/.test(lines[i - 1])),
    );
    const remove = textAtColumnZero ? Math.max(0, common - 4) : common;
    return lines.map((l) => l.slice(Math.min(remove, indentOf(l)))).join('\n');
}

let moved = 0;
for (const root of roots) {
    for (const file of files(root)) {
        const source = readFileSync(file, 'utf8');
        if (!source.includes('template:')) continue;
        const sf = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true);
        // Every @Component({ template: `…` }) of the file, with its class.
        const found = [];
        const visit = (node) => {
            if (ts.isClassDeclaration(node)) {
                for (const decorator of ts.getDecorators(node) ?? []) {
                    const call = decorator.expression;
                    if (!ts.isCallExpression(call) || call.expression.getText() !== 'Component') continue;
                    const object = call.arguments[0];
                    if (!object || !ts.isObjectLiteralExpression(object)) continue;
                    const property = object.properties.find(
                        (p) => ts.isPropertyAssignment(p) && p.name.getText() === 'template',
                    );
                    if (property) found.push({ name: node.name.text, property });
                }
            }
            ts.forEachChild(node, visit);
        };
        visit(sf);
        const components = found.length;
        const edits = [];
        for (const { name, property } of found) {
            const literal = property.initializer;
            const lines = literal.getText().split('\n').length;
            if (lines < MIN_LINES) continue;
            if (!ts.isNoSubstitutionTemplateLiteral(literal) && !ts.isStringLiteral(literal)) {
                console.error(`${file}: ${name}'s template has \${…}: left`);
                continue;
            }
            const html =
                components === 1 ? basename(file, '.ts') + '.html' : `${kebab(name)}.component.html`;
            const target = join(dirname(file), html);
            if (existsSync(target)) {
                console.error(`${file}: ${html} exists already: ${name} left`);
                continue;
            }
            const value = literal.text;
            const keep = /<pre[\s>]|<textarea[\s>]|ngPreserveWhitespaces/.test(value);
            const content = (keep ? value : dedent(value)).replace(/\s+$/, '') + '\n';
            console.log(`${file}: ${name} (${lines} lines) -> ${html}`);
            moved++;
            if (dryRun) continue;
            writeFileSync(target, content);
            edits.push([property.getStart(), property.getEnd(), `templateUrl: './${html}'`]);
        }
        if (edits.length === 0 || dryRun) continue;
        let result = source;
        for (const [start, end, text] of edits.sort((a, b) => b[0] - a[0]))
            result = result.slice(0, start) + text + result.slice(end);
        writeFileSync(file, result);
    }
}
console.log(`${moved} template(s) ${dryRun ? 'to move' : 'moved'}`);
