// Tagging on a phone (mobile e2e plan, Phase 2, tags): the selection bar's Tags, the picker's suggestion taken by a
// tap (it wasn't on iPhones: a prevented pointerdown cancelled the tap), the tag shown on the tile.
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNoZoomOnFocus } from '../fixtures/layout';

test.describe('tags', () => {
    test('tag a selected file from the selection bar', async ({ page, scratch }, testInfo) => {
        const name = `Phone ${testInfo.project.name} ${testInfo.repeatEachIndex} ${testInfo.retry}`;
        const created = await page.request.post('api/tags', { data: { name }, headers: { 'X-SFE-Request': '1' } });
        expect(created.ok(), await created.text()).toBe(true);

        await openFolder(page, scratch);
        const tile = item(page, `${scratch}/A.jpg`);
        await tile.locator('.select-box').tap();
        // Icon only on phones: named by its title.
        await page.locator('.selection-bar').getByRole('button', { name: /^(Tags|Add or remove tags)/ }).tap();
        const dialog = page.locator('dialog.sheet[open]');
        await expect(dialog).toBeVisible();
        await expectFitsScreen(page);

        const input = dialog.locator('input.tag-input');
        await input.tap();
        await expectNoZoomOnFocus(page, 'dialog.sheet[open] input.tag-input');
        await input.fill(name.slice(0, 12));
        const option = dialog.locator('.tag-options li[role=option]', { hasText: name }).first();
        await expect(option).toBeVisible();
        await option.tap();
        await expect(dialog.locator('.tag-group sfe-tag-chip', { hasText: name })).toBeVisible();

        await dialog.getByRole('button', { name: 'Done' }).tap();
        await expect(dialog).toHaveCount(0);
        await expect(tile.locator('sfe-item-tags')).toContainText(name);
    });
});
