// Every menu that opens at the bottom of a phone (mobile e2e plan, Phase 2, menus and sheets): a sheet across the
// width, against the bottom, opened by one tap within the budget, closed by a tap on the dimmed area.
import type { Page } from '@playwright/test';
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { expectTapTargets } from '../fixtures/layout';
import { expectMedianOpensWithin, menuBudget } from '../fixtures/timing';

const menu = '.item-menu[role="menu"]';

async function expectSheet(page: Page): Promise<void> {
    if (!isSheetWidth(page))
        return;
    const box = (await page.locator(menu).boundingBox())!;
    const viewport = page.viewportSize()!;
    expect(box.x).toBe(0);
    expect(Math.round(box.width)).toBe(viewport.width);
    expect(Math.round(box.y + box.height)).toBe(viewport.height);
    await expectTapTargets(page, menu);
}

test.describe('menus', () => {
    test('the folder menu ("⋯" of the location bar)', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        // Phones: the location bar's "⋯"; wider (landscape), the folder's own buttons: no menu to check there.
        const more = page.locator('button.location-more');
        test.skip(!await more.isVisible(), 'the folder menu is a "⋯" up to 640 px');
        await more.tap();
        await expect(page.locator(menu)).toBeVisible();
        await expectSheet(page);
        await closeMenu(page);
        await expectMedianOpensWithin(page, menu, () => more.tap(), () => closeMenu(page), menuBudget());
    });

    test('the "⋯" of the selection bar', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.select-box').tap();
        const more = page.locator('.selection-bar .overflow-more.shown');
        await expect(page.locator('.selection-bar')).toBeVisible();
        test.skip(!await more.isVisible(), 'every button fits at this width');
        await more.tap();
        await expect(page.locator(menu)).toBeVisible();
        await expectSheet(page);
        await closeMenu(page);
        await expectMedianOpensWithin(page, menu, () => more.tap(), () => closeMenu(page), menuBudget());
    });

    test('the ⋮ of the viewer', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-link').first().tap();
        const more = page.locator('dialog.viewer[open] .viewer-menu-button');
        await more.tap();
        await expect(page.locator(menu)).toBeVisible();
        await expectSheet(page);
        await closeMenu(page);
        await expect(page.locator('dialog.viewer[open]'), 'closing the menu leaves the viewer open').toHaveCount(1);
        await expectMedianOpensWithin(page, menu, () => more.tap(), () => closeMenu(page), menuBudget());
    });

    test('a long item menu fits the screen and scrolls inside itself', async ({ page }) => {
        test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-menu-button').tap();
        const sheet = page.locator(menu);
        await expect(sheet).toBeVisible();
        const box = (await sheet.boundingBox())!;
        expect(box.height, 'no taller than 75 % of the screen').toBeLessThanOrEqual(page.viewportSize()!.height * 0.75 + 1);
        const last = sheet.locator('.item-menu-entry').last();
        await last.scrollIntoViewIfNeeded();
        await expect(last).toBeInViewport();
        await closeMenu(page);
    });
});
