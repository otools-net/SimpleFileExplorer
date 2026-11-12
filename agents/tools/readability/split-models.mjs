#!/usr/bin/env node
// Splits the client's `core/models.ts` by area (readability plan, phase 3): each declaration goes to the models file
// of the one area that uses it (`tags/tag.models.ts`…), or stays in `core/models.ts` when several areas (or `core/`
// itself) use it. `core/api.service.ts`, which carries every type to the server, is not counted as a use. A type that
// stays in core keeps what it refers to in core too, so core never depends on an area. Text unchanged (doc comments
// move with their declaration); each new file imports what it refers to; `core/models.ts` re-exports the new files
// (`export * from`), so no importer changes yet. See README.md.
//
//   node agents/tools/readability/split-models.mjs            # prints the plan
//   node agents/tools/readability/split-models.mjs --write    # writes the files
//   node agents/tools/readability/split-models.mjs --imports  # importers take each name from its file
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname, relative, sep } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const web = join(repo, 'src', 'Sfe.Web');
const app = join(web, 'src', 'app');
const ts = createRequire(join(web, 'package.json'))('typescript');
const modelsPath = join(app, 'core', 'models.ts');
const write = process.argv.includes('--write');

// File name of each area's models (singular where the folder is a plural).
const FILE_OF_AREA = {
    access: 'access.models.ts',
    account: 'account.models.ts',
    admin: 'admin.models.ts',
    editor: 'editor.models.ts',
    files: 'file.models.ts',
    formats: 'format.models.ts',
    library: 'library.models.ts',
    logs: 'log.models.ts',
    menus: 'menu.models.ts',
    migration: 'migration.models.ts',
    player: 'player.models.ts',
    settings: 'settings.models.ts',
    shared: 'shared.models.ts',
    tags: 'tag.models.ts',
    tasks: 'task.models.ts',
    tracking: 'tracking.models.ts',
    trickplay: 'trickplay.models.ts',
};

if (process.argv.includes('--imports')) {
    retargetImports();
    process.exit(0);
}

/**
 * Second step: every `import { … } from '…/core/models'` becomes imports from the files the names live in now (one
 * declaration per file, `type` kept, aliases kept). Files are found from the declarations of `core/models.ts` and the
 * files it re-exports.
 */
function retargetImports() {
    const modelsText = readFileSync(modelsPath, 'utf8');
    const modelsSf = ts.createSourceFile(modelsPath, modelsText, ts.ScriptTarget.Latest, true);
    const where = new Map();
    const declare = (file, fileSf) => {
        for (const s of fileSf.statements) if (s.name && !ts.isImportDeclaration(s)) where.set(s.name.text, file);
    };
    declare(modelsPath, modelsSf);
    for (const s of modelsSf.statements) {
        if (!ts.isExportDeclaration(s) || s.exportClause) continue;
        const file = join(dirname(modelsPath), s.moduleSpecifier.text) + '.ts';
        declare(file, ts.createSourceFile(file, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true));
    }
    let changed = 0;
    for (const file of tsFilesOf(join(web, 'src'))) {
        if (file === modelsPath || where.has(file)) continue;
        const text = readFileSync(file, 'utf8');
        if (!text.includes('models')) continue;
        const fileSf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
        const edits = [];
        for (const statement of fileSf.statements) {
            if (!ts.isImportDeclaration(statement)) continue;
            const from = statement.moduleSpecifier.text;
            if (!from.startsWith('.') || join(dirname(file), from) + '.ts' !== modelsPath) continue;
            const clause = statement.importClause;
            const byFile = new Map();
            for (const element of clause.namedBindings.elements) {
                const name = (element.propertyName ?? element.name).text;
                const target = where.get(name);
                if (!target) throw new Error(`${file}: ${name} is not in core/models or its files`);
                if (!byFile.has(target)) byFile.set(target, []);
                byFile.get(target).push(element.getText());
            }
            const lines = [...byFile]
                .map(([target, names]) => {
                    let spec = relative(dirname(file), target.replace(/\.ts$/, '')).split(sep).join('/');
                    if (!spec.startsWith('.')) spec = './' + spec;
                    return `import ${clause.isTypeOnly ? 'type ' : ''}{ ${names.join(', ')} } from '${spec}';`;
                })
                .sort((a, b) => a.slice(a.indexOf(' from ')).localeCompare(b.slice(b.indexOf(' from '))));
            edits.push([statement.getStart(), statement.getEnd(), lines.join('\n')]);
        }
        if (edits.length === 0) continue;
        let result = text;
        for (const [start, end, replacement] of edits.reverse())
            result = result.slice(0, start) + replacement + result.slice(end);
        writeFileSync(file, result);
        changed++;
    }
    console.log(`${changed} file(s) import from the area files`);
}

function* tsFilesOf(dir) {
    for (const name of readdirSync(dir)) {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) yield* tsFilesOf(path);
        else if (path.endsWith('.ts')) yield path;
    }
}

const source = readFileSync(modelsPath, 'utf8');
const sf = ts.createSourceFile(modelsPath, source, ts.ScriptTarget.Latest, true);

/** The declarations of models.ts in order: name, text with its doc comment, names it refers to. */
const declarations = [];
for (const statement of sf.statements) {
    if (ts.isImportDeclaration(statement)) continue;
    const name = statement.name?.text;
    if (!name) throw new Error(`models.ts: a statement without a name at ${statement.getStart()}`);
    const refs = new Set();
    const visit = (node) => {
        if (ts.isIdentifier(node) && node !== statement.name) refs.add(node.text);
        ts.forEachChild(node, visit);
    };
    visit(statement);
    // From the end of the previous statement: the doc comment and blank lines before this one.
    const text = source.slice(statement.getFullStart(), statement.getEnd()).replace(/^\s*\n/, '');
    declarations.push({ name, text, refs, exported: true, isValue: ts.isFunctionDeclaration(statement) });
}
const byName = new Map(declarations.map((d) => [d.name, d]));
for (const d of declarations) d.refs = new Set([...d.refs].filter((r) => byName.has(r) && r !== d.name));

/** Every .ts file of the client but models.ts. */
function* tsFiles(dir) {
    for (const name of readdirSync(dir)) {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) yield* tsFiles(path);
        else if (path.endsWith('.ts') && path !== modelsPath) yield path;
    }
}

/** Area of a file: its first folder under app/ ('core', 'tags'…), or 'root' for app/*.ts and src/testing. */
const areaOf = (file) => {
    const parts = relative(app, file).split(sep);
    return parts.length > 1 && !parts[0].startsWith('..') ? parts[0] : 'root';
};

// Who imports what from core/models.
const users = new Map(declarations.map((d) => [d.name, new Set()]));
for (const file of tsFiles(join(web, 'src'))) {
    if (file.endsWith(join('core', 'api.service.ts'))) continue;
    const text = readFileSync(file, 'utf8');
    if (!text.includes('models')) continue;
    const fileSf = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
    for (const statement of fileSf.statements) {
        if (!ts.isImportDeclaration(statement)) continue;
        const from = statement.moduleSpecifier.text;
        if (!from.startsWith('.') || join(dirname(file), from) + '.ts' !== modelsPath) continue;
        for (const element of statement.importClause?.namedBindings?.elements ?? [])
            users.get((element.propertyName ?? element.name).text)?.add(areaOf(file));
    }
}

// Where each declaration goes: its one area, else core. Then core keeps what core refers to, until stable.
const home = new Map();
for (const d of declarations) {
    const areas = users.get(d.name);
    const [only] = areas;
    home.set(d.name, areas.size === 1 && FILE_OF_AREA[only] ? only : 'core');
}
for (let changed = true; changed;) {
    changed = false;
    for (const d of declarations) {
        if (home.get(d.name) !== 'core') continue;
        for (const r of d.refs) {
            if (home.get(r) !== 'core') {
                home.set(r, 'core');
                changed = true;
            }
        }
    }
}

// What stays in core is shared; by subject, so that no core file passes 400 lines. Matched in order; anything else
// stays in core/models.ts.
const CORE_FILES = [
    ['tag', /^(Tag|FileTag|ScopeTag|InheritedDefaultTag|FolderDefault)/],
    ['archive', /^Archive(?!Link)/],
    ['task', /^(Task|Scan|Trickplay|Duplicate|RemovedDuplicate|SimilarVideo|RelinkPass)/],
    ['setting', /^Setting/],
    [
        'access',
        /^(UserKind|Session|FolderRule|FolderAccessEntry|FolderLevel|Membership|UserInput|GroupInput|Tenant|Migration|ChangelogEntry|Installation|ServerMode|SetupInput)/,
    ],
    [
        'config',
        /^(Theme|ClientConfig|StreamProbe|KeepAlive|StreamingMode|EditorConfig|EditedImage|SavedImage|SaveImageResult|TextVersion|SubtitleVideo|SavedText|SaveTextResult)$/,
    ],
];
for (const d of declarations) {
    if (home.get(d.name) !== 'core') continue;
    const match = CORE_FILES.find(([, pattern]) => pattern.test(d.name));
    if (match) home.set(d.name, `core:${match[0]}`);
}
const isCore = (area) => area === 'core' || area.startsWith('core:');
const pathOf = (area) =>
    area === 'core'
        ? join(app, 'core', 'models')
        : isCore(area)
          ? join(app, 'core', `${area.slice(5)}.models`)
          : join(app, area, FILE_OF_AREA[area].replace(/\.ts$/, ''));

const groups = new Map();
for (const d of declarations) {
    const area = home.get(d.name);
    if (!groups.has(area)) groups.set(area, []);
    groups.get(area).push(d);
}
for (const [area, list] of groups) {
    const lines = list.reduce((n, d) => n + d.text.split('\n').length + 1, 0);
    console.log(
        `${area.padEnd(10)} ${String(list.length).padStart(3)} declarations, ~${lines} lines: ${list.map((d) => d.name).join(', ')}`,
    );
}
if (!write) process.exit(0);

/** The import lines a file of `area` needs for the declarations it holds. */
function importsFor(area, list, fromDir) {
    const needed = new Map();
    for (const d of list)
        for (const r of d.refs) {
            const target = home.get(r);
            if (target === area) continue;
            if (!needed.has(target)) needed.set(target, new Set());
            needed.get(target).add(r);
        }
    return [...needed]
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([target, names]) => {
            let spec = relative(fromDir, pathOf(target)).split(sep).join('/');
            if (!spec.startsWith('.')) spec = './' + spec;
            const anyValue = [...names].some((n) => byName.get(n).isValue);
            return `import ${anyValue ? '' : 'type '}{ ${[...names].sort().join(', ')} } from '${spec}';`;
        });
}

const coreList = groups.get('core') ?? [];
const reexports = [];
for (const [area, list] of groups) {
    if (area === 'core') continue;
    const file = pathOf(area) + '.ts';
    const dir = dirname(file);
    const imports = importsFor(area, list, dir);
    const body = list.map((d) => d.text).join('\n\n');
    writeFileSync(file, (imports.length ? imports.join('\n') + '\n\n' : '') + body + '\n');
    reexports.push(
        `export * from '${relative(join(app, 'core'), pathOf(area))
            .split(sep)
            .join('/')
            .replace(/^(?!\.)/, './')}';`,
    );
}
const coreImports = importsFor('core', coreList, join(app, 'core'));
const head = source.slice(0, sf.statements.find((s) => !ts.isImportDeclaration(s)).getFullStart()).trimEnd();
writeFileSync(
    modelsPath,
    [head, ...coreImports].filter(Boolean).join('\n') +
        '\n\n' +
        coreList.map((d) => d.text).join('\n\n') +
        '\n\n// The models of each area, until their importers take them from there (readability plan, phase 3).\n' +
        reexports.sort().join('\n') +
        '\n',
);
console.log(`written: core/models.ts and ${reexports.length} area files`);
