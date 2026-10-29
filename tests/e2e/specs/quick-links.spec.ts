// Quick links on a phone (mobile e2e plan, Phase 2): added from an item's menu, opened from the sidebar (an overlay on
// phones, closed by opening a link), removed by a tap. Each test its own folder: the links are the account's, shared
// by the projects running at once.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test('add a folder to the quick links, open it from the sidebar, remove it', async ({ page, scratch }) => {
    const folder = `${scratch}/Inside`;
    await openFolder(page, scratch);
    await item(page, folder).locator('.item-menu-button').tap();
    await page.locator('.item-menu[role="menu"] [data-entry="quickLink"]').tap();

    const toggle = page.locator('.sidebar-toggle');
    const link = page.locator(`.quick-link[data-quick-link="${folder}"]`);
    if (await toggle.getAttribute('aria-expanded') !== 'true')
        await toggle.tap();
    await expect(link).toBeVisible();
    await expectFitsScreen(page);
    await expectNothingCut(page, '#sidebar');
    await expectTapTargets(page, '.quick-link-list');

    await link.locator('.quick-link-target').tap();
    await expect.poll(() => new URL(page.url()).searchParams.get('path')).toBe(folder);
    // A phone's sidebar covers the page: opening a link closes it.
    if (page.viewportSize()!.width <= 640)
        await expect(toggle).toHaveAttribute('aria-expanded', 'false');

    if (await toggle.getAttribute('aria-expanded') !== 'true')
        await toggle.tap();
    await link.locator('.quick-link-remove').tap();
    await expect(link).toHaveCount(0);
});
