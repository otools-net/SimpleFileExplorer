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
import { UsableConnections } from '../../core/usable-connections';
import { EmptyStateComponent } from '../../shared/empty-state.component';
import { connectionProblemOf } from '../connection-problems';
import { EverywhereComponent } from '../everywhere/everywhere.component';
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
        EverywhereComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './listing.component.html',
})
export class ListingComponent {
    protected readonly store = inject(ListingStore);
    protected readonly previews = inject(PreviewsService);
    private readonly config = inject(ConfigService);
    private readonly connections = inject(UsableConnections);
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

    /** The connection listed can't be used (sign in again, switched off...): what to say instead of an error. */
    protected readonly connectionProblem = computed(() => {
        const c = this.store.route().c;
        if (!c || this.store.status() !== 'error') return null;
        const known = this.connections.list().find((connection) => connection.id === c);
        if (!known) void this.connections.load();
        return connectionProblemOf(this.store.error(), known?.level ?? null, this.config);
    });

    /** A search of a connection whose index is not whole: what it finds may miss files. */
    protected readonly coverageNote = computed(() => {
        const page = this.store.listing();
        const coverage = page && 'coverage' in page ? page.coverage : null;
        if (coverage === 'none')
            return $localize`This connection isn't in the index of this space yet: its index is being made.`;
        if (coverage === 'partial')
            return $localize`The index of this connection is being brought up to date: some files may be missing.`;
        return null;
    });
    protected readonly noResultsText = computed(() =>
        this.store.route().c
            ? $localize`Nothing in the index of this connection matches this search.`
            : $localize`Nothing in the index matches this search. If files were added recently, scan this folder.`,
    );
    protected readonly notFoundText = $localize`It may have been moved or deleted.`;
    protected readonly errorHeading = computed(() =>
        this.store.error()?.status === 404
            ? $localize`Folder not found`
            : this.store.searching()
              ? $localize`Search isn't available`
              : $localize`Could not load this folder`,
    );
    /** The server's reason, then what to do about it: who can fix it depends on the rights. */
    protected readonly libraryMissingText = computed(() => {
        const reason = this.store.error()?.message ?? '';
        const advice = this.canInstall()
            ? $localize`Plug its drive in, or pick another folder.`
            : $localize`Ask an administrator to plug its drive in or pick another folder.`;
        return `${reason} ${advice}`.trim();
    });

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
