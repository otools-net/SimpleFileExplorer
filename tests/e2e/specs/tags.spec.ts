// Tagging on a phone: the selection bar's Tags, the picker's suggestion taken by a
// tap (it wasn't on iPhones: a prevented pointerdown cancelled the tap), the tag shown on the tile.
import type { Page } from '@playwright/test';
import { expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';
import { holdLikeIos } from '../fixtures/touch';

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

/** A tag of this test's own, made through the API; its id. */
async function newTag(page: Page, label: string): Promise<{ id: number; name: string }> {
    const info = test.info();
    const name = `${label} ${info.project.name} ${info.repeatEachIndex} ${info.retry}`;
    const created = await page.request.post('api/tags', { data: { name }, headers });
    expect(created.ok(), await created.text()).toBe(true);
    return { id: (await created.json()).id, name };
}

const headers = { 'X-SFE-Request': '1' };

test.describe('tag panels and menus', () => {
    test("a folder's default tags: their panel by a tap, a sheet on phones, closed by the dimmed area", async ({ page, scratch }) => {
        const tag = await newTag(page, 'Default');
        const set = await page.request.put('api/folder-default-tags', { data: { path: scratch, add: [tag.id] }, headers });
        expect(set.ok(), await set.text()).toBe(true);
        await openFolder(page, scratch);
        await page.locator('.location-bar .default-tags-button').tap();
        const panel = page.locator('.default-tags-popover');
        await expect(panel).toBeVisible();
        await expect(panel).toContainText(tag.name);
        await expectFitsScreen(page);
        await expectTapTargets(page, '.default-tags-popover');
        if (isSheetWidth(page)) {
            const box = (await panel.boundingBox())!;
            expect(Math.round(box.width)).toBe(page.viewportSize()!.width);
            expect(Math.round(box.y + box.height)).toBe(page.viewportSize()!.height);
            const backdrop = (await page.locator('.default-tags-layer .sheet-backdrop').boundingBox())!;
            await page.touchscreen.tap(backdrop.x + backdrop.width / 2, backdrop.y + 20);
        } else {
            await panel.locator('.default-tags-close').tap();
        }
        await expect(panel).toHaveCount(0);
    });

    test("a tag on a file: holding it opens its menu, Add to the search works", async ({ page, scratch }) => {
        const tag = await newTag(page, 'Chip');
        const tagged = await page.request.post('api/file-tags', { data: { paths: [`${scratch}/A.jpg`], add: [tag.id] }, headers });
        expect(tagged.ok(), await tagged.text()).toBe(true);
        await openFolder(page, scratch);
        const chip = item(page, `${scratch}/A.jpg`).locator('sfe-tag-chip', { hasText: tag.name });
        await expect(chip).toBeVisible();
        await holdLikeIos(page, chip);
        const menu = page.locator('.item-menu[role="menu"]');
        await expect(menu).toBeVisible();
        await expectTapTargets(page, '.item-menu[role="menu"]');
        await menu.locator('[data-entry="search"]').tap();
        await expect(menu).toHaveCount(0);
        await expect.poll(() => new URL(page.url()).searchParams.get('q')).toContain(tag.name.split(' ')[0]);
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
    });
});

test.describe('a whole folder', () => {
    test("tag every file of a folder from the folder's menu", async ({ page, scratch }) => {
        const tag = await newTag(page, 'Folder');
        await openFolder(page, scratch);
        const more = page.locator('button.location-more');
        test.skip(!await more.isVisible(), 'the folder menu is a "⋯" up to 640 px');
        await more.tap();
        await page.locator('.item-menu[role="menu"] [data-entry="tags"]').tap();
        const dialog = page.locator('dialog.sheet[open]');
        await expect(dialog.locator('.scope-count')).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, 'dialog.sheet[open]');

        // The files' picker (the folder's default tags have one too, below).
        const input = dialog.getByRole('combobox', { name: 'Add a tag to all these files…' });
        await input.tap();
        await input.fill(tag.name.slice(0, 12));
        await dialog.locator('.tag-options li[role=option]', { hasText: tag.name }).first().tap();
        await dialog.locator('.scope-confirm').getByRole('button', { name: 'Add tag' }).tap();
        await expect(dialog.locator('.tag-group', { hasText: 'On all these files' }).locator('sfe-tag-chip', { hasText: tag.name })).toBeVisible({ timeout: 15_000 });
        await dialog.getByRole('button', { name: 'Done' }).tap();
        await expect(dialog).toHaveCount(0);
        for (const name of ['A.jpg', 'B.jpg', 'C.jpg'])
            await expect(item(page, `${scratch}/${name}`).locator('sfe-item-tags')).toContainText(tag.name);
    });
});
