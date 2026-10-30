// The location bar on a phone: agents/docs/features/location-bar.md.
// The compact bar sticks under the top bar once the folder's head scrolls away; up / previous / next by a tap; a long
// path wraps on the screen.
import fs from 'node:fs';
import path from 'node:path';
import { expect, item, openFolder, test } from '../fixtures/test';
import { library } from '../paths';
import { expectFitsScreen, expectNothingCut, expectTapTargets } from '../fixtures/layout';

test.describe('location bar', () => {
    test('scrolled down, the compact bar sticks at the top; its title goes back to the top', async ({ page }) => {
        await openFolder(page, 'Many photos');
        const strip = page.locator('.location-strip-bar');
        await expect(strip).not.toHaveClass(/shown/);
        const top = (await page.locator('.topbar').first().boundingBox())!;

        const at: number[] = [];
        for (const y of [1200, 2400]) {
            await page.evaluate(y => window.scrollTo(0, y), y);
            await expect(strip).toHaveClass(/shown/);
            await page.waitForTimeout(150);
            const box = (await strip.boundingBox())!;
            at.push(Math.round(box.y));
            expect(box.y, 'under the top bar').toBeGreaterThanOrEqual(Math.floor(top.y + top.height) - 1);
        }
        expect(at[1], 'the same place while scrolling').toBe(at[0]);
        await expectTapTargets(page, '.location-strip-bar');

        await page.locator('.location-strip-title').tap();
        await expect.poll(() => page.evaluate(() => scrollY)).toBeLessThan(50);
        await expect(strip).not.toHaveClass(/shown/);
    });

    test('parent, previous and next folder by a tap', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const nav = page.locator('.location-head .folder-nav');
        const go = async (label: string, to: string) => {
            await nav.getByRole('link', { name: label }).tap();
            await expect.poll(() => new URL(page.url()).searchParams.get('path')).toBe(to);
            await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
        };
        await go('Next folder', 'Photos/Family');
        await go('Previous folder', 'Photos/2025 Mountains');
        await go('Previous folder', 'Photos/2024 Beach trip');
        await go('Parent folder', 'Photos');
    });

    test('a long path wraps on the screen, its folders reachable', async ({ page, scratch }) => {
        fs.copyFileSync(path.join(library, scratch, 'A.jpg'), path.join(library, scratch, 'Inside/A.jpg'));
        await openFolder(page, `${scratch}/Inside`);
        const crumbs = page.locator('.location-head nav.crumbs');
        await expect(crumbs).toBeVisible();
        await expectFitsScreen(page);
        await expectNothingCut(page, '.location-head');
        await crumbs.locator('a[data-path="Scratch"]').tap();
        await expect.poll(() => new URL(page.url()).searchParams.get('path')).toBe('Scratch');
        await expect(item(page, scratch)).toBeVisible();
    });
});
