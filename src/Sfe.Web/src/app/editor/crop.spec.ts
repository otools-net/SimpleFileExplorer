import { ASPECTS, aspectRatio, clampRect, dragRect, fitAspect, flipAspect, fullRect, largestAround, rectFromPoints, sameRect } from './crop';

describe('crop geometry', () => {
    const bounds = { width: 4000, height: 3000 };

    it('reads the aspect ratio choices', () => {
        expect(aspectRatio('free', bounds)).toBeNull();
        expect(aspectRatio('original', bounds)).toBeCloseTo(4 / 3);
        expect(aspectRatio('16:9', bounds)).toBeCloseTo(16 / 9);
        expect(ASPECTS.slice(1).every(([id]) => aspectRatio(id, bounds) !== null)).toBe(true);
    });

    it('turns a ratio between landscape and portrait', () => {
        expect(flipAspect('16:9')).toBe('9:16');
        expect(flipAspect('2:3')).toBe('3:2');
        expect(flipAspect('1:1')).toBeNull();
        expect(flipAspect('free')).toBeNull();
        expect(flipAspect('original')).toBeNull();
        // Every ratio listed has its other way round listed too.
        const ids = ASPECTS.map(([id]) => id);
        expect(ids.map(flipAspect).filter(id => id !== null).every(id => ids.includes(id!))).toBe(true);
    });

    it('picks the largest frame of a ratio around the current centre', () => {
        expect(largestAround({ x: 1800, y: 1300, width: 400, height: 400 }, 9 / 16, bounds)).toEqual({ x: 1156, y: 0, width: 1688, height: 3000 });
        expect(largestAround({ x: 3800, y: 100, width: 100, height: 100 }, 1, bounds)).toEqual({ x: 1000, y: 0, width: 3000, height: 3000 });
        expect(largestAround({ x: 10, y: 20, width: 30, height: 40 }, null, bounds)).toEqual({ x: 10, y: 20, width: 30, height: 40 });
    });

    it('keeps a frame in whole pixels inside the picture', () => {
        expect(clampRect({ x: -10.4, y: 3000, width: 100.6, height: 0 }, bounds)).toEqual({ x: 0, y: 2999, width: 101, height: 1 });
        expect(clampRect({ x: 3500, y: 0, width: 9000, height: 100 }, bounds)).toEqual({ x: 0, y: 0, width: 4000, height: 100 });
        expect(sameRect(fullRect(bounds), { x: 0, y: 0, width: 4000, height: 3000 })).toBe(true);
    });

    it('fits a ratio around the centre of the frame', () => {
        expect(fitAspect(fullRect(bounds), 1, bounds)).toEqual({ x: 500, y: 0, width: 3000, height: 3000 });
        expect(fitAspect(fullRect(bounds), 16 / 9, bounds)).toEqual({ x: 0, y: 375, width: 4000, height: 2250 });
        expect(fitAspect({ x: 10, y: 20, width: 30, height: 40 }, null, bounds)).toEqual({ x: 10, y: 20, width: 30, height: 40 });
    });

    describe('dragging', () => {
        const frame = { x: 1000, y: 1000, width: 1000, height: 500 };

        it('moves the frame, stopping at the edges', () => {
            expect(dragRect(frame, 'move', 200, -100, bounds, null)).toEqual({ x: 1200, y: 900, width: 1000, height: 500 });
            expect(dragRect(frame, 'move', 9000, 9000, bounds, null)).toEqual({ x: 3000, y: 2500, width: 1000, height: 500 });
        });

        it('moves a side or a corner, the opposite one staying', () => {
            expect(dragRect(frame, 'w', -300, 50, bounds, null)).toEqual({ x: 700, y: 1000, width: 1300, height: 500 });
            expect(dragRect(frame, 'se', 100, 200, bounds, null)).toEqual({ x: 1000, y: 1000, width: 1100, height: 700 });
            expect(dragRect(frame, 'nw', -5000, -5000, bounds, null)).toEqual({ x: 0, y: 0, width: 2000, height: 1500 });
        });

        it('never goes below one pixel nor through the opposite side', () => {
            expect(dragRect(frame, 'e', -5000, 0, bounds, null)).toEqual({ x: 1000, y: 1000, width: 1, height: 500 });
            expect(dragRect(frame, 'n', 0, 5000, bounds, null)).toEqual({ x: 1000, y: 1499, width: 1000, height: 1 });
        });

        it('keeps the ratio, a corner following the side that grew the most', () => {
            expect(dragRect(frame, 'se', 1000, 0, bounds, 2)).toEqual({ x: 1000, y: 1000, width: 2000, height: 1000 });
            expect(dragRect(frame, 'se', 0, 500, bounds, 2)).toEqual({ x: 1000, y: 1000, width: 2000, height: 1000 });
            // A side keeps the frame centred across it.
            expect(dragRect(frame, 's', 0, 500, bounds, 2)).toEqual({ x: 500, y: 1000, width: 2000, height: 1000 });
        });

        it('keeps the ratio at the edges of the picture', () => {
            const result = dragRect(frame, 'se', 9000, 9000, bounds, 2);
            expect(result).toEqual({ x: 1000, y: 1000, width: 3000, height: 1500 });
        });

        it('draws a new frame between two points, whichever way', () => {
            expect(rectFromPoints({ x: 3000, y: 2000 }, { x: 1000, y: 500 }, bounds, null)).toEqual({ x: 1000, y: 500, width: 2000, height: 1500 });
            expect(rectFromPoints({ x: 100, y: 100 }, { x: 700, y: 200 }, bounds, 1)).toEqual({ x: 100, y: 100, width: 600, height: 600 });
        });
    });
});
