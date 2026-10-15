import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { highlightPattern } from '../../core/format';
import { Rights } from '../../core/rights';
import { EmptyStateComponent } from '../../shared/empty-state.component';
import { ListingStore } from '../listing.store';
import { FileRowComponent, FileTileComponent } from './file-tile.component';
import { FolderItemComponent } from './folder-item.component';
import { PreviewsService } from './previews.service';
import { ScrollProgressComponent } from './scroll-progress.component';

/**
 * The listed folders and files: tiles (grid and one-per-row views) or rows (list view), more pages loading as the
 * end of the list comes near.
 */
@Component({
    selector: 'sfe-listing',
    imports: [RouterLink, EmptyStateComponent, FileTileComponent, FileRowComponent, FolderItemComponent,
        ScrollProgressComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="content" aria-live="polite">
            @switch (store.status()) {
                @case ('loading') {
                    <div class="files grid skeleton" aria-busy="true">
                        @for (tile of skeleton; track $index) {
                            <div class="tile"><div class="preview"></div><div class="info"><span></span><span></span></div></div>
                        }
                    </div>
                }
                @case ('error') {
                    @let error = store.error();
                    @if (error?.type === 'library-root-missing') {
                        <sfe-empty-state icon="folder" heading="The library folder is missing"
                                         [text]="error!.message + (canInstall() ? ' Plug its drive in, or pick another folder.' : ' Ask an administrator to plug its drive in or pick another folder.')">
                            @if (canInstall()) {
                                <a class="button" routerLink="/admin" [queryParams]="{ section: 'system', page: 'installation' }" [state]="{ sfeFromLibrary: true }"
                                   title="Pick the library folder in Administration › System › Installation">Pick another folder</a>
                            }
                        </sfe-empty-state>
                    } @else {
                        <sfe-empty-state [icon]="error?.status === 404 ? 'folder' : 'close'"
                                         [heading]="error?.status === 404 ? 'Folder not found' : store.searching() ? 'Search is unavailable' : 'Could not load this folder'"
                                         [text]="error?.status === 404 ? 'It may have been moved or deleted.' : error?.message ?? null">
                            <a class="button" routerLink="/" [queryParams]="store.browseParams('')">Go to library root</a>
                        </sfe-empty-state>
                    }
                }
                @case ('ready') {
                    @if (!store.items().length) {
                        @if (store.searching()) {
                            <sfe-empty-state icon="search" heading="No results" text="Nothing in the index matches this search. If files were added recently, run Scan on this folder." />
                        } @else {
                            <sfe-empty-state icon="folder" heading="This folder is empty" />
                        }
                    }
                    @if (store.folders().length) {
                        <h2 class="section-title">Folders</h2>
                        <div class="folders" [class]="folderLayout()">
                            @for (folder of store.folders(); track folder.path) {
                                <sfe-folder-item [item]="folder" [cards]="folderCards()" />
                            }
                        </div>
                    }
                    @if (store.files().length) {
                        <!-- Only label the files section when both kinds are listed. -->
                        @if (store.folders().length) {
                            <h2 class="section-title">Files</h2>
                        }
                        <div class="files" [class]="store.view()" [style.--ratio-guess]="previews.ratioGuess()">
                            @if (store.view() === 'list') {
                                @for (file of store.files(); track file.path) {
                                    <div sfeFileRow [item]="file" [pattern]="pattern()"></div>
                                }
                            } @else {
                                @for (file of store.files(); track file.path) {
                                    <article sfeFileTile [item]="file" [pattern]="pattern()"></article>
                                }
                            }
                        </div>
                    }
                }
            }
        </div>
        <div #sentinel class="sentinel" [hidden]="store.status() !== 'ready' || !store.hasMore()"><span class="spinner"></span></div>
        <sfe-scroll-progress />
    `,
})
export class ListingComponent {
    protected readonly store = inject(ListingStore);
    protected readonly previews = inject(PreviewsService);
    private readonly config = inject(ConfigService);
    private readonly injector = inject(Injector);
    private readonly sentinel = viewChild.required<ElementRef<HTMLElement>>('sentinel');

    protected readonly skeleton = Array.from({ length: 12 });
    protected readonly pattern = computed(() => this.store.route().q ? highlightPattern(this.store.route().q) : null);
    /** Folders are cards with pictures (`config.folderPictures`, 0 = off), except in the list view. */
    protected readonly folderCards = computed(() => this.store.view() !== 'list' && (this.config.config()?.folderPictures ?? 0) > 0);
    /** Server administrators can pick another library folder (System › Installation). */
    protected readonly canInstall = computed(() => this.config.can(Rights.serverSettings));
    protected readonly folderLayout = computed(() => this.store.view() === 'list' ? 'list' : this.folderCards() ? 'grid covers' : 'grid');

    private readonly observer = new IntersectionObserver(entries => {
        if (entries.some(entry => entry.isIntersecting))
            void this.loadMore();
    }, { rootMargin: '1200px 0px' });

    constructor() {
        afterNextRender(() => this.observer.observe(this.sentinel().nativeElement));
        inject(DestroyRef).onDestroy(() => this.observer.disconnect());
        // New files listed: the typical shape of their previews may change.
        effect(() => {
            this.store.files();
            this.previews.scheduleGuess(() => this.store.items());
        });
    }

    private async loadMore(): Promise<void> {
        await this.store.loadMore();
        // The observer only fires on visibility changes: when a short page leaves the sentinel in view, observing
        // it again (once the page is drawn) reports the current state and keeps loading.
        if (this.store.hasMore()) {
            afterNextRender(() => {
                const sentinel = this.sentinel().nativeElement;
                this.observer.unobserve(sentinel);
                this.observer.observe(sentinel);
            }, { injector: this.injector });
        }
    }
}
