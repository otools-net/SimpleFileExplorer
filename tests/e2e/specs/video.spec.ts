// The video player on a phone (mobile e2e plan, Phase 2, video): Play by a tap, a tap shows / hides the controls,
// a double tap on the right jumps forward, the controls fit on one line with big enough buttons, their "⋯" is a sheet.
// The samples are WebM: a browser that can't play them (WebKit on Windows: the app falls back to the server's
// conversion, which needs ffmpeg, then says it can't play it) skips these.
import type { Page } from '@playwright/test';
import { closeMenu, expect, isSheetWidth, item, openFolder, test } from '../fixtures/test';
import { expectFitsScreen, expectTapTargets } from '../fixtures/layout';
import { turn } from '../fixtures/viewport';

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

    test("Speed: its rates by a tap (its button, or in the \"⋯\" in the menu's place, Back returns)", async ({ page }) => {
        await openVideo(page);
        const button = page.locator(`${player} .player-speed`);
        if (await button.isVisible()) {
            await button.tap();
            const list = page.locator(`${player} .player-speed-menu`);
            await expect(list).toBeVisible();
            await expectFitsScreen(page);
            await expectTapTargets(page, `${player} .player-speed-menu`);
            await list.getByRole('menuitemradio', { name: '1.5×' }).tap();
            await expect(list).toHaveCount(0);
        } else {
            await page.locator(`${player} .player-row .overflow-more.shown`).tap();
            const menu = page.locator('.item-menu[role="menu"]');
            const before = (await menu.boundingBox())!;
            await menu.locator('[data-entry="speed"]').tap();
            await expect(menu.locator('[data-entry="speed-1.5"]')).toBeVisible();
            const submenu = (await menu.boundingBox())!;
            if (isSheetWidth(page))
                expect(Math.round(submenu.y + submenu.height), 'still against the bottom').toBe(Math.round(before.y + before.height));
            await menu.locator('[data-entry="back"]').tap();
            await expect(menu.locator('[data-entry="speed"]')).toBeVisible();
            await menu.locator('[data-entry="speed"]').tap();
            await menu.locator('[data-entry="speed-1.5"]').tap();
            await expect(menu).toHaveCount(0);
        }
        await expect.poll(() => video(page).evaluate(v => (v as HTMLVideoElement).playbackRate)).toBe(1.5);
    });
});

test.describe('video, the whole screen', () => {
    test('full screen by a tap: the player fills the screen, its controls fit; back by a tap', async ({ page }) => {
        await openVideo(page);
        const host = page.locator(player);
        await host.locator('.player-row button[aria-label^="Fullscreen"]').tap();
        await expect(host).toHaveClass(/fullscreen/);
        const box = (await host.boundingBox())!;
        const screen = page.viewportSize()!;
        expect(Math.round(box.width)).toBe(screen.width);
        expect(Math.round(box.height)).toBe(screen.height);
        await host.locator('.player-surface').tap();
        const row = host.locator('.player-row');
        await expect(row).toBeVisible();
        expect((await row.boundingBox())!.x + (await row.boundingBox())!.width).toBeLessThanOrEqual(screen.width + 0.5);
        await expectTapTargets(page, `${player} .player-controls`);
        await host.locator('.player-row button[aria-label^="Exit fullscreen"]').tap();
        await expect(host).not.toHaveClass(/fullscreen/);
        await expect(page.locator('dialog.viewer[open]'), 'the viewer stays').toHaveCount(1);
    });

    test('turning the phone while it plays: it goes on, its controls fit the new screen', async ({ page }) => {
        await openVideo(page);
        await page.locator(`${player} .player-big-play, ${player} .player-row button[aria-label^="Play"]`).first().tap();
        await expect.poll(() => video(page).evaluate(v => (v as HTMLVideoElement).paused)).toBe(false);
        const upright = await turn(page);
        await page.locator(`${player} .player-surface`).tap();
        await expect(page.locator(`${player} .player-row`)).toBeVisible();
        await expectFitsScreen(page);
        const row = (await page.locator(`${player} .player-row`).boundingBox())!;
        expect(row.x + row.width).toBeLessThanOrEqual(upright.height + 0.5);
        expect(await video(page).evaluate(v => (v as HTMLVideoElement).paused), 'still playing').toBe(false);
        await page.setViewportSize(upright);
    });
});

test.describe('audio', () => {
    test('a song plays by a tap on its bar, which fits the screen above the bottom edge', async ({ page }) => {
        await openFolder(page, 'Music');
        await item(page, 'Music/Evening walk.mp3').locator('.item-link').first().tap();
        const audio = page.locator('dialog.viewer[open] audio');
        // WebKit on Windows has no MP3 (an iPhone has): the app says it can't play it.
        const message = page.locator('dialog.viewer[open] .viewer-message');
        await expect.poll(async () => await message.isVisible()
            || await page.evaluate(() => (document.querySelector('dialog.viewer[open] audio') as HTMLAudioElement | null)?.readyState! >= 1)).toBe(true);
        test.skip(await message.isVisible(), 'this browser plays no MP3');
        await expectFitsScreen(page);
        const box = (await audio.boundingBox())!;
        expect(box.y + box.height, 'above the bottom of the screen').toBeLessThanOrEqual(page.viewportSize()!.height - 8);
        // It plays by itself (autoplay), where the browser lets it; a tap on the bar's play button pauses or plays it.
        const paused = () => audio.evaluate(a => (a as HTMLAudioElement).paused);
        // Autoplay starts when it may: settled before the tap, else the tap and it cross.
        await expect.poll(paused, { timeout: 2000 }).toBe(false).catch(() => undefined);
        const before = await paused();
        await page.touchscreen.tap(box.x + 20, box.y + box.height / 2);
        await expect.poll(paused).toBe(!before);
    });
});
