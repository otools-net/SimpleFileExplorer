// @smoke: the suite works at all on each device: signed in, a folder listed, a file's menu opened and closed.
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';

test.describe('@smoke @desktop', () => {
    test('lists a folder and opens a file menu', async ({ page, isMobile }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const tile = item(page, 'Photos/2025 Mountains/Summit.jpg');
        await expect(tile).toBeVisible();

        const menuButton = tile.locator('.item-menu-button');
        if (isMobile)
            await menuButton.tap();
        else
            await menuButton.click();
        const menu = page.locator('.item-menu[role="menu"]');
        await expect(menu).toBeVisible();
        await expect(menu.locator('.item-menu-title')).toHaveText('Summit.jpg');

        if (isSheetWidth(page)) {
            // Phones: a sheet across the bottom of the screen.
            const box = (await menu.boundingBox())!;
            const viewport = page.viewportSize()!;
            expect(box.x).toBe(0);
            expect(Math.round(box.width)).toBe(viewport.width);
            expect(Math.round(box.y + box.height)).toBe(viewport.height);
        }
        await closeMenu(page);
    });
});
