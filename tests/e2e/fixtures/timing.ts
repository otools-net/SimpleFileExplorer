// "The menu doesn't open at once": the time from the finger lifted (the last `touchend`, else `pointerup`/`click`) to
// the first animation frame where `selector` is shown, measured in the page. One tap must be enough: a menu that
// needs a second one never shows within the wait and fails. The time is split at the `click` (before it: the browser,
// a tap delay waiting for a double tap) and at the element entering the page (before it: the app's script; after it:
// style, layout and paint until the frame that has it).
import { expect, test, type Page } from '@playwright/test';

declare global {
    interface Window { __sfeOpen?: { lifted: number | null; clicked: number | null; added: number | null; shown: number | null } }
}

/** Runs `action` (one tap) and returns the ms until `selector` shows; fails if it doesn't show in `budget` ms. */
export async function expectOpensWithin(page: Page, selector: string, action: () => Promise<void>, budget = 100): Promise<number> {
    await page.evaluate(selector => {
        const open = { lifted: null as number | null, clicked: null as number | null, added: null as number | null, shown: null as number | null };
        window.__sfeOpen = open;
        let touched = false;
        // The last finger lifted before the element shows; with no touch at all (desktop), the mouse button.
        document.addEventListener('touchend', () => { touched = true; if (open.shown === null) open.lifted = performance.now(); }, { capture: true });
        document.addEventListener('pointerup', () => { if (!touched && open.shown === null) open.lifted = performance.now(); }, { capture: true });
        document.addEventListener('click', () => { if (open.shown === null) open.clicked = performance.now(); }, { capture: true });
        const added = new MutationObserver(() => {
            if (document.querySelector(selector)) {
                open.added = performance.now();
                added.disconnect();
            }
        });
        added.observe(document.body, { childList: true, subtree: true });
        // The frame's own time stamp is when it began, which can be before the element came: the time it runs at.
        const frame = () => {
            const element = document.querySelector(selector);
            if (open.lifted !== null && element && element.getClientRects().length > 0) {
                open.shown = performance.now();
                return;
            }
            requestAnimationFrame(frame);
        };
        requestAnimationFrame(frame);
    }, selector);
    await action();
    await expect(page.locator(selector), 'shown after one tap').toBeVisible({ timeout: Math.max(2000, budget * 5) });
    await expect.poll(() => page.evaluate(() => window.__sfeOpen!.shown)).not.toBeNull();
    const { lifted, clicked, added, shown } = await page.evaluate(() => window.__sfeOpen!);
    const ms = shown! - lifted!;
    const at = (from: number | null, to: number | null) => from === null || to === null ? '?' : (to - from).toFixed(0);
    const split = `click +${at(lifted, clicked)}, in the page +${at(clicked ?? lifted, added)}, on screen +${at(added, shown)} ms`;
    const text = `${selector}: ${ms.toFixed(0)} ms (${split})`;
    // In the HTML report under the test; printed with SFE_E2E_TIMINGS=1.
    test.info().annotations.push({ type: 'opens', description: text });
    if (process.env.SFE_E2E_TIMINGS === '1')
        console.log(`[timing] ${test.info().project.name} | ${test.info().title} | ${text}`);
    expect(ms, `${text}; budget ${budget} ms`).toBeLessThanOrEqual(budget);
    return ms;
}
