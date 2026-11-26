// Why a file is in the client's initial bundle: the chain of static imports from src/main.ts to it, from the stats
// file of a build (`ng build --stats-json`). A file reached only through import() or @defer is "not initial".
// Usage: node why.mjs <out>/browser-stats.json <path end>... (e.g. files/properties.dialog.ts, @use-gesture/core)
import fs from 'node:fs';

const [statsPath, ...targets] = process.argv.slice(2);
if (!statsPath || !targets.length) {
    console.error('Usage: node why.mjs <out>/browser-stats.json <path end>...');
    process.exit(1);
}
const slashes = (s) => s.split('\\').join('/');
const { inputs } = JSON.parse(fs.readFileSync(statsPath, 'utf8'));
const imports = {};
for (const [name, input] of Object.entries(inputs))
    imports[slashes(name)] = (input.imports ?? []).filter((i) => i.kind === 'import-statement').map((i) => slashes(i.path));

// Breadth first: each file's parent on a shortest chain of static imports.
const start = Object.keys(imports).find((k) => k.endsWith('src/main.ts'));
const parent = { [start]: null };
const queue = [start];
while (queue.length) {
    const file = queue.shift();
    for (const next of imports[file] ?? [])
        if (!(next in parent)) {
            parent[next] = file;
            queue.push(next);
        }
}

for (const target of targets) {
    const found = Object.keys(parent).find((k) => k.includes(target));
    if (!found) {
        console.log(`${target}: not initial`);
        continue;
    }
    const chain = [];
    for (let file = found; file; file = parent[file]) chain.unshift(file.replace(/^src\/app\//, ''));
    console.log(`${target}:\n    ${chain.join('\n -> ')}`);
}
