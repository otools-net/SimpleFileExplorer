// Checks the relative links, images and #anchors of README.md, AGENTS.md, agents/docs/ and docs/, and lists unused images.
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import path from 'node:path';
const root = process.argv[2] ?? fileURLToPath(new URL('../../..', import.meta.url));
const files = [path.join(root, 'README.md'), path.join(root, 'AGENTS.md')];
function walk(d) { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (p.endsWith('.md')) files.push(p); } }
walk(path.join(root, 'docs')); walk(path.join(root, 'agents/docs'));
const slug = h => h.trim().toLowerCase().replace(/[`*_]/g, '').replace(/[^\p{L}\p{N} -]/gu, '').replace(/ /g, '-');
const anchors = f => new Set(fs.readFileSync(f, 'utf8').split('\n').filter(l => /^#{1,6} /.test(l)).map(l => slug(l.replace(/^#+ /, ''))));
let bad = 0; const usedImages = new Set();
for (const f of files) {
    const text = fs.readFileSync(f, 'utf8').replace(/```[\s\S]*?```/g, '');
    for (const m of text.matchAll(/\]\(([^)\s]+)\)|<img src="([^"]+)"/g)) {
        const link = m[1] ?? m[2];
        if (/^(https?:|mailto:)/.test(link)) continue;
        const [p, a] = link.split('#');
        const target = p ? path.resolve(path.dirname(f), p) : f;
        if (!fs.existsSync(target)) { console.log('MISSING', path.relative(root, f), link); bad++; continue; }
        if (/\.png$/.test(target)) usedImages.add(path.resolve(target));
        if (a && target.endsWith('.md') && !anchors(target).has(a)) { console.log('ANCHOR', path.relative(root, f), link); bad++; }
    }
}
for (const img of fs.readdirSync(path.join(root, 'docs/images'))) if (!usedImages.has(path.resolve(root, 'docs/images', img))) console.log('UNUSED', img);
console.log(bad ? bad + ' problems' : 'all links ok', files.length, 'files');
