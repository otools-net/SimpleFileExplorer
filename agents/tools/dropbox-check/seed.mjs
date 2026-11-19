#!/usr/bin/env node
// Fills a connection with a tree of small files through SFE's own upload routes, so the index check of
// README.md ("Index of a large Dropbox") has folders and pages to list on a test account. Writes only under
// --root (default "sfe-seed"); remove that folder to undo. Usage in README.md.
import { readFile } from 'node:fs/promises';
import { basename } from 'node:path';

const args = Object.fromEntries(
  process.argv.slice(2).map((a) => {
    const [k, ...v] = a.replace(/^--/, '').split('=');
    return [k, v.length ? v.join('=') : 'true'];
  }),
);
const base = (args.url ?? 'http://localhost:5080').replace(/\/$/, '') + '/api/v1';
const token = process.env.SFE_TOKEN;
const tenant = args.tenant;
const connection = Number(args.connection ?? 1);
const root = args.root ?? 'sfe-seed';
const folders = Number(args.folders ?? 50);
const perFolder = Number(args.files ?? 20);
const bigFolder = Number(args.big ?? 0);
const depth = Number(args.depth ?? 0);
const parallel = Number(args.parallel ?? 4);
const extra = args.add ? args.add.split(',') : [];

if (!token) {
  console.error('Set SFE_TOKEN to an access token of an account that may write to the connection.');
  process.exit(2);
}

const headers = { Authorization: `Bearer ${token}`, ...(tenant ? { 'X-SFE-Tenant': tenant } : {}) };

async function call(method, path, body, type = 'application/json') {
  for (let attempt = 0; ; attempt++) {
    const response = await fetch(`${base}/connections/${connection}/${path}`, {
      method,
      headers: body === undefined ? headers : { ...headers, 'Content-Type': type },
      body: body === undefined || type !== 'application/json' ? body : JSON.stringify(body),
    });
    if ((response.status === 429 || response.status === 503) && attempt < 8) {
      const wait = Number(response.headers.get('Retry-After') ?? 2) * 1000;
      await new Promise((r) => setTimeout(r, wait));
      continue;
    }
    const text = await response.text();
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text}`);
    return text ? JSON.parse(text) : null;
  }
}

// The target must exist: uploads go into the connection's root, the folders on the way in the name (a folder
// upload), which the provider makes.
async function upload(folder, name, bytes) {
  const started = await call('POST', 'files/uploads', { target: '', name: `${folder}/${name}`, size: bytes.length });
  const chunk = 8 * 1024 * 1024; // the server takes at most 16 MB a chunk
  for (let offset = 0; offset < bytes.length || offset === 0; offset += chunk) {
    const part = bytes.subarray(offset, offset + chunk);
    await call('PUT', `files/uploads/${started.id}?offset=${offset}`, part, 'application/octet-stream');
    if (!bytes.length) break;
  }
  await call('POST', `files/uploads/${started.id}/finish`, { conflicts: 'skip' });
}

function plan() {
  const items = [];
  const text = (folder, i) => Buffer.from(`SFE index check, ${folder} #${i}, ${new Date().toISOString()}\n`);
  for (let f = 0; f < folders; f++) {
    const folder = `${root}/folder-${String(f).padStart(4, '0')}`;
    for (let i = 0; i < perFolder; i++) items.push([folder, `note-${i}.txt`, () => text(folder, i)]);
  }
  for (let i = 0; i < bigFolder; i++) items.push([`${root}/big`, `item-${i}.txt`, () => text('big', i)]);
  let deep = root;
  for (let d = 1; d <= depth; d++) {
    deep += `/level-${d}`;
    items.push([deep, `at-${d}.txt`, () => text(deep, d)]);
  }
  for (const file of extra) items.push([`${root}/media`, basename(file), () => readFile(file)]);
  return items;
}

const items = plan();
const startedAt = Date.now();
let done = 0;
let failed = 0;
async function worker() {
  while (items.length) {
    const [folder, name, bytes] = items.shift();
    try {
      await upload(folder, name, await bytes());
      done++;
    } catch (error) {
      failed++;
      console.error(String(error.message ?? error).slice(0, 300));
    }
    if ((done + failed) % 100 === 0) console.log(`${done} uploaded, ${failed} failed, ${items.length} left`);
  }
}
await Promise.all(Array.from({ length: parallel }, worker));
console.log(`Done: ${done} uploaded, ${failed} failed in ${((Date.now() - startedAt) / 1000).toFixed(0)} s`);
process.exit(failed ? 1 : 0);
