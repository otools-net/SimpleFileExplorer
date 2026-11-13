import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    effect,
    ElementRef,
    inject,
    Injector,
    viewChild,
} from '@angular/core';
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
    imports: [
        RouterLink,
        EmptyStateComponent,
        FileTileComponent,
        FileRowComponent,
        FolderItemComponent,
        ScrollProgressComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './listing.component.html',
})
export class ListingComponent {
    protected readonly store = inject(ListingStore);
    protected readonly previews = inject(PreviewsService);
    private readonly config = inject(ConfigService);
    private readonly injector = inject(Injector);
    private readonly sentinel = viewChild.required<ElementRef<HTMLElement>>('sentinel');

    protected readonly skeleton = Array.from({ length: 12 });
    protected readonly pattern = computed(() => (this.store.route().q ? highlightPattern(this.store.route().q) : null));
    /** Folders are cards with pictures (`config.folderPictures`, 0 = off), except in the list view. */
    protected readonly folderCards = computed(
        () => this.store.view() !== 'list' && (this.config.config()?.folderPictures ?? 0) > 0,
    );
    /** Server administrators can pick another library folder (System › Installation). */
    protected readonly canInstall = computed(() => this.config.can(Rights.serverSettings));
    protected readonly folderLayout = computed(() =>
        this.store.view() === 'list' ? 'list' : this.folderCards() ? 'grid covers' : 'grid',
    );

    private readonly observer = new IntersectionObserver(
        (entries) => {
            if (entries.some((entry) => entry.isIntersecting)) void this.loadMore();
        },
        { rootMargin: '1200px 0px' },
    );

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
            afterNextRender(
                () => {
                    const sentinel = this.sentinel().nativeElement;
                    this.observer.unobserve(sentinel);
                    this.observer.observe(sentinel);
                },
                { injector: this.injector },
            );
        }
    }
}
