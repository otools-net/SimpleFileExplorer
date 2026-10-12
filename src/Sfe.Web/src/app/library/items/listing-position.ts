import { RestoreState } from '../library-route';

/*
 * Positions in the listing. Its folders and files are laid out in reading order, so the tops and bottoms of the
 * items never decrease: binary searches read a few of thousands of positions.
 */

/** The containers of the listed items in `root`: folders, then files. */
export function itemContainers(root: ParentNode = document): Element[] {
    return [...root.querySelectorAll('.content > .folders, .content > .files')];
}

/**
 * The number of children of `container` above `y` (viewport coordinates): starting above it (`top`), or entirely
 * above it (`bottom`).
 */
export function countAbove(container: Element, y: number, edge: 'top' | 'bottom' = 'top'): number {
    const items = container.children;
    let low = 0;
    let high = items.length;
    while (low < high) {
        const middle = (low + high) >> 1;
        const value = items[middle].getBoundingClientRect()[edge];
        if (edge === 'top' ? value < y : value <= y)
            low = middle + 1;
        else
            high = middle;
    }
    return low;
}

/**
 * Where the listing is scrolled, to come back to it: the item at the top of the screen and how far from the top it
 * is, besides `scrollY`. After a reload the previews above it don't have their size yet, so `scrollY` alone would
 * land elsewhere.
 */
export function currentPosition(): Pick<RestoreState, 'scrollY' | 'anchor' | 'anchorTop'> {
    for (const container of itemContainers()) {
        const index = countAbove(container, 0, 'bottom');
        const item = container.children[index];
        const anchor = item?.getAttribute('data-path');
        if (item && anchor !== null)
            return { scrollY: window.scrollY, anchor, anchorTop: item.getBoundingClientRect().top };
    }
    return { scrollY: window.scrollY, anchor: undefined, anchorTop: undefined };
}

/** How long a restored item is held in place while the previews around it take their sizes (ms). */
const HOLD_MS = 2500;
/** What the user does to scroll: it ends the hold. */
const USER_SCROLL = ['wheel', 'touchstart', 'keydown', 'pointerdown'];

/**
 * Scrolls back to a position of `currentPosition`: its item where it was, else `scrollY`. The previews above and
 * around the item then load and change the height of their rows, which moves it: it is kept in place for a moment,
 * until the user scrolls.
 */
export function restorePosition(restore: RestoreState): void {
    const anchor = restore.anchor === undefined ? undefined
        : [...document.querySelectorAll('sfe-listing [data-path]')].find(element => element.getAttribute('data-path') === restore.anchor);
    if (!anchor) {
        window.scrollTo(0, restore.scrollY ?? 0);
        return;
    }

    const place = () => window.scrollTo(0, window.scrollY + anchor.getBoundingClientRect().top - (restore.anchorTop ?? 0));
    let frame = 0;
    const hold = () => {
        if (!anchor.isConnected)
            return stop();
        place();
        frame = requestAnimationFrame(hold);
    };
    const stop = () => {
        cancelAnimationFrame(frame);
        clearTimeout(timer);
        for (const type of USER_SCROLL)
            window.removeEventListener(type, stop, true);
    };
    const timer = setTimeout(stop, HOLD_MS);
    for (const type of USER_SCROLL)
        window.addEventListener(type, stop, { capture: true, passive: true });
    place();
    frame = requestAnimationFrame(hold);
}
