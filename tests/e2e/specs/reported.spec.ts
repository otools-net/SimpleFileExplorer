// Bugs reported by users on phones, reproduced as they told them (agents/docs/development/mobile-e2e-plan.md, Phase 0).
// Each stays as a regression test once fixed.
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { watchBoxes } from '../fixtures/steady';
import { expectMedianOpensWithin, menuBudget } from '../fixtures/timing';
import { longPress, needsTouchGestures, swipe, tapWhenSettled } from '../fixtures/touch';

const folder = 'Photos/2025 Mountains';
const file = `${folder}/Summit.jpg`;
const menu = '.item-menu[role="menu"]';
const bar = '.selection-dock .selection-bar';


test.describe('@reported @timing 2026-10-29: select an item, open its menu: the bottom bar jumps, the menu opens late', () => {
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
        await expectMedianOpensWithin(page, menu, () => tile.locator('.item-menu-button').tap(), () => closeMenu(page), menuBudget());
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
        await expectMedianOpensWithin(page, menu, () => tile.locator('.item-menu-button').tap(), () => closeMenu(page), menuBudget());
        await watch.expectSteady();
        await expect(tile).toHaveClass(/selected/);
    });
});

// The jump itself needs a phone: Android Chrome brings back its address bar (hidden by scrolling down) when the page's
// root stops scrolling (`overflow: hidden`), the viewport gets shorter and everything fixed at the bottom moves up.
// Emulation has no address bar, so this checks the cause: a sheet on a touch screen holds the page without touching the
// root's overflow, and the page still doesn't scroll behind it.
test.describe('@reported 2026-10-29: a sheet holds the page without changing the root (the address bar would come back)', () => {
    test('the root keeps its overflow while the item menu is open', async ({ page }) => {
        test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 600));
        const root = () => page.evaluate(() => {
            const style = getComputedStyle(document.documentElement);
            return `${style.overflowX} ${style.overflowY}`;
        });
        const before = await root();
        await tapWhenSettled(page, item(page, 'Many photos/Photo 020.jpg').locator('.item-menu-button'));
        await expect(page.locator(menu)).toBeVisible();
        expect(await root()).toBe(before);
        await closeMenu(page);
    });

    test('swipes on the backdrop and on the sheet do not scroll the page', async ({ page, browserName }) => {
        test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
        needsTouchGestures(browserName);
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 600));
        await tapWhenSettled(page, item(page, 'Many photos/Photo 020.jpg').locator('.item-menu-button'));
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

        // On the backdrop, above the sheet: no scroll either, and the sheet stays (only a tap there closes it).
        const top = (await page.locator(menu).boundingBox())!.y;
        await swipe(page, { x: width / 2, y: top - 20 }, { x: width / 2, y: Math.max(10, top - 170) });
        await page.waitForTimeout(300);
        await expect(page.locator(menu)).toBeVisible();
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY), 'swipe on the backdrop').toBeLessThan(20);
        await closeMenu(page);
    });
});

// Found by the suite (2026-10-29): the sheet closed at the press on its backdrop, so the tap's click went to what was
// under it (the sidebar button at the top left opened the sidebar; on the listing, a file opened).
test('@reported a tap on the dimmed area closes the menu and nothing under it', async ({ page }) => {
    test.skip(!isSheetWidth(page), 'sheets: phones up to 640 px');
    await openFolder(page, folder);
    const sidebar = page.getByRole('button', { name: 'Sidebar' });
    const box = (await sidebar.boundingBox())!;
    for (let i = 0; i < 5; i++) {
        await item(page, file).locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2);
        await expect(page.locator(menu)).toBeHidden();
        await expect(sidebar).toHaveAttribute('aria-expanded', 'false');
    }
});

// The same for dialogs shown as sheets on phones (rename, tags, properties…: `dialog.sheet`, base.css).
test.describe('@reported 2026-10-29: a dialog sheet holds the page without changing the root', () => {
    async function openRename(page: import('@playwright/test').Page) {
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 600));
        await tapWhenSettled(page, item(page, 'Many photos/Photo 020.jpg').locator('.item-menu-button'));
        await page.locator(`${menu} [data-entry="rename"]`).tap();
        const dialog = page.locator('dialog.sheet[open]');
        await expect(dialog).toBeVisible();
        return dialog;
    }

    test('the root keeps its overflow while a dialog sheet is open', async ({ page }) => {
        test.skip(page.viewportSize()!.width > 760, 'dialog sheets: up to 760 px');
        const root = () => page.evaluate(() => getComputedStyle(document.documentElement).overflowY);
        const before = await root();
        const dialog = await openRename(page);
        expect(await root()).toBe(before);
        await dialog.getByRole('button', { name: 'Close' }).tap();
        await expect(dialog).toBeHidden();
    });

    test('swipes on a dialog sheet and on the dimmed area do not scroll the page', async ({ page, browserName }) => {
        test.skip(page.viewportSize()!.width > 760, 'dialog sheets: up to 760 px');
        needsTouchGestures(browserName);
        const dialog = await openRename(page);
        const scrollY = await page.evaluate(() => window.scrollY);
        const width = page.viewportSize()!.width;
        const box = (await dialog.boundingBox())!;
        await swipe(page, { x: width / 2, y: box.y + 20 }, { x: width / 2, y: box.y - 130 });
        await swipe(page, { x: width / 2, y: Math.max(20, box.y - 30) }, { x: width / 2, y: Math.max(10, box.y - 180) });
        await page.waitForTimeout(300);
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY)).toBeLessThan(20);
        await expect(dialog).toBeVisible();
    });

    test('a finger still scrolls the body of the dialog', async ({ page, browserName }) => {
        test.skip(page.viewportSize()!.width > 760, 'dialog sheets: up to 760 px');
        needsTouchGestures(browserName);
        const dialog = await openRename(page);
        // More content than the screen holds (a long properties sheet, a batch rename preview…).
        const body = dialog.locator('.sheet-body');
        await body.evaluate(element => element.insertAdjacentHTML('beforeend', '<div style="flex: none; height: 2000px">tall</div>'));
        const scrollY = await page.evaluate(() => window.scrollY);
        const box = (await body.boundingBox())!;
        await swipe(page, { x: box.x + box.width / 2, y: box.y + box.height - 20 }, { x: box.x + box.width / 2, y: box.y + 20 });
        await page.waitForTimeout(300);
        expect(await body.evaluate(element => element.scrollTop)).toBeGreaterThan(50);
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY)).toBeLessThan(20);
    });
});
