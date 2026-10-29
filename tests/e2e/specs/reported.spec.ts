// Bugs reported by users on phones, reproduced as they told them (agents/docs/development/mobile-e2e-plan.md, Phase 0).
// Each stays as a regression test once fixed.
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { watchBoxes } from '../fixtures/steady';
import { expectOpensWithin } from '../fixtures/timing';
import { longPress, needsTouchGestures, swipe } from '../fixtures/touch';

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

// The jump itself needs a phone: Android Chrome brings back its address bar (hidden by scrolling down) when the page's
// root stops scrolling (`overflow: hidden`), the viewport gets shorter and everything fixed at the bottom moves up.
// Emulation has no address bar, so this checks the cause: a sheet on a touch screen holds the page without touching the
// root's overflow, and the page still doesn't scroll behind it.
test.describe('2026-10-29: a sheet holds the page without changing the root (the address bar would come back)', () => {
    test('the root keeps its overflow while the item menu is open', async ({ page }) => {
        test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 600));
        const root = () => page.evaluate(() => {
            const style = getComputedStyle(document.documentElement);
            return `${style.overflowX} ${style.overflowY}`;
        });
        const before = await root();
        await item(page, 'Many photos/Photo 020.jpg').locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        expect(await root()).toBe(before);
        await closeMenu(page);
    });

    test('swipes on the backdrop and on the sheet do not scroll the page', async ({ page, browserName }) => {
        test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
        needsTouchGestures(browserName);
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 600));
        await item(page, 'Many photos/Photo 020.jpg').locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        const scrollY = await page.evaluate(() => window.scrollY);
        const width = page.viewportSize()!.width;

        // On the sheet: it stays open, the page stays.
        const title = (await page.locator(`${menu} .item-menu-title`).boundingBox())!;
        await swipe(page, { x: width / 2, y: title.y + 10 }, { x: width / 2, y: title.y - 150 });
        await page.waitForTimeout(300);
        await expect(page.locator(menu)).toBeVisible();
        // A few px can come from previews loading above (scroll anchoring); scrolling the page would be the swipe's 150.
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY), 'swipe on the sheet').toBeLessThan(20);

        // On the backdrop, above the sheet: a finger outside closes it, and the swipe doesn't scroll the page either.
        const top = (await page.locator(menu).boundingBox())!.y;
        await swipe(page, { x: width / 2, y: top - 20 }, { x: width / 2, y: Math.max(10, top - 170) });
        await page.waitForTimeout(300);
        await expect(page.locator(menu)).toBeHidden();
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY), 'swipe on the backdrop').toBeLessThan(20);
    });
});
