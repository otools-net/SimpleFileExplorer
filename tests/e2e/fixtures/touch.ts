// A finger, not a mouse: phone specs use these (and `locator.tap()`), never `click()`.
// Long presses and swipes go through the DevTools protocol (`Input.dispatchTouchEvent`), which makes the browser's own
// gestures (a long press's `contextmenu`, scrolling): Chromium only. WebKit has no such protocol in Playwright, and
// iOS Safari sends no `contextmenu` anyway; specs needing them skip on WebKit with `needsTouchGestures`.
import { test, type Locator, type Page } from '@playwright/test';

export function needsTouchGestures(browserName: string): void {
    test.skip(browserName !== 'chromium', 'long presses and swipes need the DevTools protocol (Chromium)');
}

async function centre(locator: Locator): Promise<{ x: number; y: number }> {
    await locator.scrollIntoViewIfNeeded();
    const box = await locator.boundingBox();
    if (!box)
        throw new Error(`not on screen: ${locator}`);
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/** When Android Chrome sends a long press's `contextmenu`, the finger still down. */
const LONG_PRESS_MS = 500;

/**
 * A finger held on `locator` for `ms`, then lifted, as on an Android phone: pointer and touch events from Chromium's
 * gesture pipeline (`Input.synthesizeTapGesture`: trusted, and no `click` after a long one, unlike raw
 * `Input.dispatchTouchEvent`), and at 500 ms a `contextmenu` whose `pointerType` is `touch`. Headless Chromium on
 * Windows sends no `contextmenu` for a held finger, so that one is dispatched from the page (untrusted: the app's
 * handlers can't tell; the browser's own menu can't be checked this way). Checked in harness.spec.ts.
 */
export async function longPress(page: Page, locator: Locator, ms = 800): Promise<void> {
    const point = await centre(locator);
    const cdp = await page.context().newCDPSession(page);
    try {
        const gesture = cdp.send('Input.synthesizeTapGesture', { ...point, duration: ms, tapCount: 1, gestureSourceType: 'touch' });
        await page.waitForTimeout(LONG_PRESS_MS);
        await page.evaluate(({ x, y }) => {
            const target = document.elementFromPoint(x, y);
            target?.dispatchEvent(new PointerEvent('contextmenu', {
                bubbles: true, cancelable: true, composed: true, clientX: x, clientY: y, pointerType: 'touch', isPrimary: true,
            }));
        }, point);
        await gesture;
    } finally {
        await cdp.detach();
    }
}

/** A finger moved from `from` to `to` (page coordinates) in `steps` moves over about `ms`. */
export async function swipe(page: Page, from: { x: number; y: number }, to: { x: number; y: number }, ms = 300, steps = 12): Promise<void> {
    const cdp = await page.context().newCDPSession(page);
    try {
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [from] });
        for (let i = 1; i <= steps; i++) {
            await page.waitForTimeout(ms / steps);
            const point = { x: from.x + (to.x - from.x) * i / steps, y: from.y + (to.y - from.y) * i / steps };
            await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [point] });
        }
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    } finally {
        await cdp.detach();
    }
}

/**
 * Taps `locator` once the page has settled after scrolling it into view (no scroll for 200 ms, its box the same over
 * two frames). WebKit (Playwright on Windows) sends a tap's `click` to what was under the finger before a scroll that
 * just happened (Playwright's own retry scrolls included): the touch events reach the ⋮ button, the click its tile.
 */
export async function tapWhenSettled(page: Page, locator: Locator): Promise<void> {
    await locator.scrollIntoViewIfNeeded();
    await locator.evaluate(element => new Promise<void>(resolve => {
        let last = performance.now();
        const scrolled = () => { last = performance.now(); };
        addEventListener('scroll', scrolled, { capture: true, passive: true });
        let box = element.getBoundingClientRect();
        const frame = () => {
            const now = element.getBoundingClientRect();
            const same = now.x === box.x && now.y === box.y && now.width === box.width && now.height === box.height;
            box = now;
            if (same && performance.now() - last >= 200) {
                removeEventListener('scroll', scrolled, { capture: true });
                resolve();
            } else {
                requestAnimationFrame(frame);
            }
        };
        requestAnimationFrame(frame);
    }));
    await locator.tap();
}
