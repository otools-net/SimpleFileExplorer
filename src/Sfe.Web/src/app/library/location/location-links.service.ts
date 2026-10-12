import { computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { baseName, parentPath } from '../../core/paths';
import { ListingStore } from '../listing.store';

export interface FolderLink {
    path: string | null;
    title: string;
}

export interface Crumb {
    name: string;
    path: string;
}

/**
 * Where the listing is, for the location header and the compact bar that replaces it once scrolled away
 * (`LocationStripComponent`): breadcrumbs, nearby folders, and whether the header is out of view.
 */
@Injectable({ providedIn: 'root' })
export class LocationLinks {
    private readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);

    /** The location header has scrolled under the top bar: set by `LocationComponent`. */
    readonly headHidden = signal(false);

    readonly crumbs = computed((): Crumb[] => {
        const path = this.store.route().path;
        const segments = path ? path.split('/') : [];
        return [
            { name: this.config.config()?.rootName ?? '', path: '' },
            ...segments.map((segment, i) => ({ name: segment, path: segments.slice(0, i + 1).join('/') })),
        ];
    });

    /** Parent, previous and next folder links, while browsing below the root; a null path is disabled. */
    readonly folderNav = computed((): FolderLink[] | null => {
        const { path, q } = this.store.route();
        if (q || !path)
            return null;
        const parent = parentPath(path);
        const siblings = this.store.siblings();
        const where = siblings?.position ? ` (${siblings.position} of ${siblings.count})` : '';
        return [
            { path: parent, title: `Parent folder: ${baseName(parent) || this.config.config()?.rootName} (Backspace)` },
            {
                path: siblings?.previous?.path ?? null,
                title: siblings?.previous ? `Previous folder: ${siblings.previous.name}${where}` : siblings ? 'First folder' : 'Previous folder',
            },
            {
                path: siblings?.next?.path ?? null,
                title: siblings?.next ? `Next folder: ${siblings.next.name}${where}` : siblings ? 'Last folder' : 'Next folder',
            },
        ];
    });
}
