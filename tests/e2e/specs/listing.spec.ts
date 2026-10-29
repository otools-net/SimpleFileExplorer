// The listing on a phone: what is on screen stays where it is while it settles (mobile e2e plan, Phase 2, listing).
import { expect, item, openFolder, test } from '../fixtures/test';
import { watchBoxes } from '../fixtures/steady';

test.describe('listing', () => {
    // Tiles take their preview's shape once it loads (the placeholder's is a guess): rows above the screen change
    // height. The tile under the finger must not move meanwhile, or a tap lands on another item (seen on WebKit in
    // reported.spec.ts: the ⋮ tapped, the click on something else, no menu).
    test('a tile on screen stays put while the previews around it load', async ({ page }) => {
        await openFolder(page, 'Many photos');
        const tile = item(page, 'Many photos/Photo 040.jpg');
        await tile.scrollIntoViewIfNeeded();
        await expect(tile).toBeVisible();
        const watch = await watchBoxes(page, { 'Photo 040': `sfe-listing [data-path="Many photos/Photo 040.jpg"]` });
        await watch.expectSteady(3000, 2);
    });

    // What comes in above the listing after it is shown (the scan panel's last result, read after the listing) pushes
    // every item down: a tap aimed at one lands on another.
    test('the listing does not move once shown', async ({ page }) => {
        await openFolder(page, 'Photos/2025 Mountains');
        const watch = await watchBoxes(page, { 'first tile': 'sfe-listing [data-path]' });
        await watch.expectSteady(2500, 2);
    });
});
