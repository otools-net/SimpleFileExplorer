// Pages besides the listing on a phone: a file's own page (`view?path=`), the help, the theme from the
// first paint.
import { expect, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test('a file on its own page (view?path=) fits, with buttons big enough', async ({ page }) => {
    await page.goto('./view?path=' + encodeURIComponent('Documents/Hiking guide.md'));
    const view = page.locator('sfe-view-page sfe-markdown-view');
    await expect(view).toBeVisible();
    await expect
        .poll(() => view.evaluate((element) => (element as HTMLElement).innerText.trim().length))
        .toBeGreaterThan(20);
    await expectFitsScreen(page);
    await expectNothingCut(page);
    await expectTapTargets(page, 'sfe-view-page');
});

test('the help fits, its contents open from the list button and lead to a page', async ({ page }) => {
    await page.goto('./help');
    const article = page.locator('sfe-help-page .markdown-content');
    await expect(article.locator('h1')).toHaveText('SFE documentation');
    await expectFitsScreen(page);
    await expectNothingCut(page);
    await expectTapTargets(page, 'sfe-help-page .help-bar');

    await page.getByRole('button', { name: 'Contents' }).tap();
    const contents = page.locator('#help-contents');
    await expect(contents).toBeVisible();
    await contents.getByRole('link', { name: 'Browsing' }).tap();
    await expect(article.locator('h1')).toHaveText('Browsing');
    await expect(contents).toBeHidden();
    // Its pictures come from the help's own files.
    const picture = article.locator('img').first();
    await picture.scrollIntoViewIfNeeded();
    await expect.poll(() => picture.evaluate((image) => (image as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
});

for (const scheme of ['light', 'dark'] as const) {
    test.describe(`${scheme} theme`, () => {
        test.use({ colorScheme: scheme });

        // theme-boot.ts sets the theme before Angular starts: no flash of the other one on a phone's slower start.
        test('the first paint already has the theme', async ({ page }) => {
            await page.addInitScript(() => {
                addEventListener('DOMContentLoaded', () => {
                    (window as unknown as { __first: string }).__first = getComputedStyle(
                        document.body,
                    ).backgroundColor;
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
