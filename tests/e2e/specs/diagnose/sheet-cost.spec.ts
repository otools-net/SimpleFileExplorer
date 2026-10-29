// @diagnose (not in normal runs: SFE_E2E_DIAGNOSE=1): what opening the item menu's sheet costs, and which part of it.
// Prints the time to open with the page as it is, then with the sheet's page lock (`html:has(.sheet-backdrop)`,
// styles/phone-sheets.css) taken out, in a small folder and in a large one. Mobile e2e plan, Phase 0.
import { expect, item, openFolder, test } from '../../fixtures/test';
import { expectOpensWithin } from '../../fixtures/timing';

const menu = '.item-menu[role="menu"]';

/** Removes the CSS rules whose selector contains `html:has(.sheet-backdrop)` from the page's style sheets. */
async function withoutPageLock(page: import('@playwright/test').Page): Promise<number> {
    return page.evaluate(() => {
        let removed = 0;
        const strip = (rules: CSSRuleList) => {
            for (let i = rules.length - 1; i >= 0; i--) {
                const rule = rules[i];
                if (rule instanceof CSSStyleRule && rule.selectorText.includes(':has(.sheet-backdrop)')) {
                    (rule.parentRule as CSSGroupingRule | null ?? rule.parentStyleSheet!).deleteRule(i);
                    removed++;
                } else if ('cssRules' in rule) {
                    strip((rule as CSSGroupingRule).cssRules);
                }
            }
        };
        for (const sheet of document.styleSheets)
            try { strip(sheet.cssRules); } catch { /* another origin */ }
        return removed;
    });
}

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
