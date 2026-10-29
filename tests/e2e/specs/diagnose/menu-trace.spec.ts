// @diagnose (SFE_E2E_DIAGNOSE=1, Chromium): where the item menu's opening time goes, from a performance trace of the
// tap: script, style recalculation (and how many elements it touched), layout, paint, summed between the touch and
// the frame showing the menu. The client is minified, so this splits by kind of work, not by function.
// Run on android-slow for a phone's proportions. Mobile e2e plan, Phase 0.
import { chromium } from '@playwright/test';
import { withoutPageLock } from '../../fixtures/diagnose';
import { expect, item, openFolder, test } from '../../fixtures/test';

type TraceEvent = { name: string; ph: string; ts: number; dur?: number; args?: { elementCount?: number; data?: Record<string, unknown> }; tid: number };

for (const [label, folder, file] of [
    ['6 photos', 'Photos/2025 Mountains', 'Photos/2025 Mountains/Summit.jpg'],
    ['600 photos', 'Many photos', 'Many photos/Photo 002.jpg'],
] as const) {
  for (const lock of [true, false]) {
    test(`@diagnose @timing trace of the item menu, ${label}, page lock ${lock ? 'on' : 'off'}`, async ({ page, browser, browserName }) => {
        test.skip(process.env.SFE_E2E_DIAGNOSE !== '1', 'diagnostics: SFE_E2E_DIAGNOSE=1');
        test.skip(browserName !== 'chromium', 'Chromium traces');
        await openFolder(page, folder);
        if (!lock)
            expect(await withoutPageLock(page)).toBeGreaterThan(0);
        const tile = item(page, file);
        await tile.locator('.select-box').tap();
        await expect(tile).toHaveClass(/selected/);
        await page.waitForTimeout(600);

        await (browser as unknown as typeof chromium extends { launch(): Promise<infer B> } ? B : never)
            .startTracing(page, { categories: ['devtools.timeline', 'disabled-by-default-devtools.timeline', 'blink.user_timing'] });
        await page.evaluate(() => performance.mark('e2e-before-tap'));
        await tile.locator('.item-menu-button').tap();
        await expect(page.locator('.item-menu[role="menu"]')).toBeVisible();
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        const trace = JSON.parse((await (browser as unknown as { stopTracing(): Promise<Buffer> }).stopTracing()).toString()) as { traceEvents: TraceEvent[] };

        // The renderer's main thread: the one running the page's scripts.
        const events = trace.traceEvents.filter(e => e.ph === 'X' && e.dur !== undefined);
        const main = events.find(e => e.name === 'EventDispatch' && (e.args?.data as { type?: string })?.type === 'click')?.tid;
        const start = events.filter(e => e.tid === main && e.name === 'EventDispatch' && /touch|pointer/.test(String((e.args?.data as { type?: string })?.type)))
            .reduce((min, e) => Math.min(min, e.ts), Infinity);
        const onMain = events.filter(e => e.tid === main && e.ts >= start);
        const sum = (name: string) => onMain.filter(e => e.name === name).reduce((total, e) => total + e.dur!, 0) / 1000;
        const recalcs = onMain.filter(e => e.name === 'UpdateLayoutTree');
        const elements = recalcs.map(e => e.args?.elementCount ?? (e.args?.data as { elementCount?: number })?.elementCount ?? 0);
        const layouts = onMain.filter(e => e.name === 'Layout');
        const dispatches = onMain.filter(e => e.name === 'EventDispatch').map(e => `${(e.args?.data as { type?: string })?.type} ${(e.dur! / 1000).toFixed(1)}`);
        const longest = [...onMain].filter(e => e.name === 'FunctionCall' || e.name === 'EventDispatch' || e.name === 'FireAnimationFrame' || e.name === 'TimerFire' || e.name === 'RunMicrotasks')
            .sort((a, b) => b.dur! - a.dur!).slice(0, 6).map(e => `${e.name} ${(e.dur! / 1000).toFixed(1)}`);
        console.log([
            `[trace] ${test.info().project.name} | ${label}, lock ${lock ? 'on' : 'off'}`,
            `  events: ${dispatches.join(', ')}`,
            `  script (EventDispatch+FunctionCall+microtasks, nested counted once at top): longest ${longest.join(', ')}`,
            `  style recalcs: ${recalcs.length}, ${sum('UpdateLayoutTree').toFixed(1)} ms, elements ${elements.join('/')}`,
            `  layouts: ${layouts.length}, ${sum('Layout').toFixed(1)} ms; paint ${sum('Paint').toFixed(1)} ms; `
                + `pre-paint ${sum('PrePaint').toFixed(1)} ms; layerize ${sum('Layerize').toFixed(1)} ms`,
            `  forced (inside script): ${onMain.filter(e => e.name === 'UpdateLayoutTree' || e.name === 'Layout')
                .filter(e => onMain.some(s => (s.name === 'FunctionCall' || s.name === 'EventDispatch') && s.ts <= e.ts && s.ts + s.dur! >= e.ts + e.dur!))
                .map(e => `${e.name} ${(e.dur! / 1000).toFixed(1)}`).join(', ') || 'none'}`,
        ].join('\n'));
    });
  }
}
