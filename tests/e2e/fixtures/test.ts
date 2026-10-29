// The `test` every spec imports: Playwright's, signed in as `account` (default the administrator), and failing on
// what a user wouldn't see but is still a bug: console errors, uncaught page errors, CSP violations, server errors.
import { test as base, expect, type Page } from '@playwright/test';
import { auth, type Account } from '../paths';

type Options = {
    /** Who is signed in: `test.use({ account: 'sam' })`. */
    account: Account;
    /** Console errors a spec expects (a refused request it provokes...): matched against the message. */
    allowedErrors: RegExp[];
    /** The CPU slowed this many times (Chromium only): a phone's speed on a desktop (project `android-slow`). */
    cpuSlowdown: number;
};

export const test = base.extend<Options & { guard: void; slowCpu: void }>({
    account: ['admin', { option: true }],
    allowedErrors: [[], { option: true }],
    cpuSlowdown: [1, { option: true }],
    slowCpu: [async ({ page, cpuSlowdown, browserName }, use) => {
        if (cpuSlowdown > 1 && browserName === 'chromium') {
            const cdp = await page.context().newCDPSession(page);
            await cdp.send('Emulation.setCPUThrottlingRate', { rate: cpuSlowdown });
        }
        await use();
    }, { auto: true }],
    storageState: async ({ account }, use) => use(auth(account)),
    guard: [async ({ page, allowedErrors }, use) => {
        const problems: string[] = [];
        const allowed = (text: string) => allowedErrors.some(pattern => pattern.test(text));
        page.on('console', message => {
            if (message.type() === 'error' && !allowed(message.text()))
                problems.push(`console: ${message.text()}`);
        });
        page.on('pageerror', error => { if (!allowed(error.message)) problems.push(`page error: ${error.message}`); });
        page.on('response', response => {
            if (response.status() >= 500 && !allowed(response.url()))
                problems.push(`server: ${response.status()} ${response.request().method()} ${response.url()}`);
        });
        await page.addInitScript(() => {
            document.addEventListener('securitypolicyviolation', event =>
                console.error(`CSP violation: ${event.violatedDirective} ${event.blockedURI}`));
        });
        await use();
        expect(problems, 'errors while the test ran').toEqual([]);
    }, { auto: true }],
});

export { expect };

/** Opens the listing of `folder` (root-relative, `/` separators) and waits for its items. */
export async function openFolder(page: Page, folder: string): Promise<void> {
    await page.goto(`./?path=${encodeURIComponent(folder)}`);
    await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
}

/** A listed file or folder by its root-relative path. */
export const item = (page: Page, itemPath: string) => page.locator(`sfe-listing [data-path="${itemPath}"]`);

/** Phones up to 640 px wide show menus and popups as sheets at the bottom (styles/phone-sheets.css). */
export const isSheetWidth = (page: Page) => page.viewportSize()!.width <= 640;

/** Closes the open menu as a user would: a tap on the sheet's backdrop, or Esc where the menu is a popup. */
export async function closeMenu(page: Page): Promise<void> {
    const backdrop = page.locator('.item-menu-backdrop');
    if (await backdrop.isVisible())
        await backdrop.tap({ position: { x: 20, y: 20 } });
    else
        await page.keyboard.press('Escape');
    await expect(page.locator('.item-menu[role="menu"]')).toBeHidden();
}
