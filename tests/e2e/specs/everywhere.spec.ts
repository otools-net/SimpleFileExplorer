// A search everywhere on a phone: the search's menu (a sheet) has "Search in" › "Everywhere", which lists one
// group per place, each loading alone: a connection asking to wait shows its own message and Retry while the others
// show their results. The suite's server has no connection: the configuration, the connections and their searches
// are answered by the test (page.route); the library's search is the server's.
import { Page } from '@playwright/test';
import { expect, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

const DROPBOX = { kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} };

/** Two connections the session uses: one finds a file (its index not whole yet), one asks to wait. */
async function fakeConnections(page: Page): Promise<void> {
    await page.route('**/api/v1/config', async (route) => {
        const response = await route.fetch();
        const json = await response.json();
        json.connections = { levels: ['account'], kinds: [DROPBOX] };
        json.rights = [...json.rights, 'connections.use'];
        await route.fulfill({ response, json });
    });
    const usable = (id: number, name: string) => ({
        id,
        kind: 'dropbox',
        name,
        level: 'account',
        state: 'ready',
        access: 'read',
    });
    await page.route('**/api/v1/connections', (route) =>
        route.fulfill({
            json: {
                usable: [usable(3, 'Holiday Dropbox'), usable(4, 'Busy Dropbox')],
                managed: [],
                groups: [],
                libraries: [],
            },
        }),
    );
    const found = {
        name: 'Summit copy.jpg',
        path: 'Trips/Summit copy.jpg',
        kind: 'image',
        extension: 'jpg',
        size: 1234,
        modified: null,
        directory: 'Trips',
    };
    await page.route('**/api/v1/connections/3/search?*', (route) =>
        route.fulfill({
            json: {
                connectionId: 3,
                name: 'Holiday Dropbox',
                kind: 'dropbox',
                results: { query: 'summit', path: '', items: [found], limit: 20, hasMore: false, next: null, total: 1 },
                access: 'read',
                capabilities: {},
                coverage: 'partial',
            },
        }),
    );
    await page.route('**/api/v1/connections/4/search?*', (route) =>
        route.fulfill({
            status: 429,
            contentType: 'application/problem+json',
            headers: { 'Retry-After': '5' },
            body: JSON.stringify({ type: 'connection-rate-limited', title: 'Dropbox asks to wait.', status: 429 }),
        }),
    );
}

test.describe('search everywhere', () => {
    // The browser logs the busy connection's 429, which the test makes on purpose.
    test.use({ allowedErrors: /\/api\/v1\/connections\/4\/search/ });

    test('the scope menu switches to Everywhere: a group per place, each alone', async ({ page }) => {
        await fakeConnections(page);
        await openFolder(page, 'Photos');
        const box = page.getByRole('combobox', { name: 'Search', exact: true });
        await box.tap();
        await box.fill('summit');
        await box.press('Enter');
        await expect(page.locator('sfe-listing [data-path="Photos/2025 Mountains/Summit.jpg"]')).toBeVisible();

        // The chip of the search box opens it; on a phone, where the chip gives way, the search's own menu (the
        // location's ⋯) has it as "Search in".
        const menu = page.locator('.item-menu[role="menu"]');
        const chip = page.locator('.search-scope');
        if (await chip.isVisible()) await chip.tap();
        else {
            await page.getByRole('button', { name: 'Actions on this search' }).tap();
            await menu.locator('[data-entry="searchIn"]').tap();
        }
        await menu.locator('[data-entry="everywhere"]').tap();
        await expect(menu).toHaveCount(0);

        await expect(page).toHaveURL(/[?&]all=1(&|$)/);
        const groups = page.locator('.everywhere-group');
        await expect(groups).toHaveCount(3);
        await expect(groups.nth(0).locator('.everywhere-item-name')).toContainText(['Summit.jpg']);
        await expect(groups.nth(1)).toContainText('Holiday Dropbox');
        await expect(groups.nth(1).locator('.everywhere-item-name')).toHaveText(['Summit copy.jpg']);
        await expect(groups.nth(1).locator('.everywhere-note')).toBeVisible();
        await expect(groups.nth(2)).toContainText('Busy for now');
        await expect(groups.nth(2).getByRole('button', { name: 'Retry' })).toBeVisible();
        await expectFitsScreen(page);
        await expectNothingCut(page);
        await expectTapTargets(page, 'sfe-everywhere');
    });
});
