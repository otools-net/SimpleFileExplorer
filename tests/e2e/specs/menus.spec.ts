// Every menu that opens at the bottom of a phone: a sheet across the
// width, against the bottom, opened by one tap within the budget, closed by a tap on the dimmed area.
import type { Page } from '@playwright/test';
import { closeMenu, expect, isSheetWidth, item, openFolder, quietScans, test } from '../fixtures/test';
import { expectTapTargets } from '../fixtures/layout';
import { expectMedianOpensWithin, menuBudget } from '../fixtures/timing';
import { holdLikeIos } from '../fixtures/touch';

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

// Decided 2026-10-29: Android's Back closes the open menu and stays on the page (a close watcher, Chromium only);
// turning the phone keeps it open, laid out again.
test.describe('closing and turning', () => {
    test('Android\'s Back closes the open menu, and only it', async ({ page, browserName }) => {
        test.skip(browserName !== 'chromium', 'close watchers: Chromium (Android\'s Back); iOS has no Back button');
        // Back reaches the newest close watcher: kept here to ask it, as the system does.
        await page.addInitScript(() => {
            const Native = (window as unknown as { CloseWatcher?: new () => object }).CloseWatcher;
            if (!Native)
                return;
            const made: object[] = [];
            Object.assign(window, { __watchers: made });
            Object.assign(window, { CloseWatcher: class extends Native { constructor() { super(); made.push(this); } } });
        });
        const back = () => page.evaluate(() => (window as unknown as { __watchers: { requestClose(): void }[] }).__watchers.at(-1)!.requestClose());
        await openFolder(page, 'Photos/2025 Mountains');
        const url = page.url();
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        await back();
        await expect(page.locator(menu)).toHaveCount(0);
        expect(page.url()).toBe(url);

        // In the viewer: Back closes its menu, the viewer stays.
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-link').first().tap();
        await page.locator('dialog.viewer[open] .viewer-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        await back();
        await expect(page.locator(menu)).toHaveCount(0);
        await expect(page.locator('dialog.viewer[open]')).toHaveCount(1);
    });

    test('turning the phone keeps the menu open, inside the screen', async ({ page }) => {
        const { width, height } = page.viewportSize()!;
        test.skip(width > height, 'starts upright');
        await openFolder(page, 'Photos/2025 Mountains');
        const tile = item(page, 'Photos/2025 Mountains/Summit.jpg');
        await tile.locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();

        for (const size of [{ width: height, height: width }, { width, height }]) {
            await page.setViewportSize(size);
            await expect(page.locator(menu)).toBeVisible();
            await expect.poll(async () => {
                const box = (await page.locator(menu).boundingBox())!;
                return box.x >= 0 && box.y >= 0 && box.x + box.width <= size.width + 0.5 && box.y + box.height <= size.height + 0.5;
            }, `the menu inside ${size.width}×${size.height}`).toBe(true);
        }
        await expectSheet(page);
        await closeMenu(page);
    });
});

// iOS sends no `contextmenu`: holding an element with a menu opens it all the same (the app's own hold).
test.describe('holding a finger, as on an iPhone', () => {
    // Another test's scan panel coming in moved the page under the finger: the hold ended (CI).
    test.beforeEach(({ page }) => quietScans(page));

    test('a folder of the path: its menu', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const crumb = page.locator('nav.crumbs a[data-path="Photos"]');
        await holdLikeIos(page, crumb);
        await expect(page.locator(menu)).toBeVisible();
        await expect(page.locator(`${menu} .item-menu-title`)).toHaveText('Photos');
        expect(new URL(page.url()).searchParams.get('path'), 'the lift opened nothing').toBe('Photos/2025 Mountains');
        await closeMenu(page);
    });

    test('a listed item: selected, as on Android', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const tile = item(page, 'Photos/2025 Mountains/Summit.jpg');
        await holdLikeIos(page, tile.locator('.preview'));
        await expect(tile).toHaveClass(/selected/);
        await expect(page.locator(menu)).toHaveCount(0);
        await expect(page.locator('dialog.viewer[open]'), 'the lift opened nothing').toHaveCount(0);
    });
});

test.describe('after a menu', () => {
    // Another test's scan panel coming in moved the listing (4 px, CI's landscape run).
    test.beforeEach(({ page }) => quietScans(page));

    test('closed, the listing is where it was, the selection kept, no keyboard brought up', async ({ page }) => {
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 1500));
        const photo = item(page, 'Many photos/Photo 030.jpg');
        await photo.scrollIntoViewIfNeeded();
        await page.waitForTimeout(300);
        await photo.locator('.select-box').tap();
        await expect(photo).toHaveClass(/selected/);
        const scrollY = await page.evaluate(() => window.scrollY);

        await photo.locator('.item-menu-button').tap();
        await expect(page.locator(menu)).toBeVisible();
        await closeMenu(page);
        await expect(page.locator(menu)).toHaveCount(0);
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY), 'the listing did not move').toBeLessThanOrEqual(2);
        await expect(photo).toHaveClass(/selected/);
        const focused = await page.evaluate(() => {
            const element = document.activeElement;
            return element?.matches('input:not([type=checkbox]):not([type=radio]), textarea, [contenteditable=true]') ? element.outerHTML.slice(0, 80) : null;
        });
        expect(focused, 'a text field focused (the keyboard would come up)').toBeNull();
    });
});
