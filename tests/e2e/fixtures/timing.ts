// "The menu doesn't open at once": the time from the finger lifted (the last `touchend`, else `pointerup`/`click`) to
// the first animation frame where `selector` is shown, measured in the page. One tap must be enough: a menu that
// needs a second one never shows within the wait and fails. The time is split at the `click` (before it: the browser,
// a tap delay waiting for a double tap) and at the element entering the page (before it: the app's script; after it:
// style, layout and paint until the frame that has it).
import { expect, test, type Page } from '@playwright/test';

declare global {
    interface Window { __sfeOpen?: { lifted: number | null; clicked: number | null; added: number | null; shown: number | null; log: string[] } }
}

/** Runs `action` (one tap) and returns the ms until `selector` shows; fails if it doesn't show in `budget` ms. */
export async function expectOpensWithin(page: Page, selector: string, action: () => Promise<void>, budget = 100): Promise<number> {
    await page.evaluate(selector => {
        const open = { lifted: null as number | null, clicked: null as number | null, added: null as number | null, shown: null as number | null, log: [] as string[] };
        // What happened, for the failure message: where each event of the tap went, the element coming and going.
        const t0 = performance.now();
        const describe = (target: EventTarget | null) => {
            const element = target as Element | null;
            const control = element?.closest?.('button, a, label, [data-path], .sheet-backdrop');
            return control ? `${control.tagName.toLowerCase()}.${String(control.className).split(' ').slice(0, 2).join('.')}` : element?.tagName ?? '?';
        };
        for (const type of ['pointerdown', 'touchend', 'click'])
            document.addEventListener(type, event => open.log.push(`${(performance.now() - t0).toFixed(0)} ${type} ${describe(event.target)}`), { capture: true });
        let present = false;
        new MutationObserver(() => {
            const now = !!document.querySelector(selector);
            if (now !== present)
                open.log.push(`${(performance.now() - t0).toFixed(0)} ${now ? 'in' : 'out'}`);
            present = now;
        }).observe(document.body, { childList: true, subtree: true });
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
    try {
        await expect(page.locator(selector)).toBeVisible({ timeout: 2000 });
    } catch {
        const log = await page.evaluate(() => window.__sfeOpen!.log);
        throw new Error(`${selector} not shown after one tap; events: ${log.join(' | ')}`);
    }
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

/**
 * Opens with `open` and closes with `close` `times` times; the median time to open must be within `budget` (single
 * taps swing too much on a loaded machine: 80–600 ms at 4× CPU for the same menu).
 */
export async function expectMedianOpensWithin(page: Page, selector: string, open: () => Promise<void>, close: () => Promise<void>,
    budget: number, times = 5): Promise<number> {
    const samples: number[] = [];
    for (let i = 0; i < times; i++) {
        samples.push(await expectOpensWithin(page, selector, open, Number.POSITIVE_INFINITY));
        await close();
    }
    samples.sort((a, b) => a - b);
    const median = samples[Math.floor(samples.length / 2)];
    test.info().annotations.push({ type: 'opens (median)', description: `${selector}: ${median.toFixed(0)} ms of ${samples.map(s => s.toFixed(0)).join(', ')}` });
    expect(median, `${selector}: median ${median.toFixed(0)} ms of ${samples.map(s => s.toFixed(0)).join(', ')}; budget ${budget} ms`).toBeLessThanOrEqual(budget);
    return median;
}
