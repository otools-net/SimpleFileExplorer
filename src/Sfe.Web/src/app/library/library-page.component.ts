import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, OnInit, signal, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, NavigationEnd, NavigationStart, Router } from '@angular/router';
import { redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { baseName, parentPath } from '../core/paths';
import { ThemeService } from '../core/theme.service';
import { ImageEditorDialog } from '../editor/image-editor.dialog';
import { EmptyStateComponent } from '../shared/empty-state.component';
import { FileTagsDialog } from '../tags/file-tags.dialog';
import { FilesTagsDialog } from '../tags/files-tags.dialog';
import { ScopeTagsDialog } from '../tags/scope-tags.dialog';
import { TagEditorDialog } from '../tags/tag-editor.dialog';
import { TagManagerDialog } from '../tags/tag-manager.dialog';
import { RemoveFromIndexDialog } from './scan/remove-from-index.dialog';
import { ListingComponent } from './items/listing.component';
import { currentPosition } from './items/listing-position';
import { initialHistoryState, readRestoreState, readRoute, RestoreState } from './library-route';
import { ListingStore } from './listing.store';
import { LocationComponent } from './location/location.component';
import { LocationStripComponent } from './location/location-strip.component';
import { ScanPanelComponent } from './scan/scan-panel.component';
import { SelectionBarComponent } from './selection-bar.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { SidebarService } from './sidebar/sidebar.service';
import { Popovers } from './topbar/popovers.service';
import { Dialogs } from './dialogs.service';
import { TopbarComponent } from './topbar/topbar.component';
import { ViewerComponent } from './viewer/viewer.component';

/** Scrolling stopped this long (ms), the position is written into the history entry. */
const REMEMBER_DELAY = 250;

/** Whether the page was shown before (see ngOnInit). */
let created = false;

/**
 * The listing page: a folder of the library or a search (`?path=&q=&sort=&desc=1`), with the tag sidebar, the
 * viewer and the dialogs. Loads the configuration first; a navigation loads the listing of its route.
 */
@Component({
    selector: 'sfe-library-page',
    imports: [TopbarComponent, ScanPanelComponent, SidebarComponent, LocationComponent, LocationStripComponent, ListingComponent, SelectionBarComponent,
        EmptyStateComponent, ViewerComponent, FileTagsDialog, FilesTagsDialog, ScopeTagsDialog, TagEditorDialog, TagManagerDialog,
        RemoveFromIndexDialog, ImageEditorDialog],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '[class.searching]': 'store.searching()',
        '[class.selecting]': 'store.selection().size > 0',
        '[class.sidebar-collapsed]': '!sidebar.open()',
        '(document:keydown)': 'onKeydown($event)',
    },
    template: `
        @if (unreachable(); as error) {
            <sfe-empty-state icon="close" heading="The server is not reachable" [text]="error" />
        } @else if (ready()) {
            <header sfeTopbar></header>
            <sfe-scan-panel />
            <div class="layout">
                <sfe-sidebar />
                <main class="page">
                    <sfe-location-strip />
                    <section sfeLocation></section>
                    <sfe-listing />
                    <sfe-selection-bar />
                </main>
            </div>
            <sfe-viewer />
            <sfe-file-tags />
            <sfe-files-tags />
            <sfe-scope-tags />
            <sfe-tag-editor />
            <sfe-tag-manager />
            <sfe-remove-from-index />
            <!-- After the viewer: it opens over it. -->
            <sfe-image-editor />
        }
    `,
})
export class LibraryPageComponent implements OnInit {
    protected readonly store = inject(ListingStore);
    protected readonly sidebar = inject(SidebarService);
    private readonly config = inject(ConfigService);
    private readonly theme = inject(ThemeService);
    private readonly popovers = inject(Popovers);
    private readonly dialogs = inject(Dialogs);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly destroyRef = inject(DestroyRef);
    private readonly topbar = viewChild(TopbarComponent);

    protected readonly ready = signal(false);
    protected readonly unreachable = signal<string | null>(null);

    constructor() {
        const title = inject(Title);
        effect(() => {
            const { path, q } = this.store.route();
            const config = this.config.config();
            if (config)
                title.setTitle(q ? `“${q}” – SFE` : `${baseName(path) || config.rootName} – SFE`);
        });
    }

    async ngOnInit(): Promise<void> {
        // Created again when coming back from another page (the administration page): its history entry says what was
        // loaded and where it was scrolled. The first time, the router has already replaced the entry's state.
        const restore = created ? readRestoreState(history.state) : initialHistoryState;
        created = true;
        try {
            await this.config.load();
        } catch (error) {
            // Not signed in, or the server is not set up: the sign-in or setup page opens instead.
            if (!redirected(error))
                this.unreachable.set((error as Error).message);
            return;
        }
        this.theme.setDefault(this.config.value.theme);
        history.scrollRestoration = 'manual';
        this.watchNavigation();
        this.store.route.set(readRoute(this.route.snapshot.queryParamMap));
        this.ready.set(true);
        void this.store.load(restore);
    }

    /**
     * The history entry of a listing remembers its scroll position and the amount of items loaded, written when
     * leaving it and when the scrolling stops, so that "back", "forward" and a reload restore them.
     */
    private watchNavigation(): void {
        let restore: RestoreState = {};
        let timer = 0;
        const subscription = this.router.events.subscribe(event => {
            if (event instanceof NavigationStart) {
                clearTimeout(timer);
                if (event.navigationTrigger === 'popstate') {
                    restore = readRestoreState(event.restoredState);
                } else {
                    restore = {};
                    this.rememberPosition();
                }
            } else if (event instanceof NavigationEnd) {
                this.store.route.set(readRoute(this.router.routerState.snapshot.root.queryParamMap));
                void this.store.load(restore);
            }
        });
        // A reload (or a tab the browser closes and opens again) keeps the history entry: the position comes back
        // with `initialHistoryState`. It is written once the scrolling stops: Chrome ignores a `replaceState` made
        // while the page unloads for a reload.
        const scrolled = () => {
            clearTimeout(timer);
            timer = setTimeout(() => this.rememberPosition(), REMEMBER_DELAY);
        };
        const hidden = () => {
            if (document.visibilityState === 'hidden')
                this.rememberPosition();
        };
        window.addEventListener('scroll', scrolled, { passive: true });
        document.addEventListener('visibilitychange', hidden);
        this.destroyRef.onDestroy(() => {
            subscription.unsubscribe();
            clearTimeout(timer);
            window.removeEventListener('scroll', scrolled);
            document.removeEventListener('visibilitychange', hidden);
        });
    }

    private rememberPosition(): void {
        if (this.store.status() === 'ready')
            history.replaceState({ ...history.state, ...currentPosition(), loaded: this.store.items().length }, '');
    }

    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as Element;
        // A selection checkbox is an input, but not one being typed in.
        const typing = target.closest?.('input:not([type="checkbox"]), select, textarea, dialog');
        if (!typing && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a' && this.store.files().length) {
            event.preventDefault();
            this.store.selectAll();
            return;
        }
        if (typing || event.ctrlKey || event.metaKey)
            return;
        const { path, q } = this.store.route();
        if (event.key === '/') {
            event.preventDefault();
            this.topbar()?.searchBox().focus();
        } else if (event.key === '?') {
            event.preventDefault();
            this.dialogs.searchHelp()?.open();
        } else if ((event.key === 'Backspace' || (event.altKey && event.key === 'ArrowUp')) && !q && path) {
            event.preventDefault();
            this.store.navigate({ path: parentPath(path) });
        } else if ((event.key === '[' || event.key === ']') && !q && this.store.siblings()) {
            const sibling = event.key === '[' ? this.store.siblings()!.previous : this.store.siblings()!.next;
            if (sibling)
                this.store.navigate({ path: sibling.path });
        } else if (event.key === 'Escape') {
            if (this.popovers.open())
                this.popovers.close();
            else
                this.store.clearSelection();
        }
    }
}
