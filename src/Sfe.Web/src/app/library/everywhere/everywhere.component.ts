import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatSize, joinParts } from '../../core/format';
import { icu } from '../../core/i18n/icu';
import { LibraryItem } from '../../core/models';
import { UsableConnections } from '../../core/usable-connections';
import { ContextMenuDirective, MenuTarget } from '../../menus/context-menu';
import { IconComponent } from '../../shared/icon.component';
import { currentSort, LibraryRoute, routeParams } from '../library-route';
import { ListingStore } from '../listing.store';
import { EverywhereGroup, EverywhereLocation, EverywherePage, EverywhereSearch } from './everywhere-search';

/** Results read at a time per location: a first look at each, more on demand. */
export const EVERYWHERE_PAGE = 20;

const KIND_ICONS: Record<string, string> = { image: 'image', video: 'video', audio: 'audio' };

/**
 * A search everywhere (`?q=&all=1`): the library and each connection the session uses searched apart, in groups
 * headed by the location's name with its count and first results, more on demand. Each group loads, fails and
 * pages alone; a file opens where it is (its folder in its location, the file shown).
 */
@Component({
    selector: 'sfe-everywhere',
    imports: [RouterLink, IconComponent, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'everywhere' },
    templateUrl: './everywhere.component.html',
})
export class EverywhereComponent {
    protected readonly store = inject(ListingStore);
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly connections = inject(UsableConnections);

    protected readonly search = new EverywhereSearch((location, after, signal) => this.fetch(location, after, signal));
    protected readonly formatSize = formatSize;

    constructor() {
        // A new search, sort or visit of the page searches again; leaving it cancels the requests.
        effect(() => {
            this.store.generation();
            const route = this.store.route();
            untracked(() => void this.begin(route));
        });
        inject(DestroyRef).onDestroy(() => this.search.stop());
    }

    protected countText(group: EverywhereGroup): string {
        if (group.status !== 'ready' || group.total === null) return '';
        return group.capped
            ? icu($localize`{total, plural, other {#+ files}}`, { total: group.total })
            : icu($localize`{total, plural, one {# file} other {# files}}`, { total: group.total });
    }

    protected countTitle(group: EverywhereGroup): string {
        if (group.total === null) return '';
        return group.capped
            ? icu($localize`{total, plural, one {At least # file found.} other {At least # files found.}}`, {
                  total: group.total,
              })
            : icu($localize`{total, plural, one {# file found.} other {# files found.}}`, { total: group.total });
    }

    /** Why a connection's results may be incomplete; null when its index is whole. */
    protected coverageText(group: EverywhereGroup): string | null {
        if (group.coverage === 'none')
            return $localize`Not in the index yet: its index is being made, search again later.`;
        if (group.coverage === 'partial')
            return $localize`Its index is being brought up to date: some files may be missing.`;
        return null;
    }

    /** The search of this location alone, in the listing (every sort, the viewer). */
    protected hereParams(group: EverywhereGroup) {
        const { q, sort, desc } = this.store.route();
        return routeParams({ c: group.location.c, path: '', q, sort, desc, all: false });
    }

    protected hereTitle(group: EverywhereGroup): string {
        return $localize`Search only in “${group.location.name}:place:”, with every sort and the viewer.`;
    }

    protected iconOf(item: LibraryItem): string {
        return KIND_ICONS[item.kind] ?? 'file';
    }

    protected showTitle(item: LibraryItem): string {
        return $localize`Show “${item.path}:path:” in its folder.`;
    }

    protected detailsOf(item: LibraryItem): string {
        return joinParts([item.directory || '/', formatSize(item.size)]);
    }

    /** Shows the file in its folder, in its location (what has a viewer opens there). */
    protected open(group: EverywhereGroup, item: LibraryItem): void {
        this.store.reveal(item.path, group.location.c);
    }

    protected itemTarget(group: EverywhereGroup, item: LibraryItem): MenuTarget {
        const folder = item.directory ?? '';
        return {
            kind: 'file',
            key: `everywhere:${group.location.c ?? ''}:${item.path}`,
            name: item.name,
            entries: () => [
                {
                    id: 'show',
                    label: $localize`Show in its folder`,
                    icon: 'folder',
                    title: $localize`Open “${folder || group.location.name}:folder:” and show this file there.`,
                    run: () => this.open(group, item),
                },
                {
                    id: 'newTab',
                    label: $localize`Open folder in new tab`,
                    icon: 'external',
                    title: $localize`Open its folder in a tab of its own.`,
                    newTab: true,
                    href: this.store.pageUrl(this.store.browseParams(folder, group.location.c)),
                },
            ],
        };
    }

    /** The library, then each connection the session uses (as the server lists them). */
    private async begin(route: LibraryRoute): Promise<void> {
        if (!route.q || !route.all) {
            this.search.stop();
            return;
        }
        const generation = this.store.generation();
        const connections = await this.connections.load();
        if (generation !== this.store.generation()) return;
        const locations: EverywhereLocation[] = [
            { c: null, name: this.config.libraryName() },
            ...connections.map((connection) => ({ c: connection.id, name: connection.name })),
        ];
        this.search.start(locations);
    }

    private async fetch(location: EverywhereLocation, after: string | null, signal: AbortSignal) {
        const route = this.store.route();
        const ask = { q: route.q, path: '', after, limit: EVERYWHERE_PAGE, sort: currentSort(route), desc: route.desc };
        if (location.c === null) return this.api.search(ask, signal);
        const page = await this.api.connectionSearch(location.c, ask, signal);
        return { ...page.results, coverage: page.coverage } satisfies EverywherePage;
    }
}
