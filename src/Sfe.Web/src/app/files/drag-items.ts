/**
 * What a drag of listed items carries (`DRAG_ITEMS`): their paths and their place, a connection's id or null for the
 * library. Written as `{ connection, paths }`; a plain array of paths (as written before places) is the library's.
 */
export interface DraggedItems {
    connection: number | null;
    paths: string[];
}

/** The data of a drag of `paths` from `connection` (null: the library). */
export function dragItemsData(paths: string[], connection: number | null): string {
    return JSON.stringify({ connection, paths });
}

/** The items a drag carries; no paths for anything else. */
export function parseDragItems(json: string): DraggedItems {
    let value: unknown;
    try {
        value = JSON.parse(json);
    } catch {
        return { connection: null, paths: [] };
    }
    if (Array.isArray(value)) return { connection: null, paths: strings(value) };
    if (!value || typeof value !== 'object') return { connection: null, paths: [] };
    const { connection, paths } = value as { connection?: unknown; paths?: unknown };
    return {
        connection: typeof connection === 'number' ? connection : null,
        paths: Array.isArray(paths) ? strings(paths) : [],
    };
}

function strings(values: unknown[]): string[] {
    return values.filter((path): path is string => typeof path === 'string');
}
