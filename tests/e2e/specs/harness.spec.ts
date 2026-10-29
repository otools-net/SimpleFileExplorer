// The helpers themselves: a long press is what a phone's is (the browser's own events), so a spec failing on one
// means the app, not the harness.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectOpensWithin } from '../fixtures/timing';
import { longPress, needsTouchGestures } from '../fixtures/touch';

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
