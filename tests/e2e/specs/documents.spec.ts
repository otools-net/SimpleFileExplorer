// Using the document viewers by finger (mobile e2e plan, Phase 2, viewer): find in a text, its reading settings ("Aa"),
// a PDF's zoom and find, an EPUB's page turns, an archive's folders. Opening each format: viewers.spec.ts.
import type { Locator, Page } from '@playwright/test';
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

const viewer = (page: Page) => page.locator('dialog.viewer[open]');

async function open(page: Page, file: string, view: string): Promise<Locator> {
    await openFolder(page, file.slice(0, file.lastIndexOf('/')));
    await item(page, file).locator('.item-link').first().tap();
    const shown = viewer(page).locator(view);
    await expect(shown).toBeVisible();
    return shown;
}

/** A button of a toolbar by finger: in the row, or in its "⋯" menu (`entry`) on a narrow screen. */
async function toolbarAction(page: Page, toolbar: Locator, button: Locator, entry: string): Promise<void> {
    if (await button.isVisible()) {
        await button.tap();
        return;
    }
    await toolbar.locator('.overflow-more.shown').tap();
    await page.locator(`.item-menu[role="menu"] [data-entry="${entry}"]`).tap();
}

test.describe('documents by finger', () => {
    test('a text: find a word, then its reading settings make the text larger', async ({ page }) => {
        const text = await open(page, 'Documents/Packing list.txt', 'sfe-text-view');
        const toolbar = text.locator('.text-toolbar');
        await toolbarAction(page, toolbar, toolbar.getByRole('button', { name: 'Find', exact: true }), 'find');
        const field = text.locator('.cm-search input[name=search]');
        await expect(field).toBeVisible();
        await field.fill('Camera');
        await field.press('Enter');
        await expect(text.locator('.cm-searchMatch').first()).toBeVisible();
        await expectFitsScreen(page);
        await field.press('Escape');

        const size = () => text.locator('.cm-content').evaluate(content => parseFloat(getComputedStyle(content).fontSize));
        const before = await size();
        await toolbarAction(page, toolbar, toolbar.locator('[data-reading-toggle]'), 'reading');
        const panel = text.locator('sfe-reading-panel, .reading-panel').first();
        await expect(panel).toBeVisible();
        await expectFitsScreen(page);
        await expectNothingCut(page, '.reading-panel');
        await expectTapTargets(page, '.reading-panel');
        await panel.getByRole('button', { name: 'Larger' }).tap();
        await expect.poll(size).toBeGreaterThan(before);
        // Back as it was: the reading settings are this browser's.
        await panel.getByRole('button', { name: 'Smaller' }).tap();
        await expect.poll(size).toBe(before);
    });

    test('a PDF: larger and smaller by its buttons, a word found', async ({ page }) => {
        const pdf = await open(page, 'Documents/Hut handbook.pdf', 'sfe-pdf-view');
        const page1 = pdf.locator('canvas').first();
        await expect(page1).toBeVisible({ timeout: 15_000 });
        const width = async () => (await page1.boundingBox())!.width;
        const before = await width();
        const toolbar = pdf.locator('.text-toolbar');
        await toolbar.locator('[data-zoom-in]').tap();
        await expect.poll(width).toBeGreaterThan(before * 1.05);
        await toolbar.locator('[data-zoom-out]').tap();
        await expect.poll(width).toBeLessThan(before * 1.05);

        // Phones: a magnifier shows the field; wider, the field is there.
        const toggle = toolbar.locator('[data-find-toggle]');
        if (await toggle.isVisible())
            await toggle.tap();
        const field = toolbar.locator('input[data-find]');
        await expect(field).toBeVisible();
        await expectFitsScreen(page);
        await field.fill('hut');
        await expect(toolbar.locator('[data-matches]')).toHaveText(/\d+ of \d+/);
    });

    test('an archive: into a folder by a tap, Up back out, Go to folder', async ({ page }) => {
        const archive = await open(page, 'Documents/Mountain trip.zip', 'sfe-archive-view');
        const folder = archive.locator('.archive-row[data-kind="folder"] .archive-open').first();
        await expect(folder).toBeVisible();
        const name = (await folder.textContent())!.trim();
        await folder.tap();
        await expect(archive.locator('.archive-crumb.current')).toHaveText(name);
        await archive.locator('[data-up]').tap();
        await expect(archive.locator('.archive-crumb.current')).toHaveCount(0);

        const go = archive.locator('input[data-go]');
        await go.tap();
        await go.fill(name);
        await go.press('Enter');
        await expect(archive.locator('.archive-crumb.current')).toHaveText(name);
        await expectFitsScreen(page);
        await expectNothingCut(page, 'sfe-archive-view');
    });
});

test.describe('an EPUB by finger', () => {
    // epub.js asks every book for Apple's optional display options (404 when it has none); its pages' frame runs no
    // scripts (epub-reader.md).
    test.use({ allowedErrors: /status of 404|frame is sandboxed/ });

    test('an EPUB: next page and back by its buttons', async ({ page }) => {
        const book = await open(page, 'Documents/Hut stories.epub', 'sfe-epub-view');
        const next = book.locator('[data-next]');
        await expect(next).toBeEnabled({ timeout: 15_000 });
        const place = () => book.locator('iframe').first().evaluate(frame => {
            const view = (frame as HTMLIFrameElement).contentWindow;
            return `${frame.getAttribute('src') ?? ''}#${view?.scrollX ?? 0}:${(frame as HTMLElement).style.transform ?? ''}:${(frame.parentElement as HTMLElement).scrollLeft}`;
        });
        const progress = book.locator('[data-progress]');
        const start = await progress.textContent();
        const before = await place();
        await next.tap();
        await expect.poll(async () => `${await place()}|${await progress.textContent()}`).not.toBe(`${before}|${start}`);
        await book.locator('[data-previous]').tap();
        await expect.poll(place).toBe(before);
    });
});
