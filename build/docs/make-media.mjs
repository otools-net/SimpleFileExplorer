// Generates a sample media library for the documentation screenshots.
import fs from 'node:fs';
import path from 'node:path';
import { launch } from './cdp.mjs';

const root = process.argv[2];
if (!root) throw new Error('usage: node make-media.mjs <empty folder for the sample library>');
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
fs.mkdirSync(path.join(root, 'Documents'), { recursive: true });
fs.writeFileSync(path.join(root, 'Documents/Packing list.txt'), 'Towels\nSun cream\nCamera\n');
fs.writeFileSync(path.join(root, 'Documents/Notes.md'), '# Trip notes\n');
console.log('done');
