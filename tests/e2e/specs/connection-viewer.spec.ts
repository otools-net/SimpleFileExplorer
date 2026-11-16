// The viewer on a connection's files, on a phone (agents/docs/features/connection-previews.md): a Markdown file of a
// connection (?c=) opens by a tap in the Markdown view, read through the connection's own text route, never the
// library's; a thumbnail is asked of the connection's route. The suite's server has no connection: the
// configuration, the connection's listing, its text and its thumbnail are answered by the test (page.route).
import { Page } from '@playwright/test';
import { expect, item, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut } from '../fixtures/layout';

const DROPBOX = { kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} };
const NOTES = {
    name: 'Trip notes.md',
    path: 'Trips/Trip notes.md',
    kind: 'file',
    extension: 'md',
    size: 64,
    modified: null,
    indexed: true,
};

/** A connection the session reads, its folder Trips holding one Markdown file; the text routes asked. */
async function fakeConnection(page: Page): Promise<{ asked: string[] }> {
    await page.route('**/api/v1/config', async (route) => {
        const response = await route.fetch();
        const json = await response.json();
        json.connections = { levels: ['account'], kinds: [DROPBOX] };
        json.rights = [...json.rights, 'connections.use'];
        await route.fulfill({ response, json });
    });
    const usable = {
        id: 3,
        kind: 'dropbox',
        name: 'Holiday Dropbox',
        level: 'account',
        state: 'ready',
        access: 'read',
    };
    await page.route('**/api/v1/connections', (route) =>
        route.fulfill({ json: { usable: [usable], managed: [], groups: [], libraries: [] } }),
    );
    const listing = {
        path: 'Trips',
        name: 'Trips',
        parentPath: '',
        breadcrumbs: [],
        items: [NOTES],
        offset: 0,
        limit: 100,
        total: 1,
        directoryCount: 0,
        fileCount: 1,
    };
    await page.route('**/api/v1/connections/3/browse?*', (route) =>
        route.fulfill({
            json: {
                connectionId: 3,
                name: 'Holiday Dropbox',
                kind: 'dropbox',
                listing,
                access: 'read',
                sorted: true,
                hasMore: false,
                capabilities: {},
            },
        }),
    );
    const asked: string[] = [];
    const text = {
        text: '# Day one\n\nWe walked up to the hut and **slept** there.\n',
        encoding: 'utf-8',
        lineEnding: 'lf',
        version: 'hash1',
        size: 64,
        modified: '2026-11-01T10:00:00Z',
        access: { overwrite: false, saveCopy: false, download: true, permanent: false },
    };
    await page.route(/\/api\/v1\/(connections\/3\/)?files\/text\?/, (route) => {
        asked.push(new URL(route.request().url()).pathname);
        return route.fulfill({ json: text });
    });
    return { asked };
}

test.describe('the viewer on a connection', () => {
    test("a connection's Markdown file opens read through the connection", async ({ page }) => {
        const { asked } = await fakeConnection(page);

        await page.goto('./?c=3&path=Trips');
        await item(page, NOTES.path).locator('.item-link').first().tap();
        const viewer = page.locator('dialog.viewer[open]');
        const shown = viewer.locator('sfe-markdown-view');
        await expect(shown).toBeVisible();
        await expect(shown.getByRole('heading', { name: 'Day one' })).toBeVisible();
        await page.waitForTimeout(300);

        expect(asked.length).toBeGreaterThan(0);
        expect(asked.every((path) => path.endsWith('/api/v1/connections/3/files/text'))).toBe(true);
        await expectFitsScreen(page);
        await expectNothingCut(page);
    });
});
