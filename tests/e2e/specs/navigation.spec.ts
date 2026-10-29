// Moving around on a phone: files and folders opened by a tap, the viewer's swipes, Back,
// more items as the listing scrolls.
import type { Page } from '@playwright/test';
import { expect, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen } from '../fixtures/layout';
import { needsTouchGestures, pinch, swipe, tapWhenSettled } from '../fixtures/touch';

const title = (page: Page) => page.locator('dialog.viewer[open] .viewer-title strong');

test.describe('navigation', () => {
    test('a tap opens a picture in the viewer; closing keeps the listing where it was', async ({ page }) => {
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 900));
        await page.waitForTimeout(300);
        const photo = item(page, 'Many photos/Photo 020.jpg');
        await tapWhenSettled(page, photo.locator('.item-link').first());
        await expect(title(page)).toHaveText('Photo 020.jpg');
        const scrollY = await page.evaluate(() => window.scrollY);
        await expectFitsScreen(page);
        await page.locator('dialog.viewer[open]').getByRole('button', { name: 'Close' }).tap();
        await expect(page.locator('dialog.viewer[open]')).toHaveCount(0);
        expect(Math.abs(await page.evaluate(() => window.scrollY) - scrollY)).toBeLessThan(20);
        await expect(photo).toBeInViewport();
    });

    test('a swipe on the viewer goes to the next and the previous file', async ({ page, browserName }) => {
        needsTouchGestures(browserName);
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Blue hour.jpg').locator('.item-link').first().tap();
        await expect(title(page)).toHaveText('Blue hour.jpg');
        const stage = (await page.locator('dialog.viewer[open] .viewer-stage').boundingBox())!;
        const y = stage.y + stage.height / 2;
        await swipe(page, { x: stage.x + stage.width * 0.8, y }, { x: stage.x + stage.width * 0.2, y }, 200);
        await expect(title(page)).not.toHaveText('Blue hour.jpg');
        await swipe(page, { x: stage.x + stage.width * 0.2, y }, { x: stage.x + stage.width * 0.8, y }, 200);
        await expect(title(page)).toHaveText('Blue hour.jpg');
        // Not the browser's swipe back (Chrome went back a page: base.css, the viewer's overscroll-behavior-x).
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos/2025 Mountains');
    });

    // Decided 2026-10-29: two fingers zoom a picture (@use-gesture), the page itself doesn't zoom, the file stays.
    test('two fingers zoom a picture and fit it back; neither the page zooms nor the file changes', async ({ page, browserName }) => {
        needsTouchGestures(browserName);
        await openFolder(page, 'Photos/2025 Mountains');
        await item(page, 'Photos/2025 Mountains/Blue hour.jpg').locator('.item-link').first().tap();
        const picture = page.locator('dialog.viewer[open] .viewer-stage > img:not(.preview)');
        await expect(picture).toBeVisible();
        await expect.poll(() => picture.evaluate(img => (img as HTMLImageElement).complete)).toBe(true);
        const fit = (await picture.boundingBox())!;
        const centre = { x: fit.x + fit.width / 2, y: fit.y + fit.height / 2 };

        await pinch(page, centre, 60, 240);
        await expect(picture).toHaveClass(/actual-size/);
        const zoomed = (await picture.boundingBox())!;
        expect(zoomed.width, 'larger').toBeGreaterThan(fit.width * 1.5);
        expect(await page.evaluate(() => visualViewport?.scale ?? 1), 'the page itself not zoomed').toBe(1);
        await expect(title(page)).toHaveText('Blue hour.jpg');

        await pinch(page, centre, 240, 50);
        await expect(picture).not.toHaveClass(/actual-size/);
        await expect(title(page)).toHaveText('Blue hour.jpg');
    });

    test('a tap opens a folder, Back comes back to it', async ({ page }) => {
        await openFolder(page, 'Photos');
        await item(page, 'Photos/Family').locator('a').first().tap();
        await expect(item(page, 'Photos/Family/Picnic.jpg')).toBeVisible();
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos/Family');
        await page.goBack();
        await expect(item(page, 'Photos/Family')).toBeVisible();
        expect(new URL(page.url()).searchParams.get('path')).toBe('Photos');
    });

    /** Scrolls 'Many photos' down, leaves it with `leave`, comes back with Back: the item at the top is back in place. */
    async function backKeepsPlace(page: Page, leave: () => Promise<void>) {
        await openFolder(page, 'Many photos');
        await page.evaluate(() => window.scrollTo(0, 2500));
        await page.waitForTimeout(800);
        const anchor = await page.evaluate(() => {
            const items = [...document.querySelectorAll('sfe-listing [data-path]')];
            const first = items.find(element => element.getBoundingClientRect().top >= 0)!;
            return { path: first.getAttribute('data-path')!, top: first.getBoundingClientRect().top };
        });
        await leave();
        await page.goBack();
        const back = item(page, anchor.path);
        await expect(back).toBeAttached();
        await page.waitForTimeout(800);
        const top = (await back.boundingBox())!.y;
        expect(Math.abs(top - anchor.top), `${anchor.path} back where it was`).toBeLessThan(40);
    }

    test('Back from the parent folder (a link of the app) brings the position back', async ({ page }) => {
        await backKeepsPlace(page, async () => {
            // The one a finger can reach (the sticky bar's): tapping the one scrolled away made Playwright scroll to it.
            const links = page.getByRole('link', { name: 'Parent folder' });
            let up = links.first();
            for (let i = 0; i < await links.count(); i++) {
                const box = await links.nth(i).boundingBox();
                if (box && box.y >= 0 && box.y < page.viewportSize()!.height)
                    up = links.nth(i);
            }
            // At its place on the screen: locator.tap() first scrolls a sticky element "into view", which moved the page.
            await expect(up).toBeInViewport();
            const box = (await up.boundingBox())!;
            expect(await page.evaluate(() => window.scrollY)).toBe(2500);
            await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2);
            await expect(item(page, 'Many photos')).toBeVisible();
        });
    });

    test('Back from another page (the app loaded again) brings the position back', async ({ page, browserName }) => {
        // WebKit (Playwright on Windows) gives the page back with `history.state` null, where the position is kept:
        // not known yet for a real iPhone (mobile-e2e-plan.md). If it is so there, keep it in sessionStorage too.
        test.fixme(browserName === 'webkit', 'history.state comes back null in WebKit on Windows: check on an iPhone');
        await backKeepsPlace(page, async () => {
            await page.goto('./?path=Photos');
            await expect(item(page, 'Photos/Family')).toBeVisible();
        });
    });

    test('scrolling a large folder loads the rest', async ({ page }) => {
        await openFolder(page, 'Many photos');
        const items = page.locator('sfe-listing [data-path]');
        expect(await items.count()).toBeLessThan(600);
        for (let i = 0; i < 60 && await items.count() < 600; i++) {
            await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
            await page.waitForTimeout(250);
        }
        await expect(items).toHaveCount(600);
    });
});
