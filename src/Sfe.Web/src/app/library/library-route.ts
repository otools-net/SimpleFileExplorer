import { ParamMap, Params } from '@angular/router';
import { SortField } from './library.models';

/**
 * What the listing page shows; the URL's query string is the source of truth (`?c=&path=&q=&all=1&sort=&desc=1`).
 */
export interface LibraryRoute {
    /** The connection whose files are listed (`path` relative to its root); null or absent for the library. */
    c?: number | null;
    path: string;
    q: string;
    /**
     * The search runs everywhere: the library and every connection the session uses, each apart (`c` and `path` are
     * kept, for going back to searching there). Only with `q`.
     */
    all?: boolean;
    /** '' = the default, see `currentSort`. */
    sort: SortField | '';
    desc: boolean;
}

const SORTS = new Set<string>(['name', 'modified', 'size', 'type', 'path', 'duration', 'resolution']);

/** The sorts only a search has: they read the index (a folder is listed from disk) or group by folder. */
export const SEARCH_SORTS = new Set<SortField>(['path', 'duration', 'resolution']);

export function readRoute(params: ParamMap): LibraryRoute {
    const sort = params.get('sort') ?? '';
    const connection = Number(params.get('c'));
    return {
        c: Number.isInteger(connection) && connection > 0 ? connection : null,
        path: params.get('path') || '',
        q: params.get('q') || '',
        ...(params.get('all') === '1' ? { all: true } : {}),
        sort: SORTS.has(sort) ? (sort as SortField) : '',
        desc: params.get('desc') === '1',
    };
}

/** The query parameters of a route; the empty ones are left out of the URL (`c` only for a connection). */
export function routeParams(route: LibraryRoute): Params {
    return {
        ...(route.c ? { c: route.c } : {}),
        path: route.path || null,
        q: route.q || null,
        ...(route.all && route.q ? { all: '1' } : {}),
        sort: route.sort || null,
        desc: route.desc ? '1' : null,
    };
}

/** The default sort: searches group results by folder (path order), a folder is listed by name. */
export function defaultSort(route: LibraryRoute): SortField {
    return route.q ? 'path' : 'name';
}

/** The sort in effect. A folder has no path, duration or resolution order: it lists by name instead. */
export function currentSort(route: LibraryRoute): SortField {
    const sort = route.sort || defaultSort(route);
    return SEARCH_SORTS.has(sort) && !route.q ? 'name' : sort;
}

/** Where "back" returns to: the scroll position and how many items were loaded. */
export interface RestoreState {
    scrollY?: number;
    loaded?: number;
    /** Path of the item at the top of the screen, and its distance from the top (see `currentPosition`). */
    anchor?: string;
    anchorTop?: number;
}

/** The history state of the page as it was opened, before the router replaces it (see main.ts). */
export const initialHistoryState: RestoreState = readRestoreState(history.state);

export function readRestoreState(state: unknown): RestoreState {
    if (!state || typeof state !== 'object') return {};
    const { scrollY, loaded, anchor, anchorTop } = state as Record<string, unknown>;
    return {
        scrollY: typeof scrollY === 'number' ? scrollY : undefined,
        loaded: typeof loaded === 'number' ? loaded : undefined,
        anchor: typeof anchor === 'string' ? anchor : undefined,
        anchorTop: typeof anchorTop === 'number' ? anchorTop : undefined,
    };
}

/**
 * The app's address of a folder (`Router.navigateByUrl`), the library's or a connection's (`c`): an Open kept with a
 * notification after a reload.
 */
export function folderLink(path: string, c: number | null = null): string {
    const params = new URLSearchParams();
    if (c) params.set('c', String(c));
    if (path) params.set('path', path);
    const query = params.toString();
    return query ? `/?${query}` : '/';
}
