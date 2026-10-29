// Accessibility of the main screens on a phone (mobile e2e plan, Phase 3, `@a11y`): axe-core's WCAG 2.2 A and AA rules
// on each screen's main states. Serious and critical findings fail; moderate and minor ones are listed in the report.
// Once per run (the android device): the rules don't depend on the engine, and the layouts are checked elsewhere.
import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { closeMenu, expect, item, openFolder, test } from '../fixtures/test';

const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** The page as it is now: serious and critical violations fail the test, the others go in its report. */
async function audit(page: Page, screen: string, include?: string): Promise<void> {
    let builder = new AxeBuilder({ page }).withTags(TAGS);
    if (include)
        builder = builder.include(include);
    const { violations } = await builder.analyze();
    const line = (v: (typeof violations)[number]) =>
        `${v.id} (${v.impact}): ${v.help} — ${v.nodes.slice(0, 4).map(node => node.target.join(' ')).join(' | ')}${v.nodes.length > 4 ? ` … ${v.nodes.length} in all` : ''}`;
    const minor = violations.filter(v => v.impact !== 'serious' && v.impact !== 'critical');
    if (minor.length)
        test.info().annotations.push({ type: `a11y ${screen}`, description: minor.map(line).join('\n') });
    expect(violations.filter(v => v.impact === 'serious' || v.impact === 'critical').map(line), `${screen}: serious or critical`).toEqual([]);
}

test.describe('@a11y', () => {
    test.beforeEach(({}, testInfo) => test.skip(testInfo.project.name !== 'android', 'once per run, on the android device'));

    test('the listing, selected items, an item menu', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await audit(page, 'listing');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.select-box').tap();
        await expect(page.locator('.selection-dock .selection-bar')).toBeVisible();
        await audit(page, 'selection bar', '.selection-dock');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-menu-button').tap();
        await expect(page.locator('.item-menu[role="menu"]')).toBeVisible();
        await audit(page, 'item menu', '.item-menu[role="menu"]');
        await closeMenu(page);
    });

    test('the viewer and a dialog', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-link').first().tap();
        await expect(page.locator('dialog.viewer[open] img:not(.preview)')).toBeVisible();
        await audit(page, 'viewer', 'dialog.viewer[open]');
        await page.locator('dialog.viewer[open]').getByRole('button', { name: 'Close' }).tap();

        await item(page, 'Photos/2025 Mountains/Summit.jpg').locator('.item-menu-button').tap();
        await page.locator('.item-menu[role="menu"] [data-entry="properties"]').tap();
        await expect(page.locator('dialog.sheet[open]')).toBeVisible();
        await audit(page, 'properties dialog', 'dialog.sheet[open]');
    });

    test('search results', async ({ page }) => {
        await page.goto('./?q=' + encodeURIComponent('summit'));
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        await audit(page, 'search');
    });

    test('the administration and my account', async ({ page }) => {
        await page.goto('./admin');
        await expect(page.locator('nav.admin-nav')).toBeVisible();
        await audit(page, 'administration');
        await page.goto('./admin?section=account');
        await expect(page.locator('.admin-head')).toBeVisible();
        await audit(page, 'my account');
    });

    test.describe('signed out', () => {
        test.use({ storageState: { cookies: [], origins: [] }, allowedErrors: /status of 401/ });

        test('the sign-in page', async ({ page }) => {
            await page.goto('./');
            await expect(page.getByLabel(/password/i).first()).toBeVisible();
            await audit(page, 'sign-in');
        });
    });
});
