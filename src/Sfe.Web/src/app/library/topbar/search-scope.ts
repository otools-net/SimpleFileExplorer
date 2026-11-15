import { MenuEntry } from '../../menus/context-menu';
import { LibraryRoute } from '../library-route';

/** Where a search can run, as the search box's scope menu offers it. */
export interface SearchScopes {
    /** The name of the location listed: the library's, or the connection's. */
    location: string;
    /** Other locations (connections) the session uses: "Everywhere" is offered. */
    everywhere: boolean;
}

/** The scope's button text: the folder, the location, or everywhere. */
export function scopeText(route: LibraryRoute, scopes: SearchScopes): string {
    if (route.q && route.all) return 'everywhere';
    if (route.path) return `in ${route.path.split('/').pop()}`;
    return route.c || (route.q && scopes.everywhere) ? `in ${scopes.location}` : '';
}

/** The scope's button hint. */
export function scopeTitle(route: LibraryRoute, scopes: SearchScopes): string {
    if (!route.q) return route.path ? `Searches will look inside “${route.path}”.` : '';
    if (route.all) return 'Searching the library and every connection, each apart. Click to choose where to search.';
    const where = route.path ? `“${route.path}”` : scopes.location;
    return `Searching ${where}. Click to choose where to search.`;
}

/**
 * The scope menu of a search: this folder, the whole location, everywhere (when the session uses connections). Each
 * entry runs `go` with the route's change.
 */
export function scopeEntries(
    route: LibraryRoute,
    scopes: SearchScopes,
    go: (change: Partial<LibraryRoute>) => void,
): MenuEntry[] {
    const all = Boolean(route.all);
    const entries: MenuEntry[] = [];
    if (route.path)
        entries.push({
            id: 'folder',
            label: `In “${route.path.split('/').pop()}”`,
            icon: 'folder',
            title: `Search “${route.path}” and the folders inside it`,
            checked: !all,
            run: () => go({ all: false }),
        });
    entries.push({
        id: 'location',
        label: `All of ${scopes.location}`,
        icon: route.c ? 'globe' : 'space',
        title: `Search everything in ${scopes.location}`,
        checked: !all && !route.path,
        run: () => go({ path: '', all: false }),
    });
    if (scopes.everywhere)
        entries.push({
            id: 'everywhere',
            label: 'Everywhere',
            icon: 'search',
            title: 'Search the library and every connection you use; the results come by location',
            checked: all,
            run: () => go({ all: true }),
        });
    return entries;
}
