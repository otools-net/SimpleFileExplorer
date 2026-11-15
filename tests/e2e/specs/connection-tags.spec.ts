// Tagging a connection's file on a phone: in a folder of a connection (?c=), the selection bar's Tags opens the same
// dialog as in the library; its tags are read from, and the picked tag (the library's) sent to, the connection's own
// routes, and the tile shows it. The suite's server has no connection: the configuration, the connection's listing
// and its tag routes are answered by the test (page.route); the tag itself is made by the server.
import { Page } from '@playwright/test';
import { expect, item, test } from '../fixtures/test';
import { expectFitsScreen } from '../fixtures/layout';

const DROPBOX = { kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} };
const FILE = {
    name: 'Pack.zip',
    path: 'Trips/Pack.zip',
    kind: 'file',
    extension: 'zip',
    size: 1234,
    modified: null,
    indexed: true,
};

/** The names of the tags the tests made, by id: the fake tag route answers with them. */
const tagNames = new Map<number, string>();

/** A connection the session reads, its folder Trips holding one file; the tag changes it receives. */
async function fakeConnection(page: Page): Promise<{ changes: unknown[] }> {
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
    await page.route('**/api/v1/connections/3/browse?*', (route) =>
        route.fulfill({
            json: {
                connectionId: 3,
                name: 'Holiday Dropbox',
                kind: 'dropbox',
                listing: {
                    path: 'Trips',
                    name: 'Trips',
                    parentPath: '',
                    breadcrumbs: [],
                    items: [FILE],
                    offset: 0,
                    limit: 100,
                    total: 1,
                    directoryCount: 0,
                    fileCount: 1,
                },
                access: 'read',
                sorted: true,
                hasMore: false,
                capabilities: {},
            },
        }),
    );
    const changes: unknown[] = [];
    // The file's tags (none yet), read when the dialog opens.
    await page.route(/\/api\/v1\/connections\/3\/file-tags\?path=/, (route) =>
        route.fulfill({ json: { path: FILE.path, tags: [], groups: [] } }),
    );
    await page.route('**/api/v1/connections/3/file-tags', async (route) => {
        const change = route.request().postDataJSON();
        changes.push(change);
        const name = tagNames.get(change.add[0]) ?? 'tag';
        const tag = { id: change.add[0], name, displayName: name, color: null, isCategory: false, isHidden: false };
        const files = [{ path: FILE.path, tags: [tag], groups: [{ category: null, tags: [tag] }] }];
        await route.fulfill({ json: { added: 1, removed: 0, files } });
    });
    return { changes };
}

test.describe('tags on a connection', () => {
    test("a connection's file tagged from the selection bar, through the connection", async ({ page }, testInfo) => {
        const name = `Remote ${testInfo.project.name} ${testInfo.repeatEachIndex} ${testInfo.retry}`;
        const created = await page.request.post('api/v1/tags', { data: { name }, headers: { 'X-SFE-Request': '1' } });
        expect(created.ok(), await created.text()).toBe(true);
        const id = (await created.json()).id as number;
        tagNames.set(id, name);
        const { changes } = await fakeConnection(page);

        await page.goto('./?c=3&path=Trips');
        const tile = item(page, FILE.path);
        await expect(tile).toBeVisible();
        await tile.locator('.select-box').tap();
        await page
            .locator('.selection-bar')
            .getByRole('button', { name: /^(Tags|Add or remove tags)/ })
            .tap();
        const dialog = page.locator('dialog.sheet[open]');
        await expect(dialog).toBeVisible();
        await expectFitsScreen(page);

        await dialog.locator('input.tag-input').tap();
        await dialog.locator('input.tag-input').fill(name.slice(0, 12));
        const option = dialog.locator('.tag-options li[role=option]', { hasText: name }).first();
        await expect(option).toBeVisible();
        await option.tap();
        await expect(dialog.locator('.tag-group sfe-tag-chip', { hasText: name })).toBeVisible();
        await dialog.getByRole('button', { name: 'Done' }).tap();

        await expect(tile.locator('sfe-item-tags')).toContainText(name);
        expect(changes).toEqual([{ paths: [FILE.path], add: [id] }]);
    });
});
