// The home folder on a phone: chosen from a folder's menu, then Home at the top of the sidebar (an overlay on phones,
// closed by opening it). The home folder is the account's, shared by the projects running at once: the test only
// expects one of the runs' folders, and forgets the choice at the end.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test('choose a home folder from its menu and open it from the sidebar', async ({ page, scratch }) => {
    const folder = `${scratch}/Inside`;
    await openFolder(page, scratch);
    await item(page, folder).locator('.item-menu-button').tap();
    await page.locator('.item-menu[role="menu"] [data-entry="homeFolder"]').tap();
    await expect(page.locator('.toast').filter({ hasText: 'is now your home folder' })).toBeVisible();

    const toggle = page.locator('.sidebar-toggle');
    const home = page.locator('.home-link [data-home-folder] .quick-link-target');
    if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.tap();
    await expect(home).toBeVisible();
    await expectFitsScreen(page);
    await expectNothingCut(page, '#sidebar');
    await expectTapTargets(page, '.home-link');

    await home.tap();
    await expect.poll(() => new URL(page.url()).searchParams.get('path')).toMatch(/^Scratch\/.+\/Inside$/);
    // A phone's sidebar covers the page: opening Home closes it.
    if (page.viewportSize()!.width <= 640) await expect(toggle).toHaveAttribute('aria-expanded', 'false');

    const forgotten = await page.request.delete('api/v1/me/home-folder', { headers: { 'X-SFE-Request': '1' } });
    expect(forgotten.ok()).toBe(true);
});
