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
    if (route.q && route.all) return $localize`:search scope chip:everywhere`;
    if (route.path) return $localize`:search scope chip:in ${route.path.split('/').pop()}:folder:`;
    return route.c || (route.q && scopes.everywhere) ? $localize`:search scope chip:in ${scopes.location}:place:` : '';
}

/** The scope's button hint. */
export function scopeTitle(route: LibraryRoute, scopes: SearchScopes): string {
    if (!route.q) return route.path ? $localize`Searches will look inside “${route.path}:folder:”.` : '';
    if (route.all)
        return $localize`Searching the library and every connection, each apart. Click to choose where to search.`;
    return route.path
        ? $localize`Searching “${route.path}:folder:”. Click to choose where to search.`
        : $localize`Searching ${scopes.location}:place:. Click to choose where to search.`;
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
            label: $localize`In “${route.path.split('/').pop()}:folder:”`,
            icon: 'folder',
            title: $localize`Search “${route.path}:path:” and the folders inside it.`,
            checked: !all,
            run: () => go({ all: false }),
        });
    entries.push({
        id: 'location',
        label: $localize`All of ${scopes.location}:place:`,
        icon: route.c ? 'globe' : 'space',
        title: $localize`Search everything in ${scopes.location}:place:.`,
        checked: !all && !route.path,
        run: () => go({ path: '', all: false }),
    });
    if (scopes.everywhere)
        entries.push({
            id: 'everywhere',
            label: $localize`Everywhere`,
            icon: 'search',
            title: $localize`Search the library and every connection you use; the results come by place.`,
            checked: all,
            run: () => go({ all: true }),
        });
    return entries;
}
