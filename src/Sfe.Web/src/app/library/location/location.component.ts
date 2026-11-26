import { pickedFiles, UploadQueue } from '../../files/uploads';
import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    ElementRef,
    inject,
    viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { CopyPathService } from '../../core/copy-path.service';
import { joinParts } from '../../core/format';
import { icu } from '../../core/i18n/icu';
import { SortField } from '../library.models';
import { IconComponent } from '../../shared/icon.component';
import { defaultSort, SEARCH_SORTS } from '../library-route';
import { DefaultTagsBadgeComponent } from '../../tags/default-tags-badge.component';
import { exclusionChip, exclusionTitle } from '../items/exclusions';
import { FolderMenu } from '../folder-menu';
import { ContextMenu, ContextMenuDirective } from '../../menus/context-menu';
import { ListingStore } from '../listing.store';
import { SearchBoxState, SearchExplainComponent } from '../search-explain/search-explain.component';
import { PinButtonComponent } from '../quick-links/pin-button.component';
import { FolderNavComponent } from './folder-nav.component';
import { LocationLinks } from './location-links.service';
import { READ_ONLY_LABEL, readOnlyReason, readOnlyText } from '../read-only';

const SORT_OPTIONS: [SortField, string][] = [
    ['name', $localize`:sort by:Name`],
    ['modified', $localize`:sort by:Date modified`],
    ['size', $localize`:sort by:Size`],
    ['type', $localize`:sort by:Type`],
    ['duration', $localize`:sort by:Duration`],
    ['resolution', $localize`:sort by:Resolution`],
    ['path', $localize`:sort by:Path`],
];

/** Where the listing is: folder navigation, breadcrumbs, the search in words, the counts and the sort. */
@Component({
    selector: 'section[sfeLocation]',
    imports: [
        IconComponent,
        RouterLink,
        ContextMenuDirective,
        SearchExplainComponent,
        FolderNavComponent,
        PinButtonComponent,
        DefaultTagsBadgeComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'location' },
    templateUrl: './location.component.html',
})
export class LocationComponent {
    protected readonly store = inject(ListingStore);
    protected readonly copyPaths = inject(CopyPathService);
    protected readonly search = inject(SearchBoxState);
    protected readonly links = inject(LocationLinks);
    protected readonly uploads = inject(UploadQueue);
    protected readonly folder = inject(FolderMenu);
    private readonly contextMenu = inject(ContextMenu);
    protected readonly moreTitle = computed(() =>
        this.store.route().q
            ? $localize`Actions on this search.`
            : $localize`Actions on this folder: new folder, upload, tags, quick link and more.`,
    );

    /** The folder's menu (New folder, Upload…, Tags, quick link, Copy full path, Properties…), under the button. */
    protected openFolderMenu(event: MouseEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.contextMenu.toggle(this.folder.target(), event.currentTarget as HTMLElement);
    }
    protected readonly canTag = this.folder.canTag;
    /** Uploads the files picked into the folder listed. */
    protected upload(input: HTMLInputElement): void {
        void this.uploads.add(pickedFiles(input), this.store.route().path);
        input.value = '';
    }

    protected readonly canCreateFolder = this.folder.canCreateFolder;
    protected readonly canCreateFile = this.folder.canCreateFile;
    /** What the folder browsed is kept out of (scans, trickplay, thumbnails): a small note for everyone. */
    private readonly exclusions = computed(() => this.folder.exclusions());
    protected readonly exclusion = computed(() => exclusionChip(this.exclusions()));
    protected readonly exclusionTitle = computed(() =>
        exclusionTitle(this.exclusions(), this.store.listing()?.path ?? ''),
    );
    protected readonly exclusionEditTitle = computed(
        () => $localize`${this.exclusionTitle()}:hint: Click to change it.`,
    );
    protected readonly sortAscending = $localize`Ascending order (A to Z, smallest first). Click to reverse it.`;
    protected readonly sortDescending = $localize`Descending order (Z to A, largest first). Click to reverse it.`;
    /** The default tags of the folder browsed, its own and its parents': for everyone. */
    protected readonly defaultTags = computed(() => {
        const page = this.store.listing();
        const defaults = page && !this.store.searching() && 'defaultTags' in page ? (page.defaultTags ?? null) : null;
        return defaults && (defaults.own.length || defaults.inherited.length) ? defaults : null;
    });
    protected readonly folderLabel = computed(() =>
        this.store.route().path ? `“${this.folderName()}”` : $localize`the top of the library`,
    );
    protected readonly canExclude = this.folder.canExclude;
    /** Why the session can't change the folder browsed, the chip's hint; null when it can (no chip). */
    protected readonly readOnly = computed(() => {
        const reason = readOnlyReason(this.store.listing());
        return reason && this.store.status() === 'ready' ? readOnlyText(reason) : null;
    });
    protected readonly readOnlyLabel = READ_ONLY_LABEL;

    protected openExclusions(): void {
        this.folder.openExclusions();
    }

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
                { rootMargin: `-${topbar}px 0px 0px 0px` },
            );
            observer.observe(this.head().nativeElement);
            destroyRef.onDestroy(() => {
                observer.disconnect();
                this.links.headHidden.set(false);
            });
        });
    }

    protected readonly summary = computed(() => {
        const page = this.store.listing();
        if (!page || this.store.status() !== 'ready') return '';
        if (this.store.route().q) {
            const total = this.store.total();
            const count = total?.count ?? this.store.items().length;
            const more = total ? total.capped : this.store.hasMore();
            return count ? searchSummary(count, more, page.path) : '';
        }
        return 'directoryCount' in page
            ? joinParts([
                  page.directoryCount
                      ? icu($localize`{count, plural, one {# folder} other {# folders}}`, {
                            count: page.directoryCount,
                        })
                      : null,
                  page.fileCount
                      ? icu($localize`{count, plural, one {# file} other {# files}}`, { count: page.fileCount })
                      : null,
              ])
            : '';
    });

    /** The folder browsed, named as its breadcrumb (the library's name for the root). */
    protected readonly folderName = computed(() => this.links.crumbs().at(-1)?.name ?? '');

    protected readonly scopeTagsTitle = computed(() =>
        this.store.searching()
            ? $localize`Add or remove tags on all the results of this search (asks first).`
            : $localize`Add or remove tags on all the files of this folder, or of its sub-folders too (asks first).`,
    );

    protected newFolder(): void {
        this.folder.newFolder();
    }

    protected newFile(): void {
        this.folder.newFile();
    }

    /** The tags of every file of the folder browsed, or of every match of the search. */
    protected openScopeTags(): void {
        this.folder.openScopeTags();
    }

    protected changeSort(event: Event): void {
        const sort = (event.target as HTMLSelectElement).value as SortField;
        this.store.navigate({ sort: sort === defaultSort(this.store.route()) ? '' : sort }, { replace: true });
    }
}

/** "12 results in Photos", "500+ results": the count of a search's results, and where it looks when not everywhere. */
function searchSummary(count: number, more: boolean, path: string): string {
    if (path)
        return more
            ? icu($localize`{count, plural, one {#+ result in {path}} other {#+ results in {path}}}`, { count, path })
            : icu($localize`{count, plural, one {# result in {path}} other {# results in {path}}}`, { count, path });
    return more
        ? icu($localize`{count, plural, one {#+ result} other {#+ results}}`, { count })
        : icu($localize`{count, plural, one {# result} other {# results}}`, { count });
}
