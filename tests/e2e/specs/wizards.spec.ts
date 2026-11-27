// The wizards on a phone: every registered wizard opens, fits and leaves; the new-space wizard's Next and Back.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '../fixtures/test';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

/** The ids of the registry's wizards (wizard-registry.ts), read from its source: the specs don't build the app. */
function wizardIds(): string[] {
    const here = path.dirname(fileURLToPath(import.meta.url));
    const registry = fs.readFileSync(
        path.resolve(here, '../../../src/Sfe.Web/src/app/wizards/wizard-registry.ts'),
        'utf8',
    );
    return [...registry.matchAll(/^ {8}id: '([a-z-]+)',$/gm)].map((match) => match[1]);
}

test('the registry lists wizards', () => {
    expect(wizardIds().length).toBeGreaterThan(0);
});

for (const id of wizardIds()) {
    test(`the wizard ${id} opens on its first step, fits, and Cancel leaves it`, async ({ page }) => {
        await page.goto(`./wizard?id=${id}`);
        const title = page.locator('.wizard-stage h2');
        await expect(title).toBeVisible();
        await expect(page.locator('.wizard-progress')).toHaveText(/^Step 1 of \d+$/);
        await expect(title).toBeFocused();
        await expectFitsScreen(page);
        await expectNothingCut(page);
        await expectTapTargets(page, 'sfe-wizard-page');

        await page.locator('.wizard-actions').getByRole('button', { name: 'Cancel' }).tap();
        await expect(page).toHaveURL(/\/admin\?section=/);
    });
}

test('the new-space wizard keeps the choices through Next and Back, and asks before leaving', async ({ page }) => {
    await page.goto('./wizard?id=new-space');
    // The allowed roots depend on the server's configuration: the first root, then its first folder (after ‥).
    const picker = page.locator('.folder-picker');
    await picker.locator('li button').first().tap();
    const folder = picker.locator('li button').nth(1);
    await expect(picker.locator('li button').first()).toHaveText('‥');
    const chosen = (await folder.textContent())!.trim();
    await folder.tap();
    await expect(page.locator('.folder-picker-path')).toContainText(chosen);

    await page.getByRole('button', { name: 'Next' }).tap();
    await expect(page.locator('#new-space-name')).toHaveValue(chosen);
    await expect(page.locator('.wizard-progress')).toHaveText('Step 2 of 4');
    await page.getByRole('button', { name: 'Back', exact: true }).tap();
    await expect(page.locator('.folder-picker-path')).toContainText(chosen);

    let asked = '';
    page.once('dialog', (dialog) => {
        asked = dialog.message();
        void dialog.dismiss();
    });
    await page.getByRole('button', { name: 'Cancel' }).tap();
    await expect.poll(() => asked).toContain('Leave the wizard?');
    await expect(page).toHaveURL(/\/wizard\?id=new-space/);
});
