// Pages besides the listing on a phone: a file's own page (`view?path=`), the theme from the
// first paint.
import { expect, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test('a file on its own page (view?path=) fits, with buttons big enough', async ({ page }) => {
    await page.goto('./view?path=' + encodeURIComponent('Documents/Hiking guide.md'));
    const view = page.locator('sfe-view-page sfe-markdown-view');
    await expect(view).toBeVisible();
    await expect.poll(() => view.evaluate(element => (element as HTMLElement).innerText.trim().length)).toBeGreaterThan(20);
    await expectFitsScreen(page);
    await expectNothingCut(page);
    await expectTapTargets(page, 'sfe-view-page');
});

for (const scheme of ['light', 'dark'] as const) {
    test.describe(`${scheme} theme`, () => {
        test.use({ colorScheme: scheme });

        // theme-boot.ts sets the theme before Angular starts: no flash of the other one on a phone's slower start.
        test('the first paint already has the theme', async ({ page }) => {
            await page.addInitScript(() => {
                addEventListener('DOMContentLoaded', () => {
                    (window as unknown as { __first: string }).__first = getComputedStyle(document.body).backgroundColor;
                });
            });
            await page.goto('./?path=Photos');
            await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
            const { first, last } = await page.evaluate(() => ({
                first: (window as unknown as { __first: string }).__first,
                last: getComputedStyle(document.body).backgroundColor,
            }));
            expect(first, 'background at DOMContentLoaded').toBe(last);
            const [r, g, b] = last.match(/\d+/g)!.map(Number);
            expect((r + g + b) / 3 > 128 ? 'light' : 'dark').toBe(scheme);
        });
    });
}
