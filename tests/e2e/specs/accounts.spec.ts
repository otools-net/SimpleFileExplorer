// Accounts and the connection on a phone: sign out from the main menu, a session
// that ended (back to the sign-in page, then where it was), the server not answering (the ⋮ warning), saving a
// preference. Each signs in on its own: signing out ends the session the other specs share.
import type { Page } from '@playwright/test';
import { expect, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectTapTargets } from '../fixtures/layout';
import { accounts } from '../paths';

/** Signs `sam` in through the page, by finger. */
async function signIn(page: Page): Promise<void> {
    const name = page.getByLabel(/user name|name/i).first();
    await expect(name).toBeVisible();
    await name.tap();
    await name.fill(accounts.sam.userName);
    await page.getByLabel(/password/i).first().fill(accounts.sam.password);
    await page.getByRole('button', { name: /sign in/i }).first().tap();
}

test.describe('accounts', () => {
    // Signed out: the app's requests are refused (401) until the sign-in page shows.
    test.use({ storageState: { cookies: [], origins: [] }, allowedErrors: /status of 401/ });

    test('sign out from the main menu', async ({ page }) => {
        await page.goto('./');
        await signIn(page);
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        await page.getByRole('button', { name: 'More', exact: true }).tap();
        await page.locator('sfe-main-menu #menu').getByRole('menuitem', { name: 'Sign out' }).tap();
        await expect(page.getByLabel(/password/i).first()).toBeVisible();
        await expectFitsScreen(page);
    });

    test('a session that ended: the sign-in page, then back to the folder', async ({ page, context }) => {
        await page.goto('./');
        await signIn(page);
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        // Ended on the server's side (expired, signed out elsewhere): the browser's cookie is no longer good.
        await context.clearCookies();
        await page.goto('./?path=' + encodeURIComponent('Photos/2025 Mountains'));
        await signIn(page);
        await expect.poll(() => new URL(page.url()).searchParams.get('path')).toBe('Photos/2025 Mountains');
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
    });
});

test.describe('the connection', () => {
    // The change feed refused while "the server is down": the browser logs it.
    test.use({ allowedErrors: /api\/files\/changes|ERR_FAILED|EventSource/ });

    test('the server not answering: a warning on the ⋮, gone when it answers again', async ({ page }) => {
        await page.clock.install();
        await page.route('**/api/files/changes', route => route.abort());
        await openFolder(page, 'Photos');
        const more = page.getByRole('button', { name: /^More/ }).first();
        await page.clock.fastForward(11_000);
        await expect(more.locator('.menu-keep-alive.failing')).toBeVisible();
        await more.tap();
        await expect(page.locator('sfe-main-menu .menu-server-lost')).toBeVisible();
        await expectFitsScreen(page);
        await expectTapTargets(page, 'sfe-main-menu');
        await page.keyboard.press('Escape');

        await page.unroute('**/api/files/changes');
        await page.clock.fastForward(70_000);
        await expect(more.locator('.menu-keep-alive.failing')).toHaveCount(0, { timeout: 15_000 });
    });
});

test.describe('preferences', () => {
    test.use({ account: 'sam' });

    test('a preference saved from a phone, kept after a reload', async ({ page }, testInfo) => {
        // One device: the projects run at once, and this is one account's setting.
        test.skip(testInfo.project.name !== 'android-small', "one account's setting: on the narrowest phone only");
        // My account: the preferences of the account signed in (the other sections are the administrators').
        let id = '';
        const open = async () => {
            // The boxes show the defaults until the account's own values come.
            const loaded = page.waitForResponse(response => response.url().includes('api/me/preferences') && response.request().method() === 'GET');
            await page.goto('./admin?section=account');
            await loaded;
            await expect(page.locator('.admin-head')).toBeVisible();
            const all = page.getByRole('button', { name: 'Show all settings' });
            if (await all.isVisible())
                await all.tap();
            const box = id ? page.locator(`input#${id}`) : page.locator('main .setting-check input[type=checkbox]:enabled').first();
            await expect(box).toBeVisible();
            id ||= (await box.getAttribute('id'))!;
            return box;
        };
        const button = page.locator('main').getByRole('button', { name: 'Save', exact: true });
        // Saved once the server answered (the button is disabled while saving too; leaving earlier cancels it).
        const save = async () => {
            const saved = page.waitForResponse(response => response.url().includes('api/me/preferences') && response.request().method() === 'PATCH');
            await button.tap();
            expect((await saved).ok()).toBe(true);
            await expect(button).toBeDisabled();
        };
        let box = await open();
        await expectFitsScreen(page);
        const before = await box.isChecked();
        await box.tap();
        await expect(button).toBeEnabled();
        await save();

        box = await open();
        await expect(box).toBeChecked({ checked: !before });
        // Back as it was: the account is shared with other specs.
        await box.tap();
        await save();
    });
});
