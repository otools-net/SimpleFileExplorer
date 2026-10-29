// @diagnose (not in normal runs: SFE_E2E_DIAGNOSE=1): what opening the item menu's sheet costs, and which part of it.
// Prints the time to open with the page as it is, then with the sheet's page lock (`html:has(.sheet-backdrop)`,
// styles/phone-sheets.css) taken out, in a small folder and in a large one. Mobile e2e plan, Phase 0.
import { expect, item, openFolder, test } from '../../fixtures/test';
import { withoutPageLock } from '../../fixtures/diagnose';
import { expectOpensWithin } from '../../fixtures/timing';

const menu = '.item-menu[role="menu"]';

for (const [label, folder, file] of [
    ['6 photos', 'Photos/2025 Mountains', 'Photos/2025 Mountains/Summit.jpg'],
    ['600 photos', 'Many photos', 'Many photos/Photo 002.jpg'],
] as const) {
    for (const lock of [true, false]) {
        test(`@diagnose @timing item menu, ${label}, page lock ${lock ? 'on' : 'off'}`, async ({ page }) => {
            test.skip(process.env.SFE_E2E_DIAGNOSE !== '1', 'diagnostics: SFE_E2E_DIAGNOSE=1');
            await openFolder(page, folder);
            if (!lock)
                expect(await withoutPageLock(page)).toBeGreaterThan(0);
            const tile = item(page, file);
            await tile.locator('.select-box').tap();
            await expect(tile).toHaveClass(/selected/);
            await page.waitForTimeout(400);
            await expectOpensWithin(page, menu, () => tile.locator('.item-menu-button').tap(), 10_000);
        });
    }
}
