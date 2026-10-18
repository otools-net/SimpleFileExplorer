// Generates a sample media library for the documentation screenshots.
// node make-media.mjs <folder> [ffmpeg path]  (the folder is emptied first; ffmpeg also from env FFMPEG)
// With ffmpeg: 40 s WebM clips panning over a drawn landscape, two re-encoded copies (similar videos) and MP3 audio.
// Without it: short clips recorded in the browser (MediaRecorder) and WAV audio.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { launch } from './cdp.mjs';
import { makeEpub } from './make-epub.mjs';
import { makePdf } from './make-pdf.mjs';

const root = process.argv[2];
if (!root) throw new Error('usage: node make-media.mjs <empty folder for the sample library> [ffmpeg]');
const ffmpeg = process.argv[3] ?? process.env.FFMPEG;
const run = args => execFileSync(ffmpeg, ['-hide_banner', '-loglevel', 'error', '-y', ...args], { stdio: 'inherit' });
fs.rmSync(root, { recursive: true, force: true });

// Palettes: [sky top, sky bottom, sun, far hills, near hills]
const scenes = {
    sunset: ['#2b1055', '#ff7e5f', '#ffd76a', '#6a3060', '#2a1435'],
    dawn: ['#355c7d', '#f8b195', '#fff2c7', '#6c5b7b', '#2e2a4a'],
    noon: ['#1e6fd9', '#a8e0ff', '#fffbe0', '#3f8f5a', '#1d5233'],
    dusk: ['#0f2027', '#2c5364', '#e8e4c9', '#203a43', '#0b1418'],
    autumn: ['#f6d365', '#fda085', '#fff6d5', '#b5543c', '#5b2a1e'],
    sea: ['#4facfe', '#c2f5ff', '#ffffff', '#1c7ed6', '#0b4f8a'],
    snow: ['#8fb3d9', '#eef5ff', '#ffffff', '#b8c9dc', '#7f95ad'],
    forest: ['#83a4d4', '#d8f3dc', '#fffde8', '#40916c', '#1b4332'],
    desert: ['#ff9966', '#ffe0a3', '#fff9e6', '#d9895b', '#8a4b2a'],
    night: ['#000428', '#004e92', '#f5f3ce', '#0b2545', '#020c1b'],
};

const photos = [
    ['Photos/2024 Beach trip/Sunset over the bay.jpg', 'sunset', 1920, 1280, 1],
    ['Photos/2024 Beach trip/Morning waves.jpg', 'sea', 1920, 1280, 2],
    ['Photos/2024 Beach trip/Lighthouse.jpg', 'dawn', 1080, 1440, 3],
    ['Photos/2024 Beach trip/Dunes.jpg', 'desert', 1920, 1080, 4],
    ['Photos/2024 Beach trip/Night swim.jpg', 'night', 800, 600, 5],
    ['Photos/2024 Beach trip/Harbour.jpg', 'noon', 1600, 1200, 6],
    ['Photos/2025 Mountains/Summit.jpg', 'snow', 2400, 1600, 7],
    ['Photos/2025 Mountains/Valley at noon.jpg', 'noon', 1920, 1280, 8],
    ['Photos/2025 Mountains/Forest trail.jpg', 'forest', 1080, 1620, 9],
    ['Photos/2025 Mountains/Autumn lake.jpg', 'autumn', 1920, 1280, 10],
    ['Photos/2025 Mountains/Starry sky.jpg', 'night', 1920, 1280, 11],
    ['Photos/2025 Mountains/Blue hour.jpg', 'dusk', 1280, 720, 12],
    ['Photos/Family/Garden party.jpg', 'forest', 1600, 1067, 13],
    ['Photos/Family/Picnic.jpg', 'noon', 1600, 1067, 14],
    ['Photos/Family/Winter walk.jpg', 'snow', 1200, 1600, 15],
    ['Photos/Family/Birthday evening.jpg', 'sunset', 640, 480, 16],
    ['Photos/Desert road.png', 'desert', 1440, 960, 17],
    ['Photos/Sunrise.jpg', 'dawn', 3000, 2000, 18],
];
const videos = [
    ['Videos/Holidays/Beach walk.webm', 'sea', 6],
    ['Videos/Holidays/Mountain drive.webm', 'snow', 8],
    ['Videos/Holidays/Campfire.webm', 'night', 5],
    ['Videos/Timelapses/Sunset timelapse.webm', 'sunset', 7],
    ['Videos/Timelapses/Clouds.webm', 'noon', 6],
];

const draw = `
function rnd(seed) { return () => (seed = (seed * 16807) % 2147483647) / 2147483647; }
function scene(ctx, w, h, p, seed, t = 0) {
    const r = rnd(seed * 7919 + 1);
    const sky = ctx.createLinearGradient(0, 0, 0, h * 0.75);
    sky.addColorStop(0, p[0]); sky.addColorStop(1, p[1]);
    ctx.fillStyle = sky; ctx.fillRect(0, 0, w, h);
    const sx = w * (0.2 + r() * 0.6) + t * w * 0.02, sy = h * (0.25 + r() * 0.25) - t * h * 0.01, sr = Math.min(w, h) * (0.05 + r() * 0.06);
    const glow = ctx.createRadialGradient(sx, sy, 0, sx, sy, sr * 5);
    glow.addColorStop(0, p[2] + 'cc'); glow.addColorStop(1, p[2] + '00');
    ctx.fillStyle = glow; ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = p[2]; ctx.beginPath(); ctx.arc(sx, sy, sr, 0, Math.PI * 2); ctx.fill();
    if (p[0] === '#000428') for (let i = 0; i < 300; i++) { ctx.fillStyle = 'rgba(255,255,255,' + r() + ')'; ctx.fillRect(r() * w, r() * h * 0.6, 2, 2); }
    for (let layer = 0; layer < 2; layer++) {
        ctx.fillStyle = p[3 + layer];
        ctx.beginPath(); ctx.moveTo(0, h);
        const base = h * (0.55 + layer * 0.15), amp = h * (0.18 - layer * 0.06);
        const a = r() * 6, b = 1 + r() * 3, c = 3 + r() * 5;
        for (let x = 0; x <= w; x += 4) {
            const u = x / w + t * 0.01 * (layer + 1);
            ctx.lineTo(x, base - amp * (0.5 * Math.sin(u * b * Math.PI + a) + 0.3 * Math.sin(u * c * Math.PI + a * 2) + 0.2 * Math.sin(u * 13 + a)));
        }
        ctx.lineTo(w, h); ctx.fill();
    }
    const fog = ctx.createLinearGradient(0, h * 0.6, 0, h);
    fog.addColorStop(0, 'rgba(255,255,255,0)'); fog.addColorStop(1, 'rgba(255,255,255,0.08)');
    ctx.fillStyle = fog; ctx.fillRect(0, h * 0.6, w, h * 0.4);
}`;

const browser = await launch({ width: 800, height: 600 });
await browser.eval(draw.replace('function rnd', 'window.rnd = function rnd').replace('function scene', 'window.scene = function scene') + ';1');

for (const [rel, sceneName, w, h, seed] of photos) {
    const type = rel.endsWith('.png') ? 'image/png' : 'image/jpeg';
    const data = await browser.eval(`(() => { const c = document.createElement('canvas'); c.width = ${w}; c.height = ${h};
        scene(c.getContext('2d'), ${w}, ${h}, ${JSON.stringify(scenes[sceneName])}, ${seed}); return c.toDataURL('${type}', 0.9); })()`);
    const file = path.join(root, rel);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, Buffer.from(data.split(',')[1], 'base64'));
}

let seed = 40;
if (ffmpeg) {
    // A wide landscape per clip; the clip pans across it (1280×720 window scaled to 640×360).
    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'sfe-media-'));
    const seconds = 40;
    for (const [rel] of videos) {
        const sceneName = videos.find(v => v[0] === rel)[1];
        const data = await browser.eval(`(() => { const c = document.createElement('canvas'); c.width = 2600; c.height = 900;
            scene(c.getContext('2d'), 2600, 900, ${JSON.stringify(scenes[sceneName])}, ${seed++}); return c.toDataURL('image/jpeg', 0.92); })()`);
        const still = path.join(tmp, `${seed}.jpg`);
        fs.writeFileSync(still, Buffer.from(data.split(',')[1], 'base64'));
        const file = path.join(root, rel);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        run(['-loop', '1', '-framerate', '25', '-t', String(seconds), '-i', still,
            '-vf', `crop=1280:720:x='(iw-1280)*t/${seconds}':y='(ih-720)/2',scale=640:360,format=yuv420p`,
            '-c:v', 'libvpx', '-b:v', '1M', '-deadline', 'realtime', '-cpu-used', '8', file]);
    }
    fs.rmSync(tmp, { recursive: true, force: true });
    // Similar videos: a smaller copy, and a copy cut by a tenth.
    fs.mkdirSync(path.join(root, 'Inbox'), { recursive: true });
    run(['-i', path.join(root, 'Videos/Holidays/Beach walk.webm'), '-vf', 'scale=320:180', '-c:v', 'libvpx', '-b:v', '300k',
        '-deadline', 'realtime', '-cpu-used', '8', path.join(root, 'Inbox/Beach walk (small).webm')]);
    run(['-i', path.join(root, 'Videos/Holidays/Mountain drive.webm'), '-t', '36', '-c:v', 'libvpx', '-b:v', '700k',
        '-deadline', 'realtime', '-cpu-used', '8', path.join(root, 'Inbox/VID_0412.webm')]);
} else {
    for (const [rel, sceneName, seconds] of videos) {
        const data = await browser.eval(`new Promise(resolve => {
            const c = document.createElement('canvas'); c.width = 640; c.height = 360;
            const ctx = c.getContext('2d'); const stream = c.captureStream(25);
            const rec = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8', videoBitsPerSecond: 1500000 });
            const parts = []; rec.ondataavailable = e => parts.push(e.data);
            rec.onstop = () => { const fr = new FileReader(); fr.onload = () => resolve(fr.result); fr.readAsDataURL(new Blob(parts, { type: 'video/webm' })); };
            const start = performance.now(); rec.start();
            (function frame() {
                const t = (performance.now() - start) / 1000;
                scene(ctx, 640, 360, ${JSON.stringify(scenes[sceneName])}, ${seed}, t);
                if (t < ${seconds}) requestAnimationFrame(frame); else rec.stop();
            })();
        })`);
        seed++;
        const file = path.join(root, rel);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, Buffer.from(data.split(',')[1], 'base64'));
    }
}
await browser.close();

// Duplicates: two copies of a photo elsewhere.
fs.mkdirSync(path.join(root, 'Inbox'), { recursive: true });
fs.copyFileSync(path.join(root, 'Photos/2024 Beach trip/Sunset over the bay.jpg'), path.join(root, 'Inbox/IMG_2041.jpg'));
fs.copyFileSync(path.join(root, 'Photos/2025 Mountains/Summit.jpg'), path.join(root, 'Inbox/IMG_2077.jpg'));
fs.copyFileSync(path.join(root, 'Photos/2025 Mountains/Summit.jpg'), path.join(root, 'Inbox/Summit copy.jpg'));

// Audio: short WAV tones.
function wav(file, freqs, seconds) {
    const rate = 22050, n = rate * seconds, buf = Buffer.alloc(44 + n * 2);
    buf.write('RIFF', 0); buf.writeUInt32LE(36 + n * 2, 4); buf.write('WAVEfmt ', 8); buf.writeUInt32LE(16, 16);
    buf.writeUInt16LE(1, 20); buf.writeUInt16LE(1, 22); buf.writeUInt32LE(rate, 24); buf.writeUInt32LE(rate * 2, 28);
    buf.writeUInt16LE(2, 32); buf.writeUInt16LE(16, 34); buf.write('data', 36); buf.writeUInt32LE(n * 2, 40);
    for (let i = 0; i < n; i++) {
        const t = i / rate, f = freqs[Math.floor(t * 2) % freqs.length];
        buf.writeInt16LE(Math.round(Math.sin(2 * Math.PI * f * t) * 6000 * Math.min(1, (seconds - t) * 4)), 44 + i * 2);
    }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, buf);
}
wav(path.join(root, 'Music/Lullaby.wav'), [262, 330, 392, 330], 12);
wav(path.join(root, 'Music/Morning tune.wav'), [392, 440, 494, 523], 9);
if (ffmpeg) {
    wav(path.join(root, 'Music/Evening walk.wav'), [330, 294, 262, 196], 95);
    run(['-i', path.join(root, 'Music/Evening walk.wav'), '-c:a', 'libmp3lame', '-b:a', '96k', path.join(root, 'Music/Evening walk.mp3')]);
    fs.rmSync(path.join(root, 'Music/Evening walk.wav'));
}
fs.mkdirSync(path.join(root, 'Documents'), { recursive: true });
fs.writeFileSync(path.join(root, 'Documents/Packing list.txt'), 'Towels\nSun cream\nCamera\n');
fs.writeFileSync(path.join(root, 'Documents/Notes.md'), '# Trip notes\n');
// Text files for the text viewer: a log, and a script (highlighted).
fs.writeFileSync(path.join(root, 'Documents/Photo backup.log'), [
    '2025-07-14 21:00:02.118 INFO  Backup started: 1,284 photos to copy',
    '2025-07-14 21:00:02.240 DEBUG Destination "Backup drive" has 412 GB free',
    '2025-07-14 21:03:41.906 INFO  Copied 600 of 1,284 photos',
    '2025-07-14 21:04:15.377 WARN  Skipped "IMG_2231.jpg": in use by another program',
    '2025-07-14 21:07:58.051 INFO  Copied 1,283 of 1,284 photos',
    '2025-07-14 21:07:58.122 ERROR Could not copy "IMG_2231.jpg" after 3 tries',
    '2025-07-14 21:07:58.130 INFO  Backup finished in 7 min 56 s, 1 photo left for next time',
    '',
].join('\n'));
// Subtitles next to their video (Play opens it at a cue); the fourth cue overlaps the third.
fs.writeFileSync(path.join(root, 'Videos/Holidays/Campfire.en.srt'), [
    '1', '00:00:00,200 --> 00:00:01,000', 'Is the fire <i>still</i> going?', '',
    '2', '00:00:01,100 --> 00:00:01,900', '<b>Ana:</b> Just about.', '',
    '3', '00:00:02,000 --> 00:00:03,000', 'Pass me the marshmallows.', '',
    '4', '00:00:02,800 --> 00:00:03,600', 'Careful, they burn fast!', '',
    '5', '00:00:03,700 --> 00:00:04,400', 'Look at the stars up there.', '',
    '6', '00:00:04,500 --> 00:00:05,000', '<i>(crackling)</i>', '',
].join('\r\n'));
// A table for the CSV view: semicolons, as spreadsheets write them in many countries.
fs.writeFileSync(path.join(root, 'Documents/Huts.csv'), [
    'Hut;Massif;Altitude (m);Beds;Open from;Price per night',
    'Refuge du Lac Blanc;Mont Blanc;2352;38;June;62.50',
    'Refuge du Goûter;Mont Blanc;3835;120;May;98.00',
    'Monte Rosa Hut;Monte Rosa;2883;120;March;95.00',
    'Hörnlihütte;Matterhorn;3260;130;July;150.00',
    'Refuge de la Charpoua;Mont Blanc;2841;14;June;45.00',
    'Cabane du Trient;Mont Blanc;3170;130;June;72.00',
    'Rifugio Torino;Mont Blanc;3375;80;May;85.00',
    'Britanniahütte;Saas;3030;134;February;80.00',
    'Cabane de Moiry;Val d\'Anniviers;2825;100;March;76.00',
    'Refuge des Cosmiques;Mont Blanc;3613;148;February;84.00',
    '',
].join('\r\n'));
// JSON for the tree view.
fs.writeFileSync(path.join(root, 'Documents/Trip plan.json'), JSON.stringify({
    trip: 'Alps, summer 2025',
    start: '2025-07-12',
    travellers: ['Ana', 'Leo', 'Marta'],
    budget: { currency: 'EUR', total: 1850, spent: 1243.5 },
    days: [
        { day: 1, town: 'Annecy', route: 'Lake shore walk', km: 12, hut: null, done: true },
        { day: 2, town: 'Chamonix', route: 'Lac Blanc', km: 18, hut: 'Refuge du Lac Blanc', done: true },
        { day: 3, town: 'Chamonix', route: 'Mer de Glace', km: 14, hut: null, done: false },
        { day: 4, town: 'Zermatt', route: 'Five lakes walk', km: 10, hut: null, done: false },
    ],
    checklist: { boots: true, headlamp: true, map: false },
}, null, 2) + '\n');
// A book for the EPUB reader: three chapters, enough text for two pages side by side.
const story = [
    'The path left the last pines behind and climbed into a world of stone. Ana stopped to catch her breath and looked back: the valley was already blue with evening, and the village lights were coming on one by one.',
    'Leo was a few switchbacks above, a small figure against the snowfield, his red pack the only colour on the slope. He waved without turning round, the way he always did when he did not want to be asked how far there was still to go.',
    'They had left later than planned. The bus had been slow, the bakery closed, and the warden had said on the telephone that dinner would not wait for anyone. Now the light was going, and the hut was still somewhere above the ridge.',
    'Marta caught up with her, breathing hard, and laughed. "If we run, we might get dessert," she said. Nobody ran. But nobody stopped again either, and the last hour went by in a steady rhythm of boots on rock and the clink of poles.',
    'The hut appeared all at once, as huts do: a stone wall, a lit window, a flag snapping in the wind. Somebody opened the door before they reached it, and the smell of soup and wood smoke poured out into the cold.',
];
fs.writeFileSync(path.join(root, 'Documents/Hut stories.epub'), makeEpub('Hut stories', 'Ana Ruiz', [
    { title: 'The climb', paragraphs: story },
    { title: 'Night at the hut', paragraphs: [...story].reverse() },
    { title: 'Storm', paragraphs: story.slice(1).concat(story[0]) },
]));
// A PDF for the PDF view: three pages, an outline, a title.
fs.writeFileSync(path.join(root, 'Documents/Hut handbook.pdf'), makePdf('Mountain hut handbook', [
    { heading: 'Before you go', lines: [
        'Book your bed at least two weeks ahead in July and August.',
        'Bring a sleeping-bag liner: blankets are provided, sheets are not.',
        'Most huts take cash only. Check the opening dates: high huts close in September.',
        'Tell the warden if you will arrive after 18:00, or if you change your plans.',
    ] },
    { heading: 'At the hut', lines: [
        'Leave boots, poles and ice axes in the boot room by the entrance.',
        'Dinner is served at 18:30, breakfast from 4:30 for early starts.',
        'Quiet hours run from 22:00 to 5:00. Lights out in the dormitories.',
        'Water may not be drinkable: ask the warden before filling your bottle.',
    ] },
    { heading: 'Safety', lines: [
        'Check the weather forecast at the hut in the evening, and again at dawn.',
        'Write your route and expected return time in the hut book.',
        'Emergency number in the Alps: 112. Helicopter rescue sign: both arms raised.',
    ] },
]));
fs.writeFileSync(path.join(root, 'Documents/Hiking guide.md'), [
    '# Hiking guide',
    '',
    'Notes for the summer trips. The [packing list](Packing%20list.txt) and the [trip diary](Trip%20diary.txt) are next to this file.',
    '',
    '## Before leaving',
    '',
    '- Check the weather the evening before, and again in the morning.',
    '- Tell someone the route and when you plan to be back.',
    '- Charge the phone and the **headlamp**.',
    '',
    '> Start early: afternoon storms are common in the mountains.',
    '',
    '## Routes',
    '',
    '| Route | Distance | Climb | Time |',
    '|---|---:|---:|---:|',
    '| Lake loop | 9 km | 350 m | 3 h |',
    '| Ridge and pass | 14 km | 1,100 m | 6 h |',
    '| Summit | 18 km | 1,600 m | 8 h |',
    '',
    '### Lake loop',
    '',
    'An easy walk around the lake, *shaded most of the way*. Good for a first day.',
    '',
    '### Ridge and pass',
    '',
    'Steep at first, then along the ridge. See [Before leaving](#before-leaving).',
    '',
    '## Sorting the photos',
    '',
    'The [rename script](Rename%20photos.ps1) names photos after the day they were taken:',
    '',
    '```powershell',
    '.\\Rename photos.ps1 -Folder "D:\\Photos\\2025 Mountains" -WhatIf',
    '```',
    '',
].join('\n'));
fs.writeFileSync(path.join(root, 'Documents/Trip diary.txt'), [
    'Mountains, day three',
    '',
    'We left the hut before sunrise. The path climbed through the last larches, then over loose stones, and the valley ' +
    'below slowly filled with light. By nine we were on the ridge, with the lake far down on one side and the glacier on the other.',
    '',
    'Lunch at the pass: bread, cheese, the last of the apricots. A family of marmots watched us from a rock and whistled ' +
    'every time someone stood up. The wind turned cold after noon, so we did not wait for the clouds to lift from the summit.',
    '',
    'The way down was longer than it looked from above. Our knees will remember it. Dinner in the village, then early to bed: ' +
    'tomorrow the train back, and three hundred photos to sort.',
    '',
].join('\n'));
fs.writeFileSync(path.join(root, 'Documents/Rename photos.ps1'), [
    '# Renames the photos of a folder after the day they were taken: 2025-07-14 001.jpg, 2025-07-14 002.jpg...',
    'param(',
    '    [Parameter(Mandatory)] [string] $Folder,',
    '    [switch] $WhatIf',
    ')',
    '',
    '$count = @{}',
    'Get-ChildItem -Path $Folder -Filter *.jpg | Sort-Object LastWriteTime | ForEach-Object {',
    "    $day = $_.LastWriteTime.ToString('yyyy-MM-dd')",
    '    $count[$day] = 1 + $count[$day]',
    "    $name = '{0} {1:000}{2}' -f $day, $count[$day], $_.Extension.ToLower()",
    '    if ($_.Name -ne $name) {',
    '        Rename-Item -LiteralPath $_.FullName -NewName $name -WhatIf:$WhatIf',
    '    }',
    '}',
    'Write-Host "Renamed $($count.Values | Measure-Object -Sum | Select-Object -ExpandProperty Sum) photos"',
    '',
].join('\r\n'));
console.log('done');
