// Selecting on a phone (mobile e2e plan, Phase 2, listing and selection): agents/docs/features/selection-and-keys.md.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectTapTargets } from '../fixtures/layout';
import { longPress, needsTouchGestures, swipe } from '../fixtures/touch';

const bar = '.selection-dock .selection-bar';

test.describe('selection', () => {
    test('a long press selects, opens nothing and gives no browser menu (Android)', async ({ page, browserName }) => {
        needsTouchGestures(browserName);
        await openFolder(page, 'Photos/2025 Mountains');
        const tile = item(page, 'Photos/2025 Mountains/Summit.jpg');
        await longPress(page, tile);
        await expect(tile).toHaveClass(/selected/);
        await expect(page.locator('.item-menu[role="menu"]')).toBeHidden();
        await expect(page.locator('dialog.viewer[open]')).toHaveCount(0);
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos/2025 Mountains');
        expect(await page.evaluate(() => getSelection()?.toString() ?? '')).toBe('');
    });

    test('while selecting, a tap toggles files and folders instead of opening them', async ({ page }) => {
        await openFolder(page, 'Photos');
        const photo = item(page, 'Photos/Sunrise.jpg');
        const folder = item(page, 'Photos/Family');
        await photo.locator('.select-box').tap();
        await expect(photo).toHaveClass(/selected/);

        await folder.locator('a').first().tap();
        await expect(folder).toHaveClass(/selected/);
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos');
        await photo.locator('.item-link').first().tap();
        await expect(photo).not.toHaveClass(/selected/);
        await expect(page.locator('dialog.viewer[open]')).toHaveCount(0);
        await expect(page.locator('.selection-count-short, .selection-count').first()).toContainText('1');
    });

    test('select all and clear from the bar; its buttons fit and are big enough', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.select-box').tap();
        const dock = page.locator(bar);
        await expect(dock).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, '.selection-dock');

        await dock.getByRole('button', { name: 'Select all' }).tap();
        await expect(page.locator('sfe-listing .selected')).toHaveCount(6);
        await dock.getByRole('button', { name: 'Clear the selection' }).tap();
        await expect(page.locator('sfe-listing .selected')).toHaveCount(0);
        await expect(page.locator('.selection-dock')).toBeHidden();
    });

    test('the selection bar leaves the last row reachable', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.select-box').tap();
        await expect(page.locator(bar)).toBeVisible();
        await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
        await page.waitForTimeout(300);
        const last = page.locator('sfe-listing [data-path]').last();
        const lastBox = (await last.boundingBox())!;
        const barBox = (await page.locator(bar).boundingBox())!;
        expect(lastBox.y + lastBox.height, 'last item ends above the bar').toBeLessThanOrEqual(barBox.y + 1);
    });

    // Android Chrome hides its address bar when scrolling down and shows it again when scrolling up: the screen's
    // height changes and the bar must stay at the bottom.
    test('the selection bar stays at the bottom when the address bar comes and goes', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.select-box').tap();
        const dock = page.locator(bar);
        await expect(dock).toBeVisible();
        const size = page.viewportSize()!;
        for (const height of [size.height + 56, size.height]) {
            await page.setViewportSize({ width: size.width, height });
            await page.waitForTimeout(200);
            const box = (await dock.boundingBox())!;
            expect(height - (box.y + box.height), `gap under the bar at height ${height}`).toBeGreaterThanOrEqual(8);
            expect(height - (box.y + box.height)).toBeLessThanOrEqual(40);
        }
    });

    test('a swipe scrolls the listing and selects nothing', async ({ page, browserName }) => {
        needsTouchGestures(browserName);
        await openFolder(page, 'Many photos');
        const width = page.viewportSize()!.width;
        await swipe(page, { x: width / 2, y: 600 }, { x: width / 2, y: 150 }, 250);
        await page.waitForTimeout(400);
        expect(await page.evaluate(() => scrollY)).toBeGreaterThan(200);
        await expect(page.locator('sfe-listing .selected')).toHaveCount(0);
        await expect(page.locator('dialog.viewer[open]')).toHaveCount(0);
    });
});
