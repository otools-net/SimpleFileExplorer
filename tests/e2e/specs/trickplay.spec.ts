// Holding a finger on a video's tile plays its preview (trickplay, hover-preview.md), on a phone. `@ffmpeg`: the
// server makes the timelapses' trickplay only with ffmpeg (paths.ts `ffmpeg`, set by SFE_E2E_FFMPEG / SFE_TEST_FFMPEG
// or found on the PATH); without it, skipped.
import { expect, item, openFolder, test } from '../fixtures/test';
import { pressLikeIos } from '../fixtures/touch';

const video = 'Videos/Timelapses/Clouds.webm';

test.describe('@ffmpeg hold-to-preview', () => {
    test.skip(!process.env.SFE_E2E_FFMPEG, 'no ffmpeg: set SFE_E2E_FFMPEG (or SFE_TEST_FFMPEG), or put it on the PATH');

    test('holding a video tile plays its preview; lifting the finger opens nothing and selects nothing', async ({ page }) => {
        // Asking for it queues it (a folder of the trickplay's): ready in seconds for a short sample.
        await expect.poll(async () => (await page.request.get(`api/trickplay/files/${video}`, { headers: { 'X-SFE-Request': '1' } })).status(),
            { timeout: 120_000, intervals: [1000] }).toBe(200);

        await openFolder(page, 'Videos/Timelapses');
        const tile = item(page, video);
        const lift = await pressLikeIos(page, tile.locator('.preview'));
        await expect(tile.locator('canvas.trickplay-hover')).toBeVisible({ timeout: 5000 });
        // Held past the app's own long press (650 ms): the preview keeps the finger, no selection.
        await page.waitForTimeout(900);
        await lift();
        await expect(tile.locator('canvas.trickplay-hover')).toHaveCount(0);
        await expect(page.locator('dialog.viewer[open]'), 'the lift opened nothing').toHaveCount(0);
        await expect(tile).not.toHaveClass(/selected/);
    });
});
