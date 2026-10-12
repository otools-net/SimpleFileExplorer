import { countAbove, currentPosition, restorePosition } from './listing-position';

/** Every item is this tall, one under the other, the folders first (jsdom lays nothing out). */
const ITEM_HEIGHT = 300;

describe('listing positions', () => {
    let scrollY = 0;
    let scrollTo: ReturnType<typeof vi.spyOn>;

    beforeEach(() => {
        document.body.innerHTML = `
            <sfe-listing><div class="content">
                <div class="folders"><sfe-folder-item data-path="A"></sfe-folder-item><sfe-folder-item data-path="B"></sfe-folder-item></div>
                <div class="files">${Array.from({ length: 8 }, (_, i) => `<article data-path="B/f${i}.jpg"></article>`).join('')}</div>
            </div></sfe-listing>`;
        scrollY = 0;
        Object.defineProperty(window, 'scrollY', { get: () => scrollY, configurable: true });
        scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(((_x: number, y: number) => { scrollY = y; }) as typeof window.scrollTo);
        vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
            const items = [...document.querySelectorAll('[data-path]')];
            const top = items.indexOf(this) * ITEM_HEIGHT - scrollY;
            return { top, bottom: top + ITEM_HEIGHT, left: 0, right: 100, width: 100, height: ITEM_HEIGHT, x: 0, y: top } as DOMRect;
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
        Object.defineProperty(window, 'scrollY', { value: 0, configurable: true });
        document.body.innerHTML = '';
    });

    it('counts the items starting above a line, or entirely above it', () => {
        const files = document.querySelector('.files')!;
        scrollY = 600;  // the files start at 0 px on screen

        expect(countAbove(files, 450)).toBe(2);
        expect(countAbove(files, 450, 'bottom')).toBe(1);
        expect(countAbove(files, 0)).toBe(0);
        expect(countAbove(files, 10_000)).toBe(8);
    });

    it('remembers the item at the top of the screen and how far it is from the top, a file or a folder', () => {
        scrollY = 1000;     // the second file (900 px down the page) shows from -100 px
        expect(currentPosition()).toEqual({ scrollY: 1000, anchor: 'B/f1.jpg', anchorTop: -100 });

        scrollY = 100;
        expect(currentPosition()).toEqual({ scrollY: 100, anchor: 'A', anchorTop: -100 });
    });

    it('scrolls the remembered item back where it was, whatever the scroll position was', () => {
        // After a reload the items above may have other sizes: wherever the item is now (1200 px), it goes 80 px up.
        restorePosition({ scrollY: 5000, anchor: 'B/f2.jpg', anchorTop: -80 });

        expect(scrollTo).toHaveBeenCalledWith(0, 1280);
    });

    it('falls back on the scroll position when the item is not listed any more, and goes to the top by default', () => {
        restorePosition({ scrollY: 700, anchor: 'gone.jpg', anchorTop: 0 });
        expect(scrollTo).toHaveBeenLastCalledWith(0, 700);

        restorePosition({});
        expect(scrollTo).toHaveBeenLastCalledWith(0, 0);
    });
});
