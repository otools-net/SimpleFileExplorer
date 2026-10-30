// The search box on a phone: a tap focuses it without zooming, a query
// lists results, recent searches are offered and tappable.
import { expect, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';

test.describe('search', () => {
    test('a tap focuses the search box, which does not make iOS zoom', async ({ page }) => {
        await openFolder(page, 'Photos');
        const box = page.getByRole('combobox', { name: 'Search', exact: true });
        await box.tap();
        await expect(box).toBeFocused();
        await expectNoZoomOnFocus(page, 'input[type=search]');
        await expectFitsScreen(page);
    });

    test('a query lists its results; the top bar keeps fitting', async ({ page }) => {
        await openFolder(page, 'Photos');
        const box = page.getByRole('combobox', { name: 'Search', exact: true });
        await box.tap();
        await box.fill('summit');
        await box.press('Enter');
        await expect(page.locator('sfe-listing [data-path="Photos/2025 Mountains/Summit.jpg"]')).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, 'header');
    });

    test('a recent search is offered on the next tap and runs on a tap', async ({ page }) => {
        await openFolder(page, 'Photos');
        const box = page.getByRole('combobox', { name: 'Search', exact: true });
        await box.tap();
        await box.fill('lake');
        await box.press('Enter');
        await expect(page.locator('sfe-listing [data-path="Photos/2025 Mountains/Autumn lake.jpg"]')).toBeVisible();
        await openFolder(page, 'Music');
        await box.tap();
        const recent = page.locator('li.search-history-option', { hasText: 'lake' });
        await expect(recent).toBeVisible();
        await recent.tap();
        // Run again where the box is: inside Music (searches are scoped to the folder listed).
        await expect(page).toHaveURL(/[?&]q=lake(&|$)/);
        await expect(page.getByRole('navigation', { name: 'Location' })).toContainText('Search “lake”');
    });
});
