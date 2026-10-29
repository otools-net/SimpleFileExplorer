// The helpers themselves: a long press is what a phone's is (the browser's own events), so a spec failing on one
// means the app, not the harness.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectOpensWithin } from '../fixtures/timing';
import { longPress, needsTouchGestures, pinch } from '../fixtures/touch';

test('a long press gives the touch events and a touch contextmenu', async ({ page, browserName }) => {
    needsTouchGestures(browserName);
    await openFolder(page, 'Photos/2025 Mountains');
    await page.evaluate(() => {
        const seen: string[] = [];
        (window as unknown as { __seen: string[] }).__seen = seen;
        for (const type of ['touchstart', 'touchend', 'pointerdown', 'pointerup', 'contextmenu', 'click'])
            document.addEventListener(type, event => seen.push(type === 'contextmenu' || type.startsWith('pointer')
                ? `${type}:${(event as PointerEvent).pointerType ?? ''}` : type), { capture: true });
    });
    await longPress(page, item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.preview'));
    const seen = await page.evaluate(() => (window as unknown as { __seen: string[] }).__seen);
    console.log(seen.join(' '));
    expect(seen).toContain('touchstart');
    expect(seen).toContain('contextmenu:touch');
    expect(seen, 'no click after a long press, as on a phone').not.toContain('click');
});

// Each engine's floor for "a tap shows something": a bare element shown by a click handler. The menus' budgets are
// read against it (WebKit on Windows is far slower than an iPhone's).
test('baseline: a tap shows a bare element', async ({ page }) => {
    // On the app's origin, with its viewport (without it a phone lays out 980 px wide).
    await page.route('**/e2e-baseline', route => route.fulfill({
        contentType: 'text/html',
        body: '<!doctype html><meta name="viewport" content="width=device-width, initial-scale=1"><button style="width:200px;height:60px">Show</button>',
    }));
    await page.goto('./e2e-baseline');
    await page.evaluate(() => document.querySelector('button')!.addEventListener('click', () => {
        const div = document.createElement('div');
        div.id = 'shown';
        div.textContent = 'Shown';
        document.body.append(div);
    }));
    await expectOpensWithin(page, '#shown', () => page.locator('button').tap(), 1000);
});

// Phone specs act with a finger (`tap`, the touch helpers): a mouse call there tests what a phone never does. A line
// meant for the desktop project says so with a `// mouse: <why>` comment.
test('no mouse in the specs, unless a line says why', async ({}, testInfo) => {
    test.skip(testInfo.project.name !== 'android', 'reads the files: once per run');
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    const files = ['specs', 'specs/diagnose', 'fixtures'].flatMap(folder => fs.readdirSync(path.join(root, folder))
        .filter(name => name.endsWith('.ts')).map(name => path.join(folder, name)));
    const mouse = /\.(click|dblclick|hover)\(|mouse\.(click|down|up|move|dblclick|wheel)\(/;
    const found = files.flatMap(file => fs.readFileSync(path.join(root, file), 'utf8').split('\n')
        .map((line, index) => ({ line, at: `${file.replaceAll(path.sep, '/')}:${index + 1}` }))
        .filter(({ line }) => mouse.test(line) && !/\/\/ mouse: \S/.test(line) && !line.trim().startsWith('//') && !line.trim().startsWith('*'))
        .map(({ line, at }) => `${at} ${line.trim()}`));
    expect(found, 'mouse calls without a "// mouse: <why>" comment').toEqual([]);
});

test('a pinch sends two fingers moving apart', async ({ page, browserName }) => {
    needsTouchGestures(browserName);
    await openFolder(page, 'Photos/2025 Mountains');
    await page.evaluate(() => {
        const seen: number[] = [];
        (window as unknown as { __apart: number[] }).__apart = seen;
        document.addEventListener('touchmove', event => {
            if (event.touches.length === 2)
                seen.push(Math.abs(event.touches[1].clientX - event.touches[0].clientX));
        }, { capture: true, passive: true });
    });
    await pinch(page, { x: 150, y: 300 }, 60, 200);
    const apart = await page.evaluate(() => (window as unknown as { __apart: number[] }).__apart);
    expect(apart.length).toBeGreaterThan(5);
    expect(apart.at(-1)!).toBeGreaterThan(apart[0]);
});
