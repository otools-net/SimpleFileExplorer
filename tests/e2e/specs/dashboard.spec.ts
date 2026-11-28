// The dashboard on a phone: its cards one under the other, nothing wider than the screen, and Files back to the
// listing. The start page itself (the dashboard at the address alone) has unit specs: the e2e library's start page is
// its top (global-setup.ts), so the other specs open the listing at `./`.
import { expect, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test('the dashboard shows its cards in one column and leads to the files', async ({ page }) => {
    await page.goto('./dashboard');
    const places = page.locator('[data-widget="places"]');
    const storage = page.locator('[data-widget="storage"]');
    await expect(places).toBeVisible();
    await expect(storage.locator('.storage-line')).toBeVisible();
    await expect(page.locator('[data-widget="notifications"]')).toBeVisible();
    await expect(page.locator('[data-widget="files"] [data-folder]').first()).toBeVisible();
    await expect(page.locator('[data-widget="uploads"]')).toContainText('No uploads under way');
    await expect(page.locator('[data-widget="tags"] [data-history="viewed"]')).toBeVisible();

    const width = page.viewportSize()!.width;
    if (width <= 760) {
        const [first, second] = [await places.boundingBox(), await storage.boundingBox()];
        expect(second!.y).toBeGreaterThan(first!.y + first!.height - 1);
        expect(Math.abs(second!.x - first!.x)).toBeLessThan(1);
    }
    await expectFitsScreen(page);
    await expectNothingCut(page, '.dashboard-page');
    await expectTapTargets(page, '.dashboard-topbar');
    await expectTapTargets(page, '.dashboard');

    await page.locator('.dashboard-files').tap();
    await expect(page.locator('sfe-listing')).toBeVisible();
    expect(new URL(page.url()).pathname).not.toContain('dashboard');
});
