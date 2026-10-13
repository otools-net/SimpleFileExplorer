import { Rect, Size } from './edits';

/*
 * The crop tool's geometry, in the picture's pixels: whole numbers, inside the picture, at least one pixel, and at
 * the chosen aspect ratio when there is one.
 */

/** What a drag moves: the whole frame, or one of its sides or corners. */
export type Handle = 'move' | 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw';

/** The handles drawn on the frame, clockwise from the top left corner. */
export const HANDLES: readonly Exclude<Handle, 'move'>[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

/**
 * The aspect ratio choices: [id, label], `free` = none, `original` = the picture's own, then the usual ratios of
 * photos, screens and social posts, landscape before its portrait (`flipAspect` turns one into the other).
 */
export const ASPECTS: readonly [string, string][] = [
    ['free', 'Free'],
    ['original', 'Original'],
    ['1:1', 'Square'],
    ['4:3', '4:3'],
    ['3:4', '3:4'],
    ['3:2', '3:2'],
    ['2:3', '2:3'],
    ['5:4', '5:4'],
    ['4:5', '4:5'],
    ['7:5', '7:5'],
    ['5:7', '5:7'],
    ['16:9', '16:9'],
    ['9:16', '9:16'],
    ['16:10', '16:10'],
    ['10:16', '10:16'],
    ['21:9', '21:9'],
    ['9:21', '9:21'],
];

/** The same ratio the other way round (16:9 ↔ 9:16); null for those without one (free, original, square). */
export function flipAspect(id: string): string | null {
    const match = /^(\d+):(\d+)$/.exec(id);
    return match && match[1] !== match[2] ? `${match[2]}:${match[1]}` : null;
}

/** Width / height of the choice `id` for a picture of `size`; null without a ratio. */
export function aspectRatio(id: string, size: Size): number | null {
    if (id === 'original')
        return size.width / size.height;
    const match = /^(\d+):(\d+)$/.exec(id);
    return match ? Number(match[1]) / Number(match[2]) : null;
}

export function fullRect(size: Size): Rect {
    return { x: 0, y: 0, width: size.width, height: size.height };
}

export function sameRect(a: Rect, b: Rect): boolean {
    return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;
}

/** `rect` in whole pixels, at least 1 × 1, inside `bounds` (moved in first, then made smaller if still too large). */
export function clampRect(rect: Rect, bounds: Size): Rect {
    const width = clamp(Math.round(rect.width), 1, bounds.width);
    const height = clamp(Math.round(rect.height), 1, bounds.height);
    return {
        x: clamp(Math.round(rect.x), 0, bounds.width - width),
        y: clamp(Math.round(rect.y), 0, bounds.height - height),
        width,
        height,
    };
}

/** The largest part of `rect` with the aspect `ratio`, around its center (a new ratio picked for the current frame). */
export function fitAspect(rect: Rect, ratio: number | null, bounds: Size): Rect {
    if (!ratio)
        return clampRect(rect, bounds);
    const width = Math.min(rect.width, rect.height * ratio);
    const height = width / ratio;
    return withRatio({ x: rect.x + (rect.width - width) / 2, y: rect.y + (rect.height - height) / 2, width, height }, ratio, bounds);
}

/**
 * The largest frame of the aspect `ratio` that fits the picture, centred where `rect` is centred (moved in when that
 * would cross an edge): picking a ratio, or turning it between landscape and portrait. Without a ratio, `rect` itself.
 */
export function largestAround(rect: Rect, ratio: number | null, bounds: Size): Rect {
    if (!ratio)
        return clampRect(rect, bounds);
    const largest = fitAspect(fullRect(bounds), ratio, bounds);
    return clampRect({
        ...largest,
        x: rect.x + rect.width / 2 - largest.width / 2,
        y: rect.y + rect.height / 2 - largest.height / 2,
    }, bounds);
}

/**
 * The frame `start` after dragging its `handle` by `dx`, `dy` picture pixels. A side or corner moves while the
 * opposite one stays (a side keeps the frame centred across, with a ratio); the frame stays inside `bounds`, and keeps
 * `ratio` when there is one.
 */
export function dragRect(start: Rect, handle: Handle, dx: number, dy: number, bounds: Size, ratio: number | null): Rect {
    if (handle === 'move')
        return clampRect({ ...start, x: start.x + dx, y: start.y + dy }, bounds);

    const horizontal = handle.includes('w') ? 'w' : handle.includes('e') ? 'e' : null;
    const vertical = handle.includes('n') ? 'n' : handle.includes('s') ? 's' : null;
    let left = start.x;
    let right = start.x + start.width;
    let top = start.y;
    let bottom = start.y + start.height;
    if (horizontal === 'w')
        left = clamp(left + dx, 0, right - 1);
    else if (horizontal === 'e')
        right = clamp(right + dx, left + 1, bounds.width);
    if (vertical === 'n')
        top = clamp(top + dy, 0, bottom - 1);
    else if (vertical === 's')
        bottom = clamp(bottom + dy, top + 1, bounds.height);
    if (!ratio)
        return clampRect({ x: left, y: top, width: right - left, height: bottom - top }, bounds);

    // With a ratio, the side dragged decides; a corner follows the side that grew the most.
    let width = right - left;
    let height = bottom - top;
    if (!vertical || horizontal && width / ratio >= height)
        height = width / ratio;
    else
        width = height * ratio;

    // Room from the side or corner that stays (from the centre, for a side dragged alone).
    const centerX = start.x + start.width / 2;
    const centerY = start.y + start.height / 2;
    const roomX = horizontal === 'w' ? right : horizontal === 'e' ? bounds.width - left : 2 * Math.min(centerX, bounds.width - centerX);
    const roomY = vertical === 'n' ? bottom : vertical === 's' ? bounds.height - top : 2 * Math.min(centerY, bounds.height - centerY);
    const scale = Math.min(1, roomX / width, roomY / height);
    width *= scale;
    height *= scale;

    const x = horizontal === 'w' ? right - width : horizontal === 'e' ? left : centerX - width / 2;
    const y = vertical === 'n' ? bottom - height : vertical === 's' ? top : centerY - height / 2;
    return withRatio({ x, y, width, height }, ratio, bounds);
}

/**
 * The frame dragged out from the point `from` to `to` (a new frame drawn outside the current one), with the corner
 * under the pointer following it.
 */
export function rectFromPoints(from: { x: number; y: number }, to: { x: number; y: number }, bounds: Size, ratio: number | null): Rect {
    const handle = `${to.y < from.y ? 'n' : 's'}${to.x < from.x ? 'w' : 'e'}` as Handle;
    const start = { x: clamp(from.x, 0, bounds.width), y: clamp(from.y, 0, bounds.height), width: 0, height: 0 };
    return dragRect(start, handle, to.x - start.x, to.y - start.y, bounds, ratio);
}

/** Whole pixels inside `bounds`, as close to `ratio` as whole pixels allow. */
function withRatio(rect: Rect, ratio: number, bounds: Size): Rect {
    const width = clamp(Math.round(rect.width), 1, bounds.width);
    const height = clamp(Math.round(width / ratio), 1, bounds.height);
    return clampRect({ ...rect, width: height === Math.round(width / ratio) ? width : Math.round(height * ratio), height }, bounds);
}

function clamp(value: number, min: number, max: number): number {
    return Math.min(Math.max(value, min), Math.max(min, max));
}
