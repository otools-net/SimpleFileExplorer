import { pickedFiles, UploadQueue } from '../../files/uploads';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CopyPathService } from '../../core/copy-path.service';
import { joinParts, plural } from '../../core/format';
import { SortField } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { defaultSort, SEARCH_SORTS } from '../library-route';
import { DefaultTagsBadgeComponent } from '../../tags/default-tags-badge.component';
import { exclusionChip, exclusionTitle } from '../items/exclusions';
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
    imports: [IconComponent, RouterLink, SearchExplainComponent, FolderNavComponent, PinButtonComponent, DefaultTagsBadgeComponent],
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
                        <!-- data-path: dragged onto the quick links, it adds the folder; data-drop: items dropped on it move there. -->
                        <a routerLink="/" [queryParams]="store.browseParams(crumb.path)" [attr.data-path]="crumb.path" [attr.data-drop]="crumb.path">{{ crumb.name }}</a>
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
            @if (uploads.canUploadInto()) {
                <button type="button" class="icon-button small ghost upload-button" aria-label="Upload files" title="Upload files into this folder (or drop them on the page)"
                        (click)="filesInput.click()">
                    <svg sfeIcon="upload"></svg>
                </button>
                <button type="button" class="icon-button small ghost upload-folder-button" aria-label="Upload a folder" title="Upload a folder, with everything in it, into this folder"
                        (click)="folderInput.click()">
                    <svg sfeIcon="folder-up"></svg>
                </button>
            }
            <input #filesInput type="file" multiple hidden (change)="upload(filesInput)">
            <input #folderInput type="file" webkitdirectory hidden (change)="upload(folderInput)">
            @if (canCreateFile()) {
                <button type="button" class="icon-button small ghost new-file-button" aria-label="New file" title="New empty file in this folder"
                        (click)="newFile()">
                    <svg sfeIcon="file-plus"></svg>
                </button>
            }
            @if (canCreateFolder()) {
                <button type="button" class="icon-button small ghost new-folder-button" aria-label="New folder" title="New folder in this folder (Shift+N)"
                        (click)="newFolder()">
                    <svg sfeIcon="folder-plus"></svg>
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
            <p class="summary">{{ summary() }}@if (defaultTags(); as defaults) {
                    <!-- The tags the files put here get, listed in its panel; Change… opens the folder's tags for those who may. -->
                    <sfe-default-tags-badge [own]="defaults.own" [inherited]="defaults.inherited" [folder]="folderLabel()"
                                            [editable]="canTag()" (edit)="openScopeTags()" />
                }@if (exclusion(); as exclusion) {
                    @if (canExclude()) {
                        <button type="button" class="exclusion-chip" [title]="exclusionTitle() + ' Click to change it.'" (click)="openExclusions()">{{ exclusion }}</button>
                    } @else {
                        <span class="exclusion-chip" [title]="exclusionTitle()">{{ exclusion }}</span>
                    }
                }
            </p>
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
    protected readonly uploads = inject(UploadQueue);
    /** Tagging every file of the folder or search changes tags: hidden without the right (here, with folder rules). */
    /** Tagging the files, or (browsing) setting the folder's default tags: both in the same dialog. */
    protected readonly canTag = computed(() => this.store.can(Rights.assignTags) || !this.store.searching() && this.store.can(Rights.folderDefaultTags));
    /** Folders are made in the folder browsed (not in a search, nor in a folder shown only on the way to another). */
    /** Uploads the files picked into the folder listed. */
    protected upload(input: HTMLInputElement): void {
        void this.uploads.add(pickedFiles(input), this.store.route().path);
        input.value = '';
    }

    protected readonly canCreateFolder = computed(() =>
        !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFolders));
    protected readonly canCreateFile = computed(() =>
        !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFiles));
    /** What the folder browsed is kept out of (scans, trickplay, thumbnails): a small note for everyone. */
    private readonly exclusions = computed(() => {
        const page = this.store.listing();
        return page && !this.store.searching() && 'exclusions' in page ? page.exclusions ?? null : null;
    });
    protected readonly exclusion = computed(() => exclusionChip(this.exclusions()));
    protected readonly exclusionTitle = computed(() => exclusionTitle(this.exclusions(), this.store.listing()?.path ?? ''));
    /** The default tags of the folder browsed, its own and its parents': for everyone. */
    protected readonly defaultTags = computed(() => {
        const page = this.store.listing();
        const defaults = page && !this.store.searching() && 'defaultTags' in page ? page.defaultTags ?? null : null;
        return defaults && (defaults.own.length || defaults.inherited.length) ? defaults : null;
    });
    protected readonly folderLabel = computed(() => this.store.route().path ? `“${this.folderName()}”` : 'the top folder');
    protected readonly canExclude = computed(() => this.store.can(Rights.excludeFolders) && Boolean(this.dialogs.folderExclusions()));

    protected openExclusions(): void {
        const page = this.store.listing();
        if (page)
            this.dialogs.folderExclusions()?.open(page.path, this.exclusions());
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

    protected newFolder(): void {
        this.dialogs.name()?.newFolder(this.store.route().path);
    }

    protected newFile(): void {
        this.dialogs.name()?.newFile(this.store.route().path);
    }

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
