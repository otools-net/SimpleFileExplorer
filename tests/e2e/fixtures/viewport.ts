// What a phone's screen does that a fixed viewport doesn't: the address bar coming and
// going, the on-screen keyboard, turning the phone, the notch and home indicator. Each is an approximation, said
// where; the real thing is in the by-hand checklist (testing-e2e.md) and real-phone-tests-plan.md.
import { expect, test, type Page } from '@playwright/test';

/** Android Chrome's address bar: about 56 px, shown or hidden by scrolling, a resize of the height alone. */
export const ADDRESS_BAR = 56;

/** The address bar shown (the viewport `ADDRESS_BAR` px shorter than the project's) or hidden (the project's size). */
export async function setAddressBar(page: Page, shown: boolean): Promise<void> {
    const { width, height } = test.info().project.use.viewport!;
    await page.setViewportSize({ width, height: shown ? height - ADDRESS_BAR : height });
}

/**
 * The on-screen keyboard open over the lower `fraction` of the screen. Emulated by a shorter viewport, as browsers
 * that resize the page for it (`interactive-widget=resizes-content`, Firefox); Chrome and Safari shrink only the
 * visual viewport, so a field the page keeps in view here may still need the browser's own scrolling there. Returns
 * a function that closes it.
 */
export async function openKeyboard(page: Page, fraction = 0.42): Promise<() => Promise<void>> {
    const size = page.viewportSize()!;
    await page.setViewportSize({ width: size.width, height: Math.round(size.height * (1 - fraction)) });
    return () => page.setViewportSize(size);
}

/** The element that has the focus is shown whole above the keyboard (inside the shortened viewport). */
export async function expectFocusedFieldShown(page: Page): Promise<void> {
    await expect.poll(() => page.evaluate(() => {
        const element = document.activeElement as HTMLElement | null;
        if (!element || element === document.body)
            return 'nothing has the focus';
        const box = element.getBoundingClientRect();
        const what = `${element.localName}.${String(element.className).split(' ')[0]}`;
        return box.top >= 0 && box.bottom <= innerHeight + 0.5 ? 'shown' : `${what} at ${Math.round(box.top)}–${Math.round(box.bottom)}, screen ${innerHeight}`;
    }), 'the focused field above the keyboard').toBe('shown');
}

/** Turns the phone: width and height swapped. Returns its size before. */
export async function turn(page: Page): Promise<{ width: number; height: number }> {
    const size = page.viewportSize()!;
    await page.setViewportSize({ width: size.height, height: size.width });
    return size;
}

/** An iPhone's notch and home indicator, upright: 47 px at the top, 34 at the bottom (iPhone 13). */
export const IPHONE_INSETS = { top: 47, bottom: 34, left: 0, right: 0 };

/**
 * Safe-area insets (`env(safe-area-inset-*)`) for this page, through Chromium's `Emulation.setSafeAreaInsetsOverride`.
 * Skips the test where the browser can't (WebKit in Playwright; a Chromium without it).
 */
export async function setSafeArea(page: Page, insets = IPHONE_INSETS): Promise<void> {
    test.skip(page.context().browser()?.browserType().name() !== 'chromium', "safe-area insets: Chromium's DevTools protocol");
    const cdp = await page.context().newCDPSession(page);
    try {
        await cdp.send('Emulation.setSafeAreaInsetsOverride' as never, { insets } as never);
    } catch (error) {
        test.skip(true, `this browser has no Emulation.setSafeAreaInsetsOverride (${(error as Error).message})`);
    }
}

/** No control shown in `scope` reaches into the insets (under the notch or the home indicator). */
export async function expectClearOfInsets(page: Page, scope: string, insets = IPHONE_INSETS): Promise<void> {
    // The page sees them (`viewport-fit=cover` in index.html): else the check below would pass on nothing.
    const bottom = await page.evaluate(() => {
        const probe = document.body.appendChild(document.createElement('div'));
        probe.style.cssText = 'position: fixed; padding-bottom: env(safe-area-inset-bottom, 0px)';
        const value = parseFloat(getComputedStyle(probe).paddingBottom);
        probe.remove();
        return value;
    });
    expect(bottom, 'env(safe-area-inset-bottom) as emulated').toBe(insets.bottom);
    const inside = await page.evaluate(({ scope, insets }) => [...document.querySelectorAll(`${scope} :is(button, a[href], input, select, textarea, [role=button])`)]
        .filter(element => element.getClientRects().length > 0 && !element.closest('[inert], [aria-hidden=true]'))
        .map(element => ({ element, box: element.getBoundingClientRect() }))
        .filter(({ box }) => box.bottom > 0 && box.top < innerHeight
            && (box.top < insets.top || box.bottom > innerHeight - insets.bottom || box.left < insets.left || box.right > innerWidth - insets.right))
        .map(({ element, box }) => `${element.localName}.${String(element.className).split(' ')[0]} "${element.getAttribute('aria-label') ?? element.textContent?.trim().slice(0, 20)}" ${Math.round(box.top)}–${Math.round(box.bottom)}`),
    { scope, insets });
    expect(inside, `controls of ${scope} under the notch or the home indicator`).toEqual([]);
}
