import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CopyPathService } from '../../core/copy-path.service';
import { joinParts, plural } from '../../core/format';
import { SortField } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { defaultSort, SEARCH_SORTS } from '../library-route';
import { ListingStore } from '../listing.store';
import { SearchBoxState, SearchExplainComponent } from '../search-explain/search-explain.component';
import { PinButtonComponent } from '../quick-links/pin-button.component';
import { FolderNavComponent } from './folder-nav.component';
import { LocationLinks } from './location-links.service';

const SORT_OPTIONS: [SortField, string][] = [
    ['name', 'Name'],
    ['modified', 'Date modified'],
    ['size', 'Size'],
    ['type', 'Type'],
    ['duration', 'Duration'],
    ['resolution', 'Resolution'],
    ['path', 'Path'],
];

/** Where the listing is: folder navigation, breadcrumbs, the search in words, the counts and the sort. */
@Component({
    selector: 'section[sfeLocation]',
    imports: [IconComponent, RouterLink, SearchExplainComponent, FolderNavComponent, PinButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'location' },
    template: `
        <div class="location-head" #head>
            @if (links.folderNav(); as nav) {
                <nav [sfeFolderNav]="nav"></nav>
            }
            <nav class="crumbs" aria-label="Location">
                @for (crumb of links.crumbs(); track crumb.path; let first = $first, last = $last) {
                    @if (!first) {
                        <svg sfeIcon="right" class="sep"></svg>
                    }
                    @if (last && !store.route().q) {
                        <span class="current" aria-current="page">{{ crumb.name }}</span>
                    } @else {
                        <!-- data-path: dragged onto the quick links, it adds the folder. -->
                        <a routerLink="/" [queryParams]="store.browseParams(crumb.path)" [attr.data-path]="crumb.path">{{ crumb.name }}</a>
                    }
                }
                @if (store.route().q) {
                    <svg sfeIcon="right" class="sep"></svg>
                    <span class="current search-label">Search “{{ store.route().q }}”</span>
                }
            </nav>
            @if (!store.route().q) {
                <button [sfePin]="store.route().path" [name]="folderName()" class="small ghost"></button>
            }
            @if (canTag()) {
                <button type="button" class="icon-button small ghost scope-tags-button" [attr.aria-label]="scopeTagsTitle()" [title]="scopeTagsTitle()"
                        (click)="openScopeTags()">
                    <svg sfeIcon="tag"></svg>
                </button>
            }
            @if (copyPaths.enabled()) {
                <!-- Copies the folder browsed, or searched in. -->
                <button type="button" class="icon-button small ghost copy-folder-path" aria-label="Copy the full path of this folder"
                        title="Copy the full path of this folder" (click)="copyPaths.copy(store.route().path)">
                    <svg sfeIcon="copy"></svg>
                </button>
            }
        </div>
        @if (search.canExplain() && search.explainOpen()) {
            <sfe-search-explain id="search-explain" />
        }
        <div class="location-bar">
            <p class="summary">{{ summary() }}</p>
            <div class="sort">
                <label for="sort-select" class="visually-hidden">Sort by</label>
                <select id="sort-select" (change)="changeSort($event)">
                    @for (option of sortOptions; track option[0]) {
                        <!-- Only searches have a path, duration or resolution order. -->
                        @if (!searchSorts.has(option[0]) || store.searching()) {
                            <option [value]="option[0]" [selected]="option[0] === store.sort()">{{ option[1] }}</option>
                        }
                    }
                </select>
                <button type="button" class="icon-button small sort-direction" aria-label="Sort direction" title="Sort direction"
                        [class.desc]="store.route().desc" (click)="store.navigate({ desc: !store.route().desc }, { replace: true })">
                    <svg sfeIcon="up"></svg>
                </button>
            </div>
        </div>
    `,
})
export class LocationComponent {
    protected readonly store = inject(ListingStore);
    protected readonly copyPaths = inject(CopyPathService);
    protected readonly search = inject(SearchBoxState);
    protected readonly links = inject(LocationLinks);
    private readonly dialogs = inject(Dialogs);
    /** Tagging every file of the folder or search changes tags: hidden without the right (here, with folder rules). */
    protected readonly canTag = computed(() => this.store.can(Rights.assignTags));
    protected readonly sortOptions = SORT_OPTIONS;
    protected readonly searchSorts = SEARCH_SORTS;
    private readonly head = viewChild.required<ElementRef<HTMLElement>>('head');

    constructor() {
        // Tells the compact bar when the header is under the top bar (or above the screen).
        const destroyRef = inject(DestroyRef);
        afterNextRender(() => {
            const topbar = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--topbar-h')) || 60;
            const observer = new IntersectionObserver(
                ([entry]) => this.links.headHidden.set(!entry.isIntersecting && entry.boundingClientRect.top < topbar),
                { rootMargin: `-${topbar}px 0px 0px 0px` });
            observer.observe(this.head().nativeElement);
            destroyRef.onDestroy(() => {
                observer.disconnect();
                this.links.headHidden.set(false);
            });
        });
    }

    protected readonly summary = computed(() => {
        const page = this.store.listing();
        if (!page || this.store.status() !== 'ready')
            return '';
        if (this.store.route().q) {
            const total = this.store.total();
            const count = total?.count ?? this.store.items().length;
            const more = total ? total.capped : this.store.hasMore();
            return count ? `${plural(count, 'result')}${more ? '+' : ''}${page.path ? ` in ${page.path}` : ''}` : '';
        }
        return 'directoryCount' in page
            ? joinParts([
                page.directoryCount ? plural(page.directoryCount, 'folder') : null,
                page.fileCount ? plural(page.fileCount, 'file') : null,
            ])
            : '';
    });

    /** The folder browsed, named as its breadcrumb (the library's name for the root). */
    protected readonly folderName = computed(() => this.links.crumbs().at(-1)?.name ?? '');

    protected readonly scopeTagsTitle = computed(() => this.store.searching()
        ? 'Add or remove tags on all the results of this search (asks first)'
        : 'Add or remove tags on all the files of this folder, or of its sub-folders too (asks first)');

    /** The tags of every file of the folder browsed, or of every match of the search. */
    protected openScopeTags(): void {
        const { path, q } = this.store.route();
        this.dialogs.scopeTags()?.open({ path, recursive: false, query: q || null });
    }

    protected changeSort(event: Event): void {
        const sort = (event.target as HTMLSelectElement).value as SortField;
        this.store.navigate({ sort: sort === defaultSort(this.store.route()) ? '' : sort }, { replace: true });
    }
}
