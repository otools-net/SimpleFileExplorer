// @diagnose (SFE_E2E_DIAGNOSE=1, Chromium): the main thread's work for listing 600 photos and scrolling through them,
// with a page change put in by `variant` (what-if), from Chromium's cumulative metrics (script, layout, style).
// Run on android-slow. Mobile e2e plan: kept to weigh layout changes of the listing.
import { expect, test } from '../../fixtures/test';
import { swipe } from '../../fixtures/touch';

const variants: Record<string, string> = {
    'as shipped': '',
    // Before 2026-10-29: the captions skipped off screen too.
    'captions skipped': '.tile .info { content-visibility: auto; contain-intrinsic-size: auto 72px; }',
};

for (const [name, css] of Object.entries(variants)) {
    test(`@diagnose @timing listing cost, ${name}`, async ({ page, browserName }) => {
        test.skip(process.env.SFE_E2E_DIAGNOSE !== '1', 'diagnostics: SFE_E2E_DIAGNOSE=1');
        test.skip(browserName !== 'chromium', 'Chromium metrics');
        const cdp = await page.context().newCDPSession(page);
        await cdp.send('Performance.enable');
        const metrics = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map(m => [m.name, m.value]));
        if (css)
            await page.addInitScript(css => addEventListener('DOMContentLoaded', () => {
                const style = document.createElement('style');
                style.textContent = css;
                document.head.append(style);
            }), css);

        const start = await metrics();
        await page.goto('./?path=Many%20photos');
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        await page.waitForTimeout(1500);
        const listed = await metrics();
        const width = page.viewportSize()!.width;
        for (let i = 0; i < 6; i++)
            await swipe(page, { x: width / 2, y: 600 }, { x: width / 2, y: 100 }, 200);
        await page.waitForTimeout(1500);
        const scrolled = await metrics();

        const ms = (a: Record<string, number>, b: Record<string, number>, key: string) => ((b[key] - a[key]) * 1000).toFixed(0);
        const part = (a: Record<string, number>, b: Record<string, number>) =>
            `task ${ms(a, b, 'TaskDuration')}, script ${ms(a, b, 'ScriptDuration')}, layout ${ms(a, b, 'LayoutDuration')} (${b.LayoutCount - a.LayoutCount}), `
            + `style ${ms(a, b, 'RecalcStyleDuration')} (${b.RecalcStyleCount - a.RecalcStyleCount}) ms`;
        console.log(`[cost] ${test.info().project.name} | ${name}\n  listing: ${part(start, listed)}\n  scrolling: ${part(listed, scrolled)}`
            + `\n  scrolled to ${await page.evaluate(() => scrollY)}, ${await page.locator('sfe-listing [data-path]').count()} items`);
    });
}
