import { ParamMap, Params } from '@angular/router';
import { SortField } from '../core/models';

/** What the listing page shows; the URL's query string is the source of truth (`?path=&q=&sort=&desc=1`). */
export interface LibraryRoute {
    path: string;
    q: string;
    /** '' = the default, see `currentSort`. */
    sort: SortField | '';
    desc: boolean;
}

const SORTS = new Set<string>(['name', 'modified', 'size', 'type', 'path', 'duration', 'resolution']);

/** The sorts only a search has: they read the index (a folder is listed from disk) or group by folder. */
export const SEARCH_SORTS = new Set<SortField>(['path', 'duration', 'resolution']);

export function readRoute(params: ParamMap): LibraryRoute {
    const sort = params.get('sort') ?? '';
    return {
        path: params.get('path') || '',
        q: params.get('q') || '',
        sort: SORTS.has(sort) ? sort as SortField : '',
        desc: params.get('desc') === '1',
    };
}

/** The query parameters of a route; the empty ones are left out of the URL. */
export function routeParams(route: LibraryRoute): Params {
    return {
        path: route.path || null,
        q: route.q || null,
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
    if (!state || typeof state !== 'object')
        return {};
    const { scrollY, loaded, anchor, anchorTop } = state as Record<string, unknown>;
    return {
        scrollY: typeof scrollY === 'number' ? scrollY : undefined,
        loaded: typeof loaded === 'number' ? loaded : undefined,
        anchor: typeof anchor === 'string' ? anchor : undefined,
        anchorTop: typeof anchorTop === 'number' ? anchorTop : undefined,
    };
}
