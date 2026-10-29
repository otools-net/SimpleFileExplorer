// The video player on a phone (mobile e2e plan, Phase 2, video): Play by a tap, a tap shows / hides the controls,
// a double tap on the right jumps forward, the controls fit on one line with big enough buttons, their "⋯" is a sheet.
// The samples are WebM: a browser that can't play them (WebKit on Windows: the app falls back to the server's
// conversion, which needs ffmpeg, then says it can't play it) skips these.
import type { Page } from '@playwright/test';
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectTapTargets } from '../fixtures/layout';

const player = 'dialog.viewer[open] sfe-video-player';

/** Opens the video and waits until the player has settled: its video's details loaded, or the app saying it can't play it. */
async function openVideo(page: Page): Promise<void> {
    await openFolder(page, 'Videos/Holidays');
    await item(page, 'Videos/Holidays/Campfire.webm').locator('.item-link').first().tap();
    const message = page.locator('dialog.viewer[open] .viewer-message');
    await expect.poll(async () => await message.isVisible()
        // Not locator.evaluate: it waits for a video the app may have replaced by its message.
        || await page.evaluate(selector => (document.querySelector(selector) as HTMLVideoElement | null)?.readyState! >= 1, `${player} video`)).toBe(true);
    test.skip(await message.isVisible(), 'this browser plays no WebM (and the test server has no ffmpeg to convert it)');
    // The controls measure their row again once the length is known.
    await page.waitForTimeout(300);
}

const video = (page: Page) => page.locator(`${player} video`);

test.describe('video', () => {
    test('the controls fit on one line, with buttons big enough', async ({ page }) => {
        await openVideo(page);
        const row = page.locator(`${player} .player-row`);
        await expect(row).toBeVisible();
        await expectFitsScreen(page);
        const box = (await row.boundingBox())!;
        expect(box.height, 'one line').toBeLessThan(56);
        expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize()!.width);
        await expectTapTargets(page, `${player} .player-controls`);
    });

    test('Play by a tap; a tap on the picture hides and shows the controls', async ({ page }) => {
        await openVideo(page);
        await page.locator(`${player} .player-big-play, ${player} .player-row button[aria-label^="Play"]`).first().tap();
        await expect.poll(() => video(page).evaluate(v => (v as HTMLVideoElement).paused)).toBe(false);

        const host = page.locator(player);
        await expect(host).toHaveClass(/controls-visible/);
        const surface = (await page.locator(`${player} .player-surface`).boundingBox())!;
        // The middle of the picture: a single tap (not one of a double tap's thirds).
        await page.touchscreen.tap(surface.x + surface.width / 2, surface.y + surface.height / 3);
        await expect(host).not.toHaveClass(/controls-visible/, { timeout: 2000 });
        await page.waitForTimeout(400);
        await page.touchscreen.tap(surface.x + surface.width / 2, surface.y + surface.height / 3);
        await expect(host).toHaveClass(/controls-visible/, { timeout: 2000 });
    });

    test('a double tap on the right third jumps forward', async ({ page }) => {
        await openVideo(page);
        const before = await video(page).evaluate(v => (v as HTMLVideoElement).currentTime);
        const surface = (await page.locator(`${player} .player-surface`).boundingBox())!;
        const x = surface.x + surface.width * 0.85;
        const y = surface.y + surface.height / 2;
        await page.touchscreen.tap(x, y);
        await page.waitForTimeout(80);
        await page.touchscreen.tap(x, y);
        await expect.poll(() => video(page).evaluate(v => (v as HTMLVideoElement).currentTime)).toBeGreaterThan(before + 1);
    });

    test('the "⋯" of the controls opens as a sheet', async ({ page }) => {
        await openVideo(page);
        const more = page.locator(`${player} .player-row .overflow-more.shown`);
        test.skip(!await more.isVisible(), 'every control fits at this width');
        await more.tap();
        const menu = page.locator('.item-menu[role="menu"]');
        await expect(menu).toBeVisible();
        if (isSheetWidth(page)) {
            const box = (await menu.boundingBox())!;
            expect(Math.round(box.width)).toBe(page.viewportSize()!.width);
            expect(Math.round(box.y + box.height)).toBe(page.viewportSize()!.height);
        }
        await expectTapTargets(page, '.item-menu[role="menu"]');
        await closeMenu(page);
        await expect(page.locator('dialog.viewer[open]'), 'the viewer stays').toHaveCount(1);
    });
});
