// The `test` every spec imports: Playwright's, signed in as `account` (default the administrator), and failing on
// what a user wouldn't see but is still a bug: console errors, uncaught page errors, CSP violations, server errors.
// On a phone, the screen a test ends on is checked too (`layoutCheck`): no sideways scroll, nothing cut off, no tap
// target under 24 px (mobile e2e plan, Phase 3).
import { test as base, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { auth, library, type Account } from '../paths';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from './layout';

type Options = {
    /** Who is signed in: `test.use({ account: 'sam' })`. */
    account: Account;
    /**
     * Console errors a spec expects (a refused request it provokes...): matched against the message. One expression
     * (`/a|b/`): `test.use` takes an array as `[value, options]`.
     */
    allowedErrors: RegExp | null;
    /** The CPU slowed this many times (Chromium only): a phone's speed on a desktop (project `android-slow`). */
    cpuSlowdown: number;
    /**
     * The screen the test ends on fits the phone, nothing cut off, its targets ≥ 24 px. Off (`false`) for a test that
     * ends on a state made on purpose (a measurement, a probe), with a comment saying why.
     */
    layoutCheck: boolean;
};

export const test = base.extend<Options & { guard: void; slowCpu: void; finalLayout: void; scratch: string }>({
    /**
     * A folder of this test's own, for what changes files (workers run in parallel on one library): `Scratch/<id>`
     * with three sample photos (`A.jpg`, `B.jpg`, `C.jpg`) and an empty folder `Inside`; removed after the test.
     */
    scratch: async ({}, use, testInfo) => {
        const relative = `Scratch/${testInfo.project.name}-${testInfo.testId}-${testInfo.repeatEachIndex}-${testInfo.retry}`;
        const folder = path.join(library, relative);
        fs.rmSync(folder, { recursive: true, force: true });
        fs.mkdirSync(path.join(folder, 'Inside'), { recursive: true });
        for (const [name, from] of [['A.jpg', 'Photos/2025 Mountains/Summit.jpg'], ['B.jpg', 'Photos/2025 Mountains/Blue hour.jpg'], ['C.jpg', 'Photos/Sunrise.jpg']])
            fs.copyFileSync(path.join(library, from), path.join(folder, name));
        await use(relative);
        // The app may still hold it open for a moment (Windows: EPERM): retried, and never a failure of the test.
        try {
            fs.rmSync(folder, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
        } catch { /* left for the next run's server start, which empties the library */ }
    },
    account: ['admin', { option: true }],
    allowedErrors: [null, { option: true }],
    cpuSlowdown: [1, { option: true }],
    layoutCheck: [true, { option: true }],
    finalLayout: [async ({ page, layoutCheck, isMobile }, use, testInfo) => {
        await use();
        // Only after a test that passed (a failure says more than its layout), on a phone, on one of the app's pages.
        if (!layoutCheck || !isMobile || testInfo.status !== testInfo.expectedStatus || testInfo.status !== 'passed'
            || page.isClosed() || !page.url().startsWith('http'))
            return;
        await test.step('the final screen fits the phone', async () => {
            await expectFitsScreen(page);
            await expectNothingCut(page);
            await expectTapTargets(page);
        });
    }, { auto: true }],
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
        const allowed = (text: string) => allowedErrors?.test(text) ?? false;
        page.on('console', message => {
            // A video's trickplay description (not a sheet) answers 404 until it is made, or outside the trickplay's
            // folders: the app asks, by design (TrickplayEndpoints `GetTrickplay`); the browser logs it. Only with ffmpeg.
            if (/^Failed to load resource: .*404/.test(message.text()) && /\/api\/trickplay\/files\/[^?]+(\?(?!.*\bsheet=).*)?$/.test(message.location().url))
                return;
            // A failed load names its address (the browser's message doesn't).
            const text = message.text().startsWith('Failed to load resource') ? `${message.text()} ${message.location().url}` : message.text();
            if (message.type() === 'error' && !allowed(text))
                problems.push(`console: ${text}`);
        });
        // WebKit reports a request that leaving the page cancelled as "… due to access control checks" (the app has one
        // origin, nothing else gives it): forgiven when the page navigates within the second; any other time it counts.
        let cancelled: { text: string; at: number }[] = [];
        page.on('framenavigated', frame => {
            if (frame === page.mainFrame())
                cancelled = cancelled.filter(each => Date.now() - each.at > 1000);
        });
        page.on('pageerror', error => {
            if (allowed(error.message))
                return;
            if (/due to access control checks\.$/.test(error.message))
                cancelled.push({ text: `page error: ${error.message}`, at: Date.now() });
            else
                problems.push(`page error: ${error.message}`);
        });
        page.on('response', response => {
            if (response.status() >= 500 && !allowed(response.url()))
                problems.push(`server: ${response.status()} ${response.request().method()} ${response.url()}`);
        });
        await page.addInitScript(() => {
            document.addEventListener('securitypolicyviolation', event =>
                console.error(`CSP violation: ${event.violatedDirective} ${event.blockedURI}`));
        });
        await use();
        expect([...problems, ...cancelled.map(each => each.text)], 'errors while the test ran').toEqual([]);
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
