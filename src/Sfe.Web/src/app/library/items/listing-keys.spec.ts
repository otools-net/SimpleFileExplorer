import { neighbour } from './listing-keys';

/** Items laid out at the given boxes (left, top, width, height). */
function laidOut(...boxes: [number, number, number, number][]): HTMLElement[] {
    return boxes.map(([left, top, width, height]) => {
        const element = document.createElement('div');
        element.getBoundingClientRect = () => ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON: () => ({}) });
        return element;
    });
}

describe('neighbour', () => {
    // Two rows of tiles of different widths, then a short last row.
    const items = laidOut(
        [0, 0, 100, 80], [110, 0, 200, 80], [320, 0, 100, 80],
        [0, 90, 250, 80], [260, 90, 160, 80],
        [0, 180, 100, 80],
    );

    it('steps along the page with left and right, and to the ends with Home and End', () => {
        expect(neighbour(items, items[2], 'ArrowRight')).toBe(items[3]);
        expect(neighbour(items, items[3], 'ArrowLeft')).toBe(items[2]);
        expect(neighbour(items, items[0], 'ArrowLeft')).toBeNull();
        expect(neighbour(items, items[3], 'Home')).toBe(items[0]);
        expect(neighbour(items, items[3], 'End')).toBe(items[5]);
    });

    it('goes up and down to the item of the next row nearest to the current one', () => {
        expect(neighbour(items, items[1], 'ArrowDown')).toBe(items[3]);
        expect(neighbour(items, items[2], 'ArrowDown')).toBe(items[4]);
        expect(neighbour(items, items[4], 'ArrowDown')).toBe(items[5]);
        expect(neighbour(items, items[4], 'ArrowUp')).toBe(items[2]);
        expect(neighbour(items, items[5], 'ArrowDown')).toBeNull();
        expect(neighbour(items, items[0], 'ArrowUp')).toBeNull();
    });

    it('takes the items as one column without a layout', () => {
        const flat = laidOut([0, 0, 0, 0], [0, 0, 0, 0]);

        expect(neighbour(flat, flat[0], 'ArrowDown')).toBe(flat[1]);
        expect(neighbour(flat, flat[1], 'ArrowUp')).toBe(flat[0]);
    });
});
