// The administration and the sign-in page on a phone (mobile e2e plan, Phase 2, accounts and administration): each
// section opens, fits the screen, its fields don't make iOS zoom and its buttons are big enough for a finger.
import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';
import { accounts, library } from '../paths';

const sections = ['account', 'interface', 'playback', 'scanning', 'files', 'keepAlive', 'tags', 'tracking', 'thumbnails',
    'trickplay', 'editor', 'access', 'tasks', 'logs', 'system'];
const fields = ':is(input:not([type=checkbox]):not([type=radio]):not([type=range]):not([type=color]):not([type=file]), textarea, select)';

for (const section of sections) {
    test.describe('administration', () => {
        // Duplicates & moves lists what the index knows: files the specs' cleanup removed behind the app's back (the
        // scratch folders) are still there until a scan, and their thumbnails 404 (the row shows its icon instead).
        test.use({ allowedErrors: section === 'tracking' ? /status of 404/ : null });
        test(`section ${section}`, async ({ page }) => {
            await page.goto(`./admin?section=${section}`);
            await expect(page.locator('.admin-head')).toBeVisible();
            await page.waitForTimeout(500);
            await expectFitsScreen(page);
            await expectNothingCut(page);
            await expectNoZoomOnFocus(page, `main ${fields}`);
            await expectTapTargets(page, 'main');
            // Found 2026-10-29: beside the sections (800 px sideways) a log line's fixed parts left its message nothing
            // and pushed the page wider.
            if (section === 'logs') {
                const lines = await page.locator('.log-entry-head').evaluateAll(heads => heads.map(head => ({
                    message: head.querySelector('.log-message')?.getBoundingClientRect().width ?? 0,
                    right: Math.max(...[...head.children].map(child => child.getBoundingClientRect().right)),
                })));
                expect(lines.length).toBeGreaterThan(0);
                const width = page.viewportSize()!.width;
                expect(lines.filter(line => line.message < 40 || line.right > width + 1), 'log lines squeezed or past the screen').toEqual([]);
            }
        });
    });
}

test.describe('sign-in', () => {
    // Signed out: the app's first request for the session is refused (401) before the sign-in page shows.
    test.use({ storageState: { cookies: [], origins: [] }, allowedErrors: /status of 401/ });

    test('the sign-in page fits, its fields do not make iOS zoom, a finger signs in', async ({ page }) => {
        await page.goto('./');
        const name = page.getByLabel(/user name|name/i).first();
        await expect(name).toBeVisible();
        await expectFitsScreen(page);
        await expectNothingCut(page);
        await expectNoZoomOnFocus(page, fields);
        await expectTapTargets(page);
        await name.tap();
        await name.fill(accounts.sam.userName);
        await page.getByLabel(/password/i).first().fill(accounts.sam.password);
        await page.getByRole('button', { name: /sign in/i }).first().tap();
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
    });
});

// Found by the suite (2026-10-29): names without spaces (camera files, hashes) in the trash widened the page.
test('long names in the trash keep the page within the screen', async ({ page, scratch }) => {
    const folder = `${scratch}/A_folder_named_without_any_space_${'x'.repeat(40)}`;
    const name = `IMG_20261029_123456789_${'0123456789'.repeat(6)}.jpg`;
    fs.mkdirSync(path.join(library, folder), { recursive: true });
    fs.copyFileSync(path.join(library, scratch, 'A.jpg'), path.join(library, folder, name));
    const deleted = await page.request.post('api/files/delete', { data: { paths: [`${folder}/${name}`] }, headers: { 'X-SFE-Request': '1' } });
    expect(deleted.ok(), await deleted.text()).toBe(true);

    await page.goto('./admin?section=files');
    await expect(page.locator('.admin-head')).toBeVisible();
    const row = page.locator('.tracking-missing', { hasText: 'IMG_20261029' }).first();
    await expect(row).toBeVisible();
    await expectFitsScreen(page);
    await expectNothingCut(page);
    // The name keeps room (it was squeezed to nothing beside the buttons in a narrow column of a wide screen).
    expect((await row.locator('.tracking-name').boundingBox())!.width).toBeGreaterThan(80);
});

test('the server log scrolls by finger and loads older entries at its end', async ({ page, scratch }) => {
    // More entries than a page (100): a line per folder made (FileOperations logs each), in this test's own folder.
    const headers = { 'X-SFE-Request': '1' };
    for (let start = 0; start < 110; start += 10)
        await Promise.all(Array.from({ length: 10 }, (_, i) =>
            page.request.post('api/files/folders', { data: { path: scratch, name: `Log ${start + i}` }, headers })));
    // Written a moment later (AuditLogWorker).
    await expect.poll(async () => (await (await page.request.get('api/logs', { headers })).json()).next, { timeout: 20_000 }).not.toBeNull();

    await page.goto('./admin?section=logs');
    const entries = page.locator('.log-entries > li');
    await expect(entries.first()).toBeVisible();
    const before = await entries.count();
    // Down to its end with the page's own scrolling, as a finger flicks.
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const older = page.getByRole('button', { name: 'Load older entries' });
    await older.tap();
    await expect.poll(() => entries.count()).toBeGreaterThan(before);
    await expectFitsScreen(page);
    await expectNothingCut(page);
});
