import { computed, effect, inject, Injectable, untracked } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { UsableConnections } from '../../core/usable-connections';
import { MenuEntry, MenuTarget } from '../../menus/context-menu';
import { ListingStore } from '../listing.store';
import { scopeEntries, SearchScopes } from './search-scope';

/**
 * Where a search can run (this folder, the whole location, everywhere), as a menu: the search box's chip opens it,
 * and the search's menu (the location's ⋯ on phones, where the chip doesn't fit) has it as a submenu.
 */
@Injectable({ providedIn: 'root' })
export class SearchScopeMenu {
    private readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);
    private readonly connections = inject(UsableConnections);

    /** The location listed and whether other ones can be searched with it. */
    readonly scopes = computed<SearchScopes>(() => {
        const c = this.store.route().c;
        const everywhere = this.connections.enabled() && this.connections.list().length > 0;
        return { location: c ? this.connections.name(c) : this.config.libraryName(), everywhere };
    });

    constructor() {
        // Read once the configuration says the session uses connections (it may come after this service).
        effect(() => {
            if (this.connections.enabled()) untracked(() => void this.connections.load());
        });
    }

    /** The menu of the chip. */
    target(): MenuTarget {
        return { kind: 'overflow', key: 'searchScope', name: $localize`Search in`, entries: () => this.entries() };
    }

    /** "Search in" for the search's menu; null when there is only one place to search. */
    entry(): MenuEntry | null {
        const entries = this.entries();
        if (entries.length < 2) return null;
        return {
            id: 'searchIn',
            label: $localize`Search in`,
            icon: 'search',
            title: $localize`Choose where to search: this folder, all of this place, or everywhere.`,
            submenu: () => this.entries(),
        };
    }

    private entries(): MenuEntry[] {
        return scopeEntries(this.store.route(), this.scopes(), (change) => this.store.navigate(change));
    }
}
