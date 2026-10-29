// Bugs reported by users on phones, reproduced as they told them (agents/docs/development/mobile-e2e-plan.md, Phase 0).
// Each stays as a regression test once fixed.
import { closeMenu, expect, item, openFolder, test } from '../fixtures/test';
import { watchBoxes } from '../fixtures/steady';
import { expectOpensWithin } from '../fixtures/timing';
import { longPress, needsTouchGestures } from '../fixtures/touch';

const folder = 'Photos/2025 Mountains';
const file = `${folder}/Summit.jpg`;
const menu = '.item-menu[role="menu"]';
const bar = '.selection-dock .selection-bar';

test.describe('@timing 2026-10-29: select an item, open its menu: the bottom bar jumps, the menu opens late', () => {
    test('long press, then ⋮ (Android)', async ({ page, browserName }) => {
        needsTouchGestures(browserName);
        await openFolder(page, folder);
        const tile = item(page, file);

        await longPress(page, tile);
        await expect(tile).toHaveClass(/selected/);
        await expect(page.locator(bar)).toBeVisible();
        // The bar's own entry animation over first.
        await page.waitForTimeout(400);

        const watch = await watchBoxes(page, { 'selection bar': bar });
        await expectOpensWithin(page, menu, () => tile.locator('.item-menu-button').tap());
        await closeMenu(page);
        await watch.expectSteady();
        await expect(tile).toHaveClass(/selected/);
    });

    test('select box, then ⋮ (iPhone: no long press)', async ({ page }) => {
        await openFolder(page, folder);
        const tile = item(page, file);

        await tile.locator('.select-box').tap();
        await expect(tile).toHaveClass(/selected/);
        await expect(page.locator(bar)).toBeVisible();
        await page.waitForTimeout(400);

        const watch = await watchBoxes(page, { 'selection bar': bar });
        await expectOpensWithin(page, menu, () => tile.locator('.item-menu-button').tap());
        await closeMenu(page);
        await watch.expectSteady();
        await expect(tile).toHaveClass(/selected/);
    });
});
