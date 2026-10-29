// "The bar jumps": a test over time. The boxes of the watched elements are sampled on every animation frame in the
// page; a box that moves or resizes by more than `tolerance` px while shown fails, with when and how far. A check
// once before and once after an action can't see a bar that moves for 300 ms and comes back.
import { expect, type Page } from '@playwright/test';

type Box = { x: number; y: number; width: number; height: number };
type Sample = { t: number; boxes: Record<string, Box | null> };

declare global {
    interface Window { __sfeWatch?: { samples: Sample[]; stop: boolean } }
}

export interface BoxWatch {
    /** Lets the page run for `ms` more (animations end), stops sampling and fails if a watched box moved. */
    expectSteady(ms?: number, tolerance?: number): Promise<void>;
}

/** Starts sampling the first element of each selector (`{ name: selector }`) on every frame. */
export async function watchBoxes(page: Page, selectors: Record<string, string>): Promise<BoxWatch> {
    await page.evaluate(selectors => {
        const watch = { samples: [] as Sample[], stop: false };
        window.__sfeWatch = watch;
        const frame = (t: number) => {
            const boxes: Record<string, Box | null> = {};
            for (const [name, selector] of Object.entries(selectors)) {
                const element = document.querySelector(selector);
                const shown = element && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
                const r = shown ? element.getBoundingClientRect() : null;
                boxes[name] = r ? { x: r.x, y: r.y, width: r.width, height: r.height } : null;
            }
            watch.samples.push({ t, boxes });
            if (!watch.stop)
                requestAnimationFrame(frame);
        };
        requestAnimationFrame(frame);
    }, selectors);

    return {
        async expectSteady(ms = 600, tolerance = 1) {
            await page.waitForTimeout(ms);
            const samples = await page.evaluate(() => {
                const watch = window.__sfeWatch!;
                watch.stop = true;
                return watch.samples;
            });
            const moves: string[] = [];
            for (const name of Object.keys(selectors)) {
                let first: { t: number; box: Box } | null = null;
                let worst = { distance: 0, t: 0, box: null as Box | null };
                for (const sample of samples) {
                    const box = sample.boxes[name];
                    if (!box)
                        continue;
                    first ??= { t: sample.t, box };
                    const distance = Math.max(Math.abs(box.x - first.box.x), Math.abs(box.y - first.box.y),
                        Math.abs(box.width - first.box.width), Math.abs(box.height - first.box.height));
                    if (distance > worst.distance)
                        worst = { distance, t: sample.t, box };
                }
                if (first && worst.box && worst.distance > tolerance)
                    moves.push(`${name} moved ${worst.distance.toFixed(1)} px ${Math.round(worst.t - samples[0].t)} ms in: `
                        + `${fmt(first.box)} → ${fmt(worst.box)}`);
            }
            expect(moves, `boxes steady over ${samples.length} frames`).toEqual([]);
        },
    };
}

const fmt = (b: Box) => `(${b.x.toFixed(0)},${b.y.toFixed(0)} ${b.width.toFixed(0)}×${b.height.toFixed(0)})`;
