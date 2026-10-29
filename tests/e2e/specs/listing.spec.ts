// The listing on a phone: what is on screen stays where it is while it settles.
import { closeMenu, expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectTapTargets } from '../fixtures/layout';
import { watchBoxes } from '../fixtures/steady';

test.describe('listing', () => {
    // Tiles take their preview's shape once it loads (the placeholder's is a guess): rows above the screen change
    // height. The tile under the finger must not move meanwhile, or a tap lands on another item (seen on WebKit in
    // reported.spec.ts: the ⋮ tapped, the click on something else, no menu).
    test('a tile on screen stays put while the previews around it load', async ({ page }) => {
        await openFolder(page, 'Many photos');
        const tile = item(page, 'Many photos/Photo 040.jpg');
        await tile.scrollIntoViewIfNeeded();
        await expect(tile).toBeVisible();
        const watch = await watchBoxes(page, { 'Photo 040': `sfe-listing [data-path="Many photos/Photo 040.jpg"]` });
        await watch.expectSteady(3000, 2);
    });

    // What comes in above the listing after it is shown (the scan panel's last result, read after the listing) pushes
    // every item down: a tap aimed at one lands on another.
    test('the listing does not move once shown', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const watch = await watchBoxes(page, { 'first tile': 'sfe-listing [data-path]' });
        await watch.expectSteady(2500, 2);
    });

    // A scan started elsewhere (another user, a copy or an extraction indexing its folder) is found by the page's poll
    // (every 30 s): its panel comes in under the top bar. It pushed the listing down by its height, and a long press
    // under way landed on the folder instead of the photo (selection.spec.ts, under the full run).
    test('a scan started elsewhere: its panel comes in, the tile under the finger stays put', async ({ page, scratch }) => {
        const headers = { 'X-SFE-Request': '1' };
        const panel = page.locator('sfe-scan-panel');
        await page.clock.install();
        await openFolder(page, 'Many photos');
        // Another test's scan may be running or its result shown: over and dismissed first, so ours is the one coming in.
        await expect.poll(async () => (await (await page.request.get('api/scan', { headers })).json()).state, { timeout: 60_000 }).not.toBe('running');
        await page.reload();
        await expect(item(page, 'Many photos/Photo 001.jpg')).toBeVisible();
        if (await panel.isVisible())
            await panel.getByRole('button', { name: 'Dismiss' }).tap();
        await expect(panel).toBeHidden();

        const tile = item(page, 'Many photos/Photo 006.jpg');
        const before = (await tile.boundingBox())!.y;
        await expect.poll(async () => (await (await page.request.post('api/scan', { data: { path: scratch }, headers })).json()).path,
            { timeout: 60_000 }).toBe(scratch);
        await page.clock.runFor(31_000);
        await expect(panel).toBeVisible();
        await expect.poll(async () => (await tile.boundingBox())!.y).toBeCloseTo(before, 0);
    });

    // Phones show the active layout's button only: a tap moves on to the next (grid, list, one per row).
    test('each layout by a tap: fits, its items open and have their menu', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const layout = page.getByRole('group', { name: 'Layout' });
        for (const view of ['list', 'single', 'grid']) {
            await layout.locator('button[aria-pressed="true"]').tap();
            await expect(page.locator(`sfe-listing .files.${view}`)).toBeVisible();
            await expectFitsScreen(page);
            await expectTapTargets(page, 'sfe-listing');
            const photo = item(page, 'Photos/2025 Mountains/Summit.jpg');
            await photo.locator('.item-menu-button').tap();
            await expect(page.locator('.item-menu[role="menu"]')).toBeVisible();
            await closeMenu(page);
            await photo.locator('.item-link').first().tap();
            await expect(page.locator('dialog.viewer[open] .viewer-title strong')).toHaveText('Summit.jpg');
            await page.locator('dialog.viewer[open]').getByRole('button', { name: 'Close' }).tap();
            await expect(page.locator('dialog.viewer[open]')).toHaveCount(0);
        }
    });

    test('the main menu opens from the top bar, fits and closes', async ({ page }) => {
        await openFolder(page, 'Photos');
        await page.getByRole('button', { name: 'More', exact: true }).tap();
        const menu = page.locator('sfe-main-menu [role=menu], sfe-main-menu .popover').first();
        await expect(menu).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, 'sfe-main-menu');
        await page.keyboard.press('Escape');
        await expect(menu).toBeHidden();
    });

    test('the main menu closes on a tap outside, and its links work', async ({ page }) => {
        await openFolder(page, 'Photos');
        const more = page.getByRole('button', { name: 'More', exact: true });
        const menu = page.locator('sfe-main-menu #menu');
        await more.tap();
        await expect(menu).toBeVisible();
        // A tap on the folder's summary line ("3 folders · 2 files"), away from the menu: closes it, opens nothing.
        const summary = (await page.locator('.location-bar .summary').boundingBox())!;
        await page.touchscreen.tap(summary.x + 10, summary.y + summary.height / 2);
        await expect(menu).toBeHidden();
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos');

        await more.tap();
        await menu.getByRole('menuitem', { name: 'Administration' }).tap();
        await expect(page).toHaveURL(/\/admin/);
        await page.goBack();
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos');
    });
});
