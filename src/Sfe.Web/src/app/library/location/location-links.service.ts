import { computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { baseName, parentPath } from '../../core/paths';
import { FolderSiblings } from '../library.models';
import { ListingStore } from '../listing.store';

export interface FolderLink {
    path: string | null;
    title: string;
}

export interface Crumb {
    name: string;
    path: string;
    /** A folder the session only passes through (`ListingStore.passesThrough`): shown, not a link. */
    through: boolean;
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
        ].map((crumb) => ({ ...crumb, through: this.store.passesThrough(crumb.path) }));
    });

    /** The hint of a folder the session only passes through. */
    readonly throughTitle = $localize`You can't open this folder: it only leads to the folders below it that you can \
see.`;

    /** Parent, previous and next folder links, while browsing below the root; a null path is disabled. */
    readonly folderNav = computed((): FolderLink[] | null => {
        const { path, q } = this.store.route();
        if (q || !path) return null;
        const parent = parentPath(path);
        const siblings = this.store.siblings();
        const parentName = baseName(parent) || this.config.config()?.rootName;
        return [
            this.store.passesThrough(parent)
                ? { path: null, title: $localize`This is the top of the folders you can see.` }
                : { path: parent, title: $localize`Go up to “${parentName}:folder:” (Backspace).` },
            {
                path: siblings?.previous?.path ?? null,
                title: siblings?.previous
                    ? siblingTitle($localize`Previous folder: “${siblings.previous.name}:folder:”.`, siblings)
                    : siblings
                      ? $localize`This is the first folder.`
                      : $localize`Previous folder`,
            },
            {
                path: siblings?.next?.path ?? null,
                title: siblings?.next
                    ? siblingTitle($localize`Next folder: “${siblings.next.name}:folder:”.`, siblings)
                    : siblings
                      ? $localize`This is the last folder.`
                      : $localize`Next folder`,
            },
        ];
    });
}

/** A previous or next folder's hint, with where the folder browsed sits among its siblings when known. */
function siblingTitle(title: string, siblings: FolderSiblings): string {
    if (!siblings.position) return title;
    const { position, count } = siblings;
    return $localize`${title}:hint: This is folder ${position}:position: of ${count}:count:.`;
}
