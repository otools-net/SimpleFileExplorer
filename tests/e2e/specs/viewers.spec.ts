// The document viewers on a phone (mobile e2e plan, Phase 2, viewer): each format opens by a tap from its folder, shows
// something, fits the screen, its fields don't make iOS zoom and its buttons are big enough for a finger.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';

const documents: [file: string, view: string, allowed?: RegExp][] = [
    ['Documents/Packing list.txt', 'sfe-text-view'],
    ['Documents/Hiking guide.md', 'sfe-markdown-view'],
    ['Documents/Trip plan.json', 'sfe-json-view'],
    ['Documents/Huts.csv', 'sfe-csv-view'],
    ['Documents/Hut handbook.pdf', 'sfe-pdf-view'],
    // epub.js asks every book for Apple's optional display options (404 when it has none), and the book's pages are
    // in a frame that may not run scripts (by design: epub-reader.md).
    ['Documents/Hut stories.epub', 'sfe-epub-view', /status of 404|frame is sandboxed/],
    ['Documents/Mountain trip.zip', 'sfe-archive-view'],
    ['Videos/Holidays/Campfire.en.srt', 'sfe-subtitles-view'],
];

for (const [file, view, allowed] of documents) {
    test.describe('document viewers', () => {
        test.use({ allowedErrors: allowed ?? null });
        test(`${file.split('/').pop()} (${view})`, async ({ page }) => {
            const folder = file.slice(0, file.lastIndexOf('/'));
            await openFolder(page, folder);
            await item(page, file).locator('.item-link').first().tap();
            const viewer = page.locator('dialog.viewer[open]');
            const shown = viewer.locator(view);
            await expect(shown).toBeVisible();
            // Something in it: text, a page drawn (PDF), a book's frame (EPUB).
            await expect.poll(() => shown.evaluate(element =>
                (element as HTMLElement).innerText.trim().length > 10 || !!element.querySelector('canvas, iframe')), { timeout: 15_000 }).toBe(true);
            await page.waitForTimeout(500);
            await expectFitsScreen(page);
            await expectNothingCut(page);
            await expectNoZoomOnFocus(page, 'dialog.viewer[open] :is(input:not([type=checkbox]):not([type=radio]), textarea, select)');
            await expectTapTargets(page, 'dialog.viewer[open] :is(.viewer-bar, [class*=toolbar])');
        });
    });
}
