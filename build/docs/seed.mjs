// Setup page screenshot, then the administrator and sample data through the API.
// node seed.mjs <sample library folder>  (the folder make-media.mjs filled, which the app's Sfe:Root points at)
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import path from 'node:path';
import { launch, sleep } from './cdp.mjs';

const BASE = process.env.SFE_URL ?? 'http://localhost:5093/';
const OUT = fileURLToPath(new URL('../../docs/images/', import.meta.url));

const b = await launch({ width: 1280, height: 800 });
await b.goto(BASE, 3000);
await b.waitFor('input');
await b.shot(OUT + 'setup-page.png');

const js = body => `(async () => {
    const call = async (method, url, data) => {
        const r = await fetch(url, { method, headers: { 'Content-Type': 'application/json', 'X-SFE-Request': '1' }, body: data === undefined ? undefined : JSON.stringify(data) });
        const text = await r.text();
        if (!r.ok) throw new Error(method + ' ' + url + ' ' + r.status + ' ' + text);
        return text ? JSON.parse(text) : null;
    };
    ${body}
})()`;

console.log(await b.eval(js(`
    await call('POST', 'api/setup', { code: process.env.SFE_SETUP_CODE ?? 'doc-screenshots-setup', userName: 'admin', password: 'admin-password-1', displayName: 'Alex (admin)', libraryName: 'Home media' });
    await call('POST', 'api/scan', { path: '' });
    for (let i = 0; i < 100; i++) { const s = await call('GET', 'api/scan'); if (!s.isRunning && !s.running) break; await new Promise(r => setTimeout(r, 300)); }
    return 'ok';
`)));
await sleep(2000);

console.log(await b.eval(js(`
    const tag = async (name, extra = {}) => (await call('POST', 'api/tags', { name, ...extra })).id;
    const places = await tag('Places', { color: 'teal', isCategory: true });
    const people = await tag('People', { color: 'purple', isCategory: true });
    const beach = await tag('Beach', { color: 'cyan', parentIds: [places] });
    const mountains = await tag('Mountains', { color: 'green', parentIds: [places] });
    const sunsets = await tag('Sunset', { color: 'orange', shorthand: 'sun' });
    const family = await tag('Family', { color: 'pink', parentIds: [people] });
    const sea = await tag('Sea', { color: 'blue', aliases: ['Ocean'], parentIds: [places] });
    await tag('Night', { color: 'indigo' });
    await tag('Wallpaper', { color: 'amber' });
    const set = (paths, add) => call('POST', 'api/file-tags', { paths, add });
    await set(['Photos/2024 Beach trip/Sunset over the bay.jpg', 'Photos/2024 Beach trip/Morning waves.jpg', 'Photos/2024 Beach trip/Lighthouse.jpg', 'Photos/2024 Beach trip/Dunes.jpg', 'Photos/2024 Beach trip/Harbour.jpg', 'Videos/Holidays/Beach walk.webm'], [beach]);
    await set(['Photos/2024 Beach trip/Morning waves.jpg', 'Photos/2024 Beach trip/Harbour.jpg'], [sea]);
    await set(['Photos/2024 Beach trip/Sunset over the bay.jpg', 'Photos/Family/Birthday evening.jpg', 'Videos/Timelapses/Sunset timelapse.webm'], [sunsets]);
    await set(['Photos/2025 Mountains/Summit.jpg', 'Photos/2025 Mountains/Valley at noon.jpg', 'Photos/2025 Mountains/Forest trail.jpg', 'Photos/2025 Mountains/Autumn lake.jpg', 'Videos/Holidays/Mountain drive.webm'], [mountains]);
    await set(['Photos/Family/Garden party.jpg', 'Photos/Family/Picnic.jpg', 'Photos/Family/Winter walk.jpg', 'Photos/Family/Birthday evening.jpg'], [family]);
    await set(['Photos/2025 Mountains/Summit.jpg'], [1]);
    const groups = await call('GET', 'api/access/groups');
    const list = groups.groups ?? groups;
    const users = list.find(g => /users/i.test(g.name));
    const guests = list.find(g => /guests/i.test(g.name));
    await call('POST', 'api/access/users', { userName: 'sam', displayName: 'Sam', password: 'sam-password-1', kind: 'user', isServerAdmin: false, disabled: false, groupIds: [users.id] });
    await call('POST', 'api/access/users', { userName: 'kids', displayName: 'Kids', password: 'kids-password-1', kind: 'guest', isServerAdmin: false, disabled: false, groupIds: [guests.id] });
    await call('POST', 'api/access/groups', { name: 'Family editors', rights: ['library.browse', 'files.view', 'files.download', 'tags.view', 'tags.assign', 'files.edit'] });
    const start = new Date(); start.setHours(3, 0, 0, 0);
    await call('POST', 'api/tasks/schedules', { task: 'scan', path: null, kind: 'weekly', start: start.toISOString(), intervalMinutes: 0, days: 127, enabled: true });
    await call('POST', 'api/tasks/schedules', { task: 'scan', path: 'Photos', kind: 'interval', start: start.toISOString(), intervalMinutes: 360, enabled: true, days: 0 });
    return JSON.stringify(list.map(g => g.name));
`)));

// A tagged file gone from the disk (for Moved files), then a second scan and a comparison (for the task history).
const library = process.argv[2];
if (library)
    fs.rmSync(path.join(library, 'Photos/2025 Mountains/Autumn lake.jpg'), { force: true });
console.log(await b.eval(js(`
    await call('POST', 'api/tasks/scan/start?path=');
    await new Promise(r => setTimeout(r, 4000));
    await call('POST', 'api/tasks/similarVideos/start');
    await new Promise(r => setTimeout(r, 3000));
    return 'ok';
`)));
await b.close();
