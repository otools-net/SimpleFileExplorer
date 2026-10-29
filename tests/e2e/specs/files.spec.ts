// File operations on a phone (mobile e2e plan, Phase 2, file operations), each in a scratch folder of its own:
// rename, new folder, delete, move, through the menus and dialogs a finger uses.
import type { Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectNoZoomOnFocus, expectTapTargets } from '../fixtures/layout';
import { library } from '../paths';
import { expectFocusedFieldShown, openKeyboard } from '../fixtures/viewport';

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
    await expectNothingCut(page);
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

test.describe('with the keyboard open', () => {
    test('rename: the field and the dialog\'s buttons stay above the keyboard', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/A.jpg`, 'rename');
        await expect(dialog(page).getByRole('textbox')).toBeFocused();
        const close = await openKeyboard(page);
        await expectFocusedFieldShown(page);
        const save = dialog(page).locator('button.primary');
        await expect(save).toBeInViewport();
        await expectFitsScreen(page);
        await dialog(page).getByRole('textbox').fill('Typed.jpg');
        await save.tap();
        await expect(item(page, `${scratch}/Typed.jpg`)).toBeVisible();
        await close();
    });
});

test.describe('more file operations', () => {
    test('rename two files together, numbered', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await item(page, `${scratch}/A.jpg`).locator('.select-box').tap();
        await item(page, `${scratch}/B.jpg`).locator('.select-box').tap();
        await fromItemMenu(page, `${scratch}/A.jpg`, 'rename');
        await expectPhoneDialog(page);
        await expectNothingCut(page, 'dialog.sheet[open]');
        await dialog(page).getByRole('radio', { name: 'Numbered' }).tap();
        const pattern = dialog(page).locator('input[name="pattern"]');
        await pattern.fill('Trip #');
        await expect(dialog(page).locator('.batch-rename-preview tbody tr')).toHaveCount(2);
        await dialog(page).locator('button.primary').tap();
        await expect(dialog(page)).toHaveCount(0);
        await expect(item(page, `${scratch}/Trip 1.jpg`)).toBeVisible();
        await expect(item(page, `${scratch}/Trip 2.jpg`)).toBeVisible();
    });

    test('copy a file into a folder; the operations panel keeps clear of the selection bar', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/C.jpg`, 'copyTo');
        await dialog(page).locator('.transfer-folder', { hasText: 'Inside' }).tap();
        await dialog(page).locator('button.primary').tap();
        await expect(dialog(page)).toHaveCount(0);
        await expect.poll(() => fs.existsSync(path.join(library, scratch, 'Inside/C.jpg')), { timeout: 15_000 }).toBe(true);
        await expect(item(page, `${scratch}/C.jpg`)).toBeVisible();

        // Both at the bottom of a phone: never over each other.
        await item(page, `${scratch}/A.jpg`).locator('.select-box').tap();
        const panel = page.locator('sfe-operations-panel .operation').first();
        const dock = page.locator('.selection-dock .selection-bar');
        await expect(dock).toBeVisible();
        if (await panel.isVisible()) {
            const a = (await panel.boundingBox())!;
            const b = (await dock.boundingBox())!;
            const overlap = a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
            expect(overlap, 'operations panel over the selection bar').toBe(false);
        }
    });

    test('compress a file to a zip', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await fromItemMenu(page, `${scratch}/A.jpg`, 'compress');
        await expectPhoneDialog(page);
        await dialog(page).locator('[data-compress]').tap();
        await expect(dialog(page)).toHaveCount(0);
        await expect(item(page, `${scratch}/A.zip`)).toBeVisible({ timeout: 15_000 });
    });

    test('extract a zip beside it', async ({ page, scratch }) => {
        fs.copyFileSync(path.join(library, 'Documents/Mountain trip.zip'), path.join(library, scratch, 'Trip.zip'));
        await openFolder(page, scratch);
        await fromItemMenuEntry(page, `${scratch}/Trip.zip`, 'extractBeside');
        await expect(item(page, `${scratch}/Trip`)).toBeVisible({ timeout: 20_000 });
    });

    test('edit a text file and save it; leaving with changes asks first', async ({ page, scratch }) => {
        const file = path.join(library, scratch, 'Notes.txt');
        fs.writeFileSync(file, 'First line\n');
        await openFolder(page, scratch);
        await fromItemMenuEntry(page, `${scratch}/Notes.txt`, 'editText');
        const viewer = page.locator('dialog.viewer[open]');
        const editor = viewer.locator('.cm-content');
        await expect(editor).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, 'dialog.viewer[open] .text-toolbar');
        await editor.tap();
        await page.keyboard.press('Control+End');
        await page.keyboard.type('Typed on a phone');
        await viewer.locator('[data-save]').tap();
        await expect.poll(() => fs.readFileSync(file, 'utf8')).toContain('Typed on a phone');

        // A change not saved: closing the viewer asks; "no" keeps it open with the text.
        await editor.tap();
        await page.keyboard.type(' again');
        page.once('dialog', question => void question.dismiss());
        await viewer.getByRole('button', { name: 'Close' }).tap();
        await expect(viewer).toHaveCount(1);
        await expect(editor).toContainText('again');
        page.once('dialog', question => void question.accept());
        await viewer.getByRole('button', { name: 'Close' }).tap();
        await expect(viewer).toHaveCount(0);
        expect(fs.readFileSync(file, 'utf8')).not.toContain('again');
    });
});

/** Opens the menu of `path` and taps `entry`, which opens no dialog. */
async function fromItemMenuEntry(page: Page, path: string, entry: string): Promise<void> {
    await item(page, path).locator('.item-menu-button').tap();
    await page.locator(`${menu} [data-entry="${entry}"]`).tap();
}
