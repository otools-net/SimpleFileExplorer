#!/usr/bin/env node
// Screenshots of a real connection for docs/ (the docs-screenshots tool has no connection: no Dropbox account
// there). Runs against an install where a connection is signed in and indexed, filled by seed.mjs. Usage in
// README.md ("Screenshots"). The provider account's e-mail is replaced on every page before a shot.
//   node shots.mjs probe <url> [name]   one page, saved to the temp folder, with its buttons' titles printed
//   node shots.mjs                      every shot into docs/images/
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { launch, sleep } from '../docs-screenshots/cdp.mjs';

const BASE = (process.env.SFE_URL ?? 'http://localhost:5080').replace(/\/?$/, '/');
const USER = process.env.SFE_SHOTS_USER;
const PASSWORD = process.env.SFE_SHOTS_PASSWORD;
const C = process.env.SFE_CONNECTION ?? '1';
const ROOT = process.env.SFE_SEED_ROOT ?? 'sfe-seed';
const OUT = fileURLToPath(new URL('../../../docs/images/', import.meta.url));
const enc = encodeURIComponent;

if (!USER || !PASSWORD) {
  console.error('Set SFE_SHOTS_USER and SFE_SHOTS_PASSWORD (an account of the library using the connection).');
  process.exit(2);
}

// Every e-mail address on the page becomes a neutral one (the provider account shows in places).
const NEUTRAL = `(() => {
  const mail = /[\\w.+-]+@[\\w-]+(\\.[\\w-]+)+/g;
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) n.nodeValue = n.nodeValue.replace(mail, 'me@example.org');
  for (const i of document.querySelectorAll('input')) i.value = i.value.replace(mail, 'me@example.org');
  return 1; })()`;

async function session(opts = {}) {
  const b = await launch({ width: 1280, height: 800, ...opts });
  await b.goto(BASE + 'login', 1500);
  const body = JSON.stringify({ userName: USER, password: PASSWORD, remember: true });
  const status = await b.eval(
    `fetch('api/v1/session/login', { method: 'POST', headers: { 'Content-Type': 'application/json', ` +
      `'X-SFE-Request': '1' }, body: ${JSON.stringify(body)} }).then(r => r.status)`,
  );
  if (status !== 200) throw new Error(`sign-in answered ${status}`);
  b.open = async (url, wait = 4000) => {
    await b.goto(BASE + url, wait);
    await b.eval(`document.querySelector('button[title^="Hide this panel"]')?.click(); 1`);
    await sleep(300);
  };
  b.save = async (file) => {
    await b.eval(NEUTRAL);
    await sleep(200);
    await b.shot(file);
    console.log(`  + ${file}`);
  };
  b.clickText = async (selector, text) => {
    const ok = await b.eval(
      `(() => { const el = [...document.querySelectorAll(${JSON.stringify(selector)})]` +
        `.find(e => e.textContent.trim().includes(${JSON.stringify(text)})); if (!el) return false; el.click(); ` +
        `return true; })()`,
    );
    if (!ok) throw new Error(`no ${selector} with ${text}`);
    await sleep(900);
  };
  b.clickTitle = async (title) => {
    await b.click(`[title^="${title}"]`);
    await sleep(900);
  };
  return b;
}

async function probe(url, name = 'probe') {
  const b = await session({ height: 900 });
  await b.open(url, 6000);
  const file = join(tmpdir(), `sfe-${name}.png`);
  await b.save(file);
  console.log(await b.eval(`[...document.querySelectorAll('button[title], a[title]')].map(e => e.title).join(' | ')`));
  await b.close();
}

const media = `?c=${C}&path=${enc(`${ROOT}/media`)}`;

const shots = {
  /** A connection's folder listed from the index, with Dropbox's thumbnails. */
  async listing() {
    const b = await session({ height: 900 });
    await b.open(media, 8000);
    await b.save(OUT + 'connection-listing.png');
    await b.close();
  },
  /** Administration › Connections › Manage: the index in this space, then the folder rules card. */
  async manage() {
    const b = await session({ height: 900 });
    await b.open('admin?section=connections', 3000);
    await b.clickText('button', 'Manage');
    await sleep(1500);
    await b.save(OUT + 'connection-manage.png');
    await b.clickText('button', 'Folder rules');
    await sleep(1500);
    await b.eval(`[...document.querySelectorAll('*')].find(e => e.textContent.trim().startsWith('Folder access') ` +
      `&& e.children.length < 6)?.scrollIntoView({ block: 'center' }); 1`);
    await sleep(500);
    await b.save(OUT + 'connection-folder-rules.png');
    await b.close();
  },
  /** A search in the connection (from its index), then the same words Everywhere (grouped by place). */
  async search() {
    const b = await session({ height: 900 });
    await b.open(`?c=${C}&path=${enc(`${ROOT}/media`)}&q=${enc('type:image s:<100kb')}`, 7000);
    await b.save(OUT + 'connection-search.png');
    await b.open(`?q=${enc('summit')}&all=1`, 7000);
    await b.save(OUT + 'search-everywhere.png');
    await b.close();
  },
  /** The tags of every file of a connection's folder (its tag button). */
  async tags() {
    const b = await session({ height: 900 });
    await b.open(media, 6000);
    await b.clickTitle('Add or remove tags on all the files of this folder');
    await sleep(1500);
    await b.save(OUT + 'connection-folder-tags.png');
    await b.close();
  },
};

const [command, ...rest] = process.argv.slice(2);
if (command === 'probe') await probe(...rest);
else for (const name of command ? [command, ...rest] : Object.keys(shots)) await shots[name]();
process.exit(0);
