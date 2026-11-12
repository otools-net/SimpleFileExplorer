// Measures the code against the size limits of agents/docs/development/readability.md: what is over each limit,
// worst first. Read-only, except --write-baseline. See README.md.
//
//   node agents/tools/readability/measure.mjs [--top 15] [--area server|client|...] [--json]
//   node agents/tools/readability/measure.mjs --check | --write-baseline [baseline.json]

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compare, debtsOf, readBaseline, totalsOf, writeBaseline } from './baseline.mjs';

const toolFolder = dirname(fileURLToPath(import.meta.url));
const repo = resolve(toolFolder, '../../..');
const defaultBaseline = join(toolFolder, 'baseline.json');

/** The limits of readability.md ("Sizes"). Change them there and here together. */
const LIMITS = {
    lineLength: 120,
    fileLines: 400,
    testFileLines: 800,
    functionLines: 60,
    parameters: 5,
    dependencies: 8,
    depth: 3,
    templateLines: 40,
    typesPerFile: 1,
};

const isSpec = file => file.endsWith('.spec.ts') || file.includes('/testing/');
const isServerCode = file => /^src\/.*\.cs$/.test(file) && !/Migrations\//.test(file);
const isClientCode = file => /^src\/Sfe\.Web\/src\/.*\.ts$/.test(file);

/** The areas measured. In tests, files may be longer and functions are not measured. */
const AREAS = [
    { name: 'server', language: 'cs', tests: false, match: isServerCode },
    { name: 'server tests', language: 'cs', tests: true, match: file => /^tests\/Sfe\.Tests\/.*\.cs$/.test(file) },
    { name: 'client', language: 'ts', tests: false, match: file => isClientCode(file) && !isSpec(file) },
    { name: 'client tests', language: 'ts', tests: true, match: file => isClientCode(file) && isSpec(file) },
    { name: 'e2e tests', language: 'ts', tests: true, match: file => /^tests\/e2e\/.*\.ts$/.test(file) },
    { name: 'styles', language: 'css', tests: false, match: file => /^src\/Sfe\.Web\/src\/.*\.css$/.test(file) },
];

const BASELINE_FLAGS = { '--check': 'check', '--write-baseline': 'writeBaseline' };

function parseArguments(argv) {
    const options = { top: 15, area: null, json: false, check: null, writeBaseline: null };
    for (let i = 0; i < argv.length; i++) {
        const flag = argv[i];
        if (flag === '--top') {
            options.top = Number(argv[++i]);
        } else if (flag === '--area') {
            options.area = argv[++i];
        } else if (flag === '--json') {
            options.json = true;
        } else if (flag in BASELINE_FLAGS) {
            // The path is optional: the flag alone means baseline.json next to this script.
            const hasPath = i + 1 < argv.length && !argv[i + 1].startsWith('--');
            options[BASELINE_FLAGS[flag]] = hasPath ? resolve(argv[++i]) : defaultBaseline;
        } else {
            throw new Error(`Unknown argument ${argv[i]} (--top N, --area NAME, --json, --check, --write-baseline)`);
        }
    }
    if ((options.check || options.writeBaseline) && options.area)
        throw new Error('--check and --write-baseline measure every area: leave out --area');
    return options;
}

function trackedFiles() {
    const output = execFileSync('git', ['ls-files', '-z'], { cwd: repo, encoding: 'utf8', maxBuffer: 64 << 20 });
    return output.split('\0').filter(Boolean);
}

function indentOf(line) {
    return line.length - line.trimStart().length;
}

/** The parameters between the parentheses of `text` opening at `open`: commas outside any inner bracket, plus one. */
function countParameters(text, open) {
    let depth = 0;
    let commas = 0;
    let empty = true;
    for (let i = open; i < text.length; i++) {
        const char = text[i];
        if ('([{<'.includes(char)) {
            depth++;
        } else if (')]}>'.includes(char)) {
            depth--;
            if (depth === 0)
                break;
        } else if (char === ',' && depth === 1) {
            commas++;
        } else if (char.trim()) {
            empty = false;
        }
    }
    return empty ? 0 : commas + 1;
}

/* ---------- C#: by layout (Allman braces, 4 spaces, file-scoped namespaces), so approximate ---------- */

const CS_TYPE = /\b(class|record|struct|interface|enum)\s+(\w+)/;
const CS_PRIMARY_CONSTRUCTOR = /\b(class|struct)\s+(\w+)(<[^>]*>)?\s*\(/;
const CS_STATEMENT_WORDS = [
    'if', 'else', 'for', 'foreach', 'while', 'do', 'switch', 'using', 'lock', 'try', 'catch', 'finally', 'fixed',
    'return', 'await', 'yield', 'throw', 'var', 'new',
];
const CS_STATEMENT = new RegExp(`^(${CS_STATEMENT_WORDS.join('|')})\\b`);

/**
 * The lines of a declaration ending just before the brace at `braceIndex`: back to the line at the brace's indent,
 * past the `)` that CSharpier puts at that indent after parameters one per line (`) : base(…)` too).
 */
function headerBefore(lines, braceIndex, indent) {
    let start = braceIndex - 1;
    while (start > 0 && (indentOf(lines[start]) > indent || !lines[start].trim() || lines[start].trim().startsWith(')')))
        start--;
    return lines.slice(start, braceIndex).map(line => line.trim()).join(' ');
}

function closingBrace(lines, braceIndex, indent) {
    const closing = ' '.repeat(indent) + '}';
    for (let i = braceIndex + 1; i < lines.length; i++) {
        if (lines[i].startsWith(closing))
            return i;
    }
    return lines.length - 1;
}

const CS_BLOCK_HEADER = /^(if|else|for|foreach|while|do|switch|try|catch|finally|using|lock|fixed|checked|unchecked)\b/;

/**
 * Blocks opened inside the body, deepest: statements (`if`, loops, `try`…), lambdas and local functions with a body.
 * Counted as blocks open at once, not by indentation (CSharpier indents a lambda inside wrapped arguments further than
 * its nesting); the braces of initializers, collections and switch expressions open no block.
 */
function braceDepth(lines, from, to) {
    const open = [];
    let deepest = 0;
    for (let i = from + 1; i < to; i++) {
        const text = lines[i].trim();
        if (text === '{') {
            const header = headerBefore(lines, i, indentOf(lines[i]));
            open.push(CS_BLOCK_HEADER.test(header) || header.endsWith('=>') || isMethodHeader(header));
            deepest = Math.max(deepest, open.filter(Boolean).length);
        } else if (text.startsWith('}') && open.length > 0) {
            open.pop();
        }
    }
    return deepest;
}

/** The header of a method (or constructor, local function): parentheses, and neither a type nor a statement. */
function isMethodHeader(header) {
    const open = header.indexOf('(');
    if (open < 0 || header.endsWith('=>') || CS_STATEMENT.test(header))
        return false;
    const beforeParameters = header.slice(0, open);
    return !CS_TYPE.test(beforeParameters) && !beforeParameters.includes('=');
}

/** What the class declared at `index` depends on: the parameters of its primary constructor. */
function primaryConstructor(file, lines, index) {
    const line = lines[index];
    const match = CS_PRIMARY_CONSTRUCTOR.exec(line);
    if (!match || indentOf(line) > 4 || line.trim().startsWith('//'))
        return null;
    const text = lines.slice(index, index + 40).join(' ');
    const open = text.indexOf('(', text.indexOf(match[2]));
    return { file, line: index + 1, name: match[2], count: countParameters(text, open) };
}

function methodAt(file, lines, braceIndex, indent) {
    const header = headerBefore(lines, braceIndex, indent);
    if (!isMethodHeader(header))
        return null;
    const open = header.indexOf('(');
    const end = closingBrace(lines, braceIndex, indent);
    return {
        file,
        line: braceIndex + 1,
        name: /([\w.]+)\s*(<[^(]*>)?\s*$/.exec(header.slice(0, open))?.[1] ?? '?',
        lines: end - braceIndex + 1,
        parameters: countParameters(header, open),
        depth: braceDepth(lines, braceIndex, end),
    };
}

function measureCSharp(file, lines) {
    const measured = { functions: [], types: [], constructors: [], templates: [] };
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const indent = indentOf(line);
        const type = indent === 0 && !line.startsWith('//') ? CS_TYPE.exec(line) : null;
        if (type)
            measured.types.push(type[2]);
        const constructor = primaryConstructor(file, lines, i);
        if (constructor)
            measured.constructors.push(constructor);
        // Members of a type (4 spaces) and of a type inside it (8).
        const method = line.trim() === '{' && (indent === 4 || indent === 8) ? methodAt(file, lines, i, indent) : null;
        if (method)
            measured.functions.push(method);
    }
    return measured;
}

/* ---------- TypeScript: by the compiler's syntax tree ---------- */

let typescript = null;

function loadTypeScript() {
    typescript ??= createRequire(join(repo, 'src/Sfe.Web/package.json'))('typescript');
    return typescript;
}

function functionName(ts, node) {
    if (node.name && ts.isIdentifier(node.name))
        return node.name.text;
    if (ts.isConstructorDeclaration(node))
        return 'constructor';
    const parent = node.parent;
    const isNamedByParent = ts.isVariableDeclaration(parent)
        || ts.isPropertyDeclaration(parent)
        || ts.isPropertyAssignment(parent);
    if (isNamedByParent && ts.isIdentifier(parent.name))
        return parent.name.text;
    if (ts.isCallExpression(parent) && ts.isIdentifier(parent.expression))
        return `${parent.expression.text}(…)`;
    if (ts.isCallExpression(parent) && ts.isPropertyAccessExpression(parent.expression))
        return `${parent.expression.name.text}(…)`;
    return '(anonymous)';
}

/** A `describe(...)` body is a file's outline, not a function someone reads from top to bottom. */
function isSuiteBody(ts, node) {
    const call = node.parent;
    return ts.isCallExpression(call) && /^(describe|test\.describe)\b/.test(call.expression.getText());
}

function nests(ts, node) {
    if (ts.isIfStatement(node)) {
        const isElseIf = ts.isIfStatement(node.parent) && node.parent.elseStatement === node;
        return !isElseIf;
    }
    return ts.isIterationStatement(node, false) || ts.isSwitchStatement(node) || ts.isTryStatement(node);
}

/** Control statements inside one another, deepest: `else if` stays at its `if`'s level, inner functions are skipped. */
function controlDepth(ts, body) {
    let deepest = 0;
    const visit = (node, depth) => {
        if (ts.isFunctionLike(node))
            return;
        const inner = nests(ts, node) ? depth + 1 : depth;
        deepest = Math.max(deepest, inner);
        ts.forEachChild(node, child => visit(child, inner));
    };
    ts.forEachChild(body, child => visit(child, 0));
    return deepest;
}

function isInjected(ts, member, source) {
    return ts.isPropertyDeclaration(member) && /^inject\(/.test(member.initializer?.getText(source) ?? '');
}

function measureTypeScript(file, text) {
    const ts = loadTypeScript();
    const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
    const lineOf = position => source.getLineAndCharacterOfPosition(position).line + 1;
    const startOf = node => lineOf(node.getStart(source));
    const measured = { functions: [], types: [], constructors: [], templates: [] };

    const visit = node => {
        if (ts.isFunctionLike(node) && node.body && ts.isBlock(node.body) && !isSuiteBody(ts, node)) {
            measured.functions.push({
                file,
                line: startOf(node),
                name: functionName(ts, node),
                lines: lineOf(node.end) - startOf(node) + 1,
                parameters: node.parameters.length,
                depth: controlDepth(ts, node.body),
            });
        }
        const isTemplate = ts.isPropertyAssignment(node)
            && node.name.getText(source) === 'template'
            && ts.isObjectLiteralExpression(node.parent);
        if (isTemplate) {
            const rows = node.initializer.getText(source).split('\n');
            const longest = Math.max(...rows.map(row => row.length));
            measured.templates.push({ file, line: startOf(node), lines: rows.length, longest });
        }
        if (ts.isClassDeclaration(node)) {
            const count = node.members.filter(member => isInjected(ts, member, source)).length;
            if (count)
                measured.constructors.push({ file, line: startOf(node), name: node.name?.text ?? '?', count });
        }
        ts.forEachChild(node, visit);
    };
    visit(source);
    return measured;
}

/* ---------- The report ---------- */

function measureArea(area, files) {
    const result = {
        name: area.name, tests: area.tests, lines: 0, longLines: 0,
        files: [], functions: [], constructors: [], templates: [], manyTypes: [],
    };
    for (const file of files) {
        const text = readFileSync(join(repo, file), 'utf8').replace(/\r\n/g, '\n');
        const lines = text.split('\n');
        if (lines.at(-1) === '')
            lines.pop();
        const lengths = lines.map(line => line.length);
        const longLines = lengths.filter(length => length > LIMITS.lineLength).length;
        result.files.push({ file, lines: lines.length, longLines, longest: Math.max(0, ...lengths) });
        result.lines += lines.length;
        result.longLines += longLines;
        if (area.language === 'css')
            continue;

        const measured = area.language === 'cs' ? measureCSharp(file, lines) : measureTypeScript(file, text);
        result.constructors.push(...measured.constructors);
        result.templates.push(...measured.templates);
        if (area.tests)
            continue;
        result.functions.push(...measured.functions);
        if (measured.types.length > LIMITS.typesPerFile)
            result.manyTypes.push({ file, count: measured.types.length });
    }
    return result;
}

/** What is over each limit in an area, worst first: a title, the items, and how one is shown. */
function findings(area) {
    const fileLimit = area.tests ? LIMITS.testFileLines : LIMITS.fileLines;
    const worst = (items, key, limit) => items.filter(item => item[key] > limit).sort((a, b) => b[key] - a[key]);
    const at = item => `${item.file}:${item.line}`;
    return [
        {
            title: `files > ${fileLimit} lines`,
            column: 'files over limit',
            items: worst(area.files, 'lines', fileLimit),
            show: file => `${file.lines}  ${file.file}`,
        },
        {
            title: `lines > ${LIMITS.lineLength} chars`,
            count: area.longLines,
            items: worst(area.files, 'longLines', 0),
            show: file => `${file.longLines} lines (longest ${file.longest})  ${file.file}`,
        },
        {
            title: `functions > ${LIMITS.functionLines} lines`,
            items: worst(area.functions, 'lines', LIMITS.functionLines),
            show: fn => `${fn.lines}  ${fn.name}  ${at(fn)}`,
        },
        {
            title: `functions nested > ${LIMITS.depth}`,
            items: worst(area.functions, 'depth', LIMITS.depth),
            show: fn => `${fn.depth}  ${fn.name}  ${at(fn)}`,
        },
        {
            title: `functions > ${LIMITS.parameters} params`,
            items: worst(area.functions, 'parameters', LIMITS.parameters),
            show: fn => `${fn.parameters}  ${fn.name}  ${at(fn)}`,
        },
        {
            title: `classes > ${LIMITS.dependencies} deps`,
            items: worst(area.constructors, 'count', LIMITS.dependencies),
            show: type => `${type.count}  ${type.name}  ${at(type)}`,
        },
        {
            title: `templates > ${LIMITS.templateLines} lines`,
            items: worst(area.templates, 'lines', LIMITS.templateLines),
            show: template => `${template.lines} lines (longest ${template.longest})  ${at(template)}`,
        },
        {
            title: 'files with several types',
            items: worst(area.manyTypes, 'count', 0),
            show: file => `${file.count}  ${file.file}`,
        },
    ];
}

function summarize(area) {
    const row = { area: area.name, files: area.files.length, lines: area.lines };
    for (const finding of findings(area))
        row[finding.column ?? finding.title] = finding.count ?? finding.items.length;
    return row;
}

function printTable(rows) {
    const columns = Object.keys(rows[0]);
    const widths = columns.map(column => Math.max(column.length, ...rows.map(row => String(row[column]).length)));
    const pad = (cell, i) => (i === 0 ? String(cell).padEnd(widths[i]) : String(cell).padStart(widths[i]));
    console.log(columns.map(pad).join('  '));
    console.log(widths.map(width => '-'.repeat(width)).join('  '));
    for (const row of rows)
        console.log(columns.map((column, i) => pad(row[column], i)).join('  '));
}

function printArea(area, top) {
    console.log(`\n=== ${area.name} ===`);
    for (const { title, items, show } of findings(area)) {
        if (!items.length)
            continue;
        console.log(`\n${title} (${items.length})`);
        for (const item of items.slice(0, top))
            console.log(`  ${show(item)}`);
        if (items.length > top)
            console.log(`  … ${items.length - top} more (--top ${items.length})`);
    }
}

/* ---------- The baseline (ratchet) ---------- */

const showChange = ({ file, measure, before, now }) => `  ${file}  ${measure} ${before} -> ${now}`;

/** Exits with 1 when a file is worse than in the baseline; says when some are better, to lock it in. */
function checkBaseline(areas, path) {
    if (!existsSync(path))
        throw new Error(`No baseline at ${path}: run --write-baseline first`);
    const { worse, better } = compare(debtsOf(areas, LIMITS), readBaseline(path));
    if (worse.length) {
        console.log(`Worse than the baseline (${worse.length}): bring these back within it, or, when a refactoring`
            + ' only moved the debt to other files, run --write-baseline and commit the change with it.');
        worse.forEach(change => console.log(showChange(change)));
        process.exitCode = 1;
    }
    if (better.length)
        console.log(`Better than the baseline (${better.length}): run --write-baseline to lock it in.`);
    if (!worse.length)
        console.log('No file is worse than the baseline.');
}

function writeNewBaseline(areas, path) {
    const debts = debtsOf(areas, LIMITS);
    const before = totalsOf(readBaseline(path));
    const after = totalsOf(debts);
    writeBaseline(path, debts);
    console.log(`Wrote ${path} (${debts.size} files over a limit). Totals, before -> after:`);
    for (const measure of Object.keys(after)) {
        const grew = after[measure] > before[measure] ? '   <- grew' : '';
        console.log(`  ${measure.padEnd(24)} ${before[measure]} -> ${after[measure]}${grew}`);
    }
}

function main() {
    const options = parseArguments(process.argv.slice(2));
    const selected = AREAS.filter(area => !options.area || area.name === options.area);
    if (!selected.length)
        throw new Error(`No area named ${options.area}: ${AREAS.map(area => area.name).join(', ')}`);

    const files = trackedFiles();
    const areas = selected.map(area => measureArea(area, files.filter(area.match)));
    if (options.check) {
        checkBaseline(areas, options.check);
        return;
    }
    if (options.writeBaseline) {
        writeNewBaseline(areas, options.writeBaseline);
        return;
    }
    if (options.json) {
        console.log(JSON.stringify({ limits: LIMITS, areas }, null, 2));
        return;
    }
    printTable(areas.map(summarize));
    if (options.top > 0)
        areas.forEach(area => printArea(area, options.top));
}

main();
