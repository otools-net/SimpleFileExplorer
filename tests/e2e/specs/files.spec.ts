// File operations on a phone (mobile e2e plan, Phase 2, file operations), each in a scratch folder of its own:
// rename, new folder, delete, move, through the menus and dialogs a finger uses.
import type { Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';
import { library } from '../paths';

const menu = '.item-menu[role="menu"]';
const dialog = (page: Page) => page.locator('dialog.sheet[open]');

/** Opens the menu of `path` with its ⋮ and taps the entry `entry` (its `data-entry`). */
async function fromItemMenu(page: Page, path: string, entry: string): Promise<void> {
    await item(page, path).locator('.item-menu-button').tap();
    await page.locator(`${menu} [data-entry="${entry}"]`).tap();
    await expect(dialog(page)).toBeVisible();
}

/** The dialog fits the screen, its fields don't make iOS zoom, its buttons are big enough. */
async function expectPhoneDialog(page: Page): Promise<void> {
    await expectFitsScreen(page);
    await expectNoZoomOnFocus(page, 'dialog.sheet[open] :is(input:not([type=checkbox]):not([type=radio]), textarea, select)');
    await expectTapTargets(page, 'dialog.sheet[open]');
}

test.describe('file operations', () => {
    test('rename a file from its menu', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/A.jpg`, 'rename');
        await expectPhoneDialog(page);
        const name = dialog(page).getByRole('textbox');
        await expect(name).toBeFocused();
        await name.fill('Renamed.jpg');
        await name.press('Enter');
        await expect(dialog(page)).toHaveCount(0);
        await expect(item(page, `${scratch}/Renamed.jpg`)).toBeVisible();
        await expect(item(page, `${scratch}/A.jpg`)).toHaveCount(0);
    });

    test('a new folder from the folder menu', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        const more = page.locator('button.location-more');
        test.skip(!await more.isVisible(), 'the folder menu is a "⋯" up to 640 px');
        await more.tap();
        await page.locator(`${menu} [data-entry="newFolder"]`).tap();
        await expect(dialog(page)).toBeVisible();
        await expectPhoneDialog(page);
        const name = dialog(page).getByRole('textbox');
        await name.fill('Made on a phone');
        await dialog(page).locator('button.primary').tap();
        await expect(item(page, `${scratch}/Made on a phone`)).toBeVisible();
    });

    test('delete a file to the trash', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/B.jpg`, 'delete');
        await expectPhoneDialog(page);
        await dialog(page).getByRole('button', { name: 'Delete', exact: true }).tap();
        await expect(dialog(page)).toHaveCount(0);
        await expect(item(page, `${scratch}/B.jpg`)).toHaveCount(0);
        await expect(item(page, `${scratch}/A.jpg`)).toBeVisible();
    });

    test('move a file into a folder with the folder picker', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/C.jpg`, 'moveTo');
        await expectPhoneDialog(page);
        await dialog(page).locator('.transfer-folder', { hasText: 'Inside' }).tap();
        await expect(dialog(page).locator('.transfer-crumbs strong')).toHaveText('Inside');
        await dialog(page).locator('button.primary').tap();
        await expect(dialog(page)).toHaveCount(0);
        await expect(item(page, `${scratch}/C.jpg`)).toHaveCount(0);
        await item(page, `${scratch}/Inside`).locator('a').first().tap();
        await expect(item(page, `${scratch}/Inside/C.jpg`)).toBeVisible();
    });

    test('upload a photo from the phone into the folder', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        const more = page.locator('button.location-more');
        test.skip(!await more.isVisible(), 'the folder menu is a "⋯" up to 640 px');
        await more.tap();
        const chooser = page.waitForEvent('filechooser');
        await page.locator(`${menu} [data-entry="upload"]`).tap();
        await (await chooser).setFiles({ name: 'From the phone.jpg', mimeType: 'image/jpeg', buffer: fs.readFileSync(path.join(library, scratch, 'A.jpg')) });
        await expect(item(page, `${scratch}/From the phone.jpg`)).toBeVisible({ timeout: 20_000 });
    });

    test('a deleted file comes back from the trash', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/B.jpg`, 'delete');
        await dialog(page).getByRole('button', { name: 'Delete', exact: true }).tap();
        await expect(item(page, `${scratch}/B.jpg`)).toHaveCount(0);

        await page.goto('./admin?section=files');
        const row = page.locator('.tracking-missing', { hasText: 'B.jpg' }).filter({ hasText: scratch.split('/').pop()! }).first();
        await expect(row).toBeVisible();
        await row.getByRole('button', { name: 'Restore' }).tap();
        await expect(row).toHaveCount(0);
        await openFolder(page, scratch);
        await expect(item(page, `${scratch}/B.jpg`)).toBeVisible();
    });
});
