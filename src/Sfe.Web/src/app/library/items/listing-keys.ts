/*
 * Moving the keyboard focus through the listed items, as in an OS file browser: left and right to the item before and
 * after, up and down to the nearest item of the row above or below (in a grid of tiles of any width), Home and End.
 */

export const MOVE_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End']);

/** The listed items (folders, tiles, rows) in page order. */
export function listedItems(): HTMLElement[] {
    return [...document.querySelectorAll<HTMLElement>('sfe-listing [data-path]')];
}

/** The listed item holding the focus, if any. */
export function focusedListItem(): HTMLElement | null {
    return (document.activeElement as Element | null)?.closest?.<HTMLElement>('sfe-listing [data-path]') ?? null;
}

/** Puts the focus on the item's link (its name or picture), scrolling it into view. */
export function focusItem(element: HTMLElement): void {
    element.querySelector<HTMLElement>('a')?.focus();
}

/** The item `key` moves to from `current`; null at an end. */
export function neighbour(items: HTMLElement[], current: HTMLElement, key: string): HTMLElement | null {
    const index = items.indexOf(current);
    if (index < 0)
        return null;
    switch (key) {
        case 'Home': return items[0] ?? null;
        case 'End': return items.at(-1) ?? null;
        case 'ArrowLeft': return items[index - 1] ?? null;
        case 'ArrowRight': return items[index + 1] ?? null;
    }
    const down = key === 'ArrowDown';
    const from = current.getBoundingClientRect();
    // Without a layout (nothing drawn yet) the items read as one column.
    if (!from.width && !from.height)
        return items[index + (down ? 1 : -1)] ?? null;

    const middle = from.top + from.height / 2;
    const centre = from.left + from.width / 2;
    const candidates = items
        .map(element => ({ element, box: element.getBoundingClientRect() }))
        .filter(({ box }) => down ? box.top >= middle : box.bottom <= middle);
    if (!candidates.length)
        return null;
    // The nearest row in that direction, then the item of that row nearest to the current one's centre.
    const edge = down ? Math.min(...candidates.map(c => c.box.top)) : Math.max(...candidates.map(c => c.box.bottom));
    const row = candidates.filter(({ box }) => Math.abs((down ? box.top : box.bottom) - edge) < Math.max(4, from.height / 2));
    row.sort((a, b) => Math.abs(a.box.left + a.box.width / 2 - centre) - Math.abs(b.box.left + b.box.width / 2 - centre));
    return row[0].element;
}
