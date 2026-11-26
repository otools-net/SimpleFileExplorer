// What the client's initial bundle is made of: bytes per package / source folder and per file, from the stats file
// of a build (`ng build --stats-json`). Usage: node breakdown.mjs <out>/browser-stats.json [--top 40]
import fs from 'node:fs';

const [statsPath, ...rest] = process.argv.slice(2);
if (!statsPath) {
    console.error('Usage: node breakdown.mjs <out>/browser-stats.json [--top 40]');
    process.exit(1);
}
const top = Number(rest[rest.indexOf('--top') + 1]) || 40;
const { outputs } = JSON.parse(fs.readFileSync(statsPath, 'utf8'));
const slashes = (s) => s.split('\\').join('/');

// The initial files: main and polyfills, and the chunks they import statically (dynamic imports are lazy chunks).
const initial = new Set();
const queue = Object.keys(outputs).filter((k) => /(^|\/)(main|polyfills)-[^/]+\.js$/.test(slashes(k)));
while (queue.length) {
    const file = queue.pop();
    if (initial.has(file)) continue;
    initial.add(file);
    for (const i of outputs[file].imports ?? []) if (i.kind === 'import-statement' && outputs[i.path]) queue.push(i.path);
}

const byGroup = {};
const byFile = {};
let packages = 0;
let app = 0;
for (const file of initial)
    for (const [input, { bytesInOutput }] of Object.entries(outputs[file].inputs)) {
        const name = slashes(input);
        const pkg = name.match(/node_modules\/((@[^/]+\/)?[^/]+)/)?.[1];
        const group = pkg ?? name.split('/').slice(0, 4).join('/');
        byGroup[group] = (byGroup[group] ?? 0) + bytesInOutput;
        byFile[name] = (byFile[name] ?? 0) + bytesInOutput;
        if (pkg) packages += bytesInOutput;
        else app += bytesInOutput;
    }

const kB = (n) => `${(n / 1024).toFixed(1).padStart(8)} kB`;
const list = (o) =>
    Object.entries(o)
        .sort((a, b) => b[1] - a[1])
        .slice(0, top)
        .map(([k, v]) => `${kB(v)}  ${k}`)
        .join('\n');
console.log(`Initial files: ${[...initial].map(slashes).join(', ')}`);
console.log(`Packages ${kB(packages).trim()}, app ${kB(app).trim()} (before the final minification: compare, don't add)`);
console.log(`\n== By package / source folder ==\n${list(byGroup)}`);
console.log(`\n== By file ==\n${list(byFile)}`);
