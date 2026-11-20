// The picture editor on a phone: Crop… from a picture's menu opens it. The editor first checks that the browser lets
// the page read back what it draws on a canvas (core/canvas-probe.ts): a real browser must pass that check, which
// unit tests (jsdom, fake canvases) can't show. Firefox's refusal (resistFingerprinting) is checked by hand
// (agents/docs/features/picture-editor.md).
import { expect, item, openFolder, test } from '../fixtures/test';
import { tapWhenSettled } from '../fixtures/touch';

test.describe('picture editor', () => {
    test('Crop… opens the editor on the picture, the canvas check passing', async ({ page, scratch }) => {
        await openFolder(page, scratch);
        await tapWhenSettled(page, item(page, `${scratch}/A.jpg`).locator('.item-menu-button'));
        await page.locator('.item-menu[role="menu"] [data-entry="editPicture"]').tap();

        const editor = page.locator('dialog.image-editor[open]');
        await expect(editor).toBeVisible();
        await expect(editor.locator('.editor-canvas canvas')).toBeAttached();
        await expect(page.locator('.toast.error')).toHaveCount(0);
    });
});
