import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, OnInit, signal, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, NavigationEnd, NavigationStart, Router } from '@angular/router';
import { redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { baseName, parentPath } from '../core/paths';
import { ThemeService } from '../core/theme.service';
import { ImageEditorDialog } from '../editor/image-editor.dialog';
import { ItemMenu, ItemMenuComponent } from '../files/item-menu';
import { NameDialog } from '../files/name.dialog';
import { LibraryItem } from '../core/models';
import { Rights } from '../core/rights';
import { EmptyStateComponent } from '../shared/empty-state.component';
import { FileTagsDialog } from '../tags/file-tags.dialog';
import { FilesTagsDialog } from '../tags/files-tags.dialog';
import { ScopeTagsDialog } from '../tags/scope-tags.dialog';
import { TagEditorDialog } from '../tags/tag-editor.dialog';
import { TagManagerDialog } from '../tags/tag-manager.dialog';
import { RemoveFromIndexDialog } from './scan/remove-from-index.dialog';
import { ListingComponent } from './items/listing.component';
import { currentPosition } from './items/listing-position';
import { focusedListItem, focusItem, listedItems, MOVE_KEYS, neighbour } from './items/listing-keys';
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
        RemoveFromIndexDialog, ImageEditorDialog, NameDialog, ItemMenuComponent],
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
            <sfe-name-dialog />
            <sfe-item-menu />
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
    private readonly menu = inject(ItemMenu);
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

    /** New folders can be made in the folder listed. */
    private canCreateFolder(): boolean {
        return !this.store.route().q && this.store.status() === 'ready' && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.createFolders);
    }

    /** The item that has the keyboard focus, else the only item selected. */
    private focusedItem(): LibraryItem | null {
        const path = (document.activeElement as Element | null)?.closest?.('sfe-listing [data-path]')?.getAttribute('data-path');
        const focused = path !== null && path !== undefined ? this.store.items().find(item => item.path === path) : undefined;
        if (focused)
            return focused;
        const selected = this.store.selectedItems();
        return selected.length === 1 ? selected[0] : null;
    }

    /**
     * Keys moving through the listed items once one has the focus (Tab, or a click, puts it there; until then the
     * arrows scroll the page as usual): arrows, Home, End; with Shift the items passed are selected; Space (or
     * Ctrl+Space) selects or deselects the item. Enter opens it (the link's own behaviour). True when handled.
     */
    private onListingKey(event: KeyboardEvent): boolean {
        const current = focusedListItem();
        if (!current || event.altKey || (event.target as Element).matches?.('input'))
            return false;
        const item = this.itemAt(current);
        if (event.key === ' ' && !event.metaKey && item) {
            event.preventDefault();
            this.store.select(item, !this.store.isSelected(item.path));
            return true;
        }
        if (!MOVE_KEYS.has(event.key) || event.ctrlKey || event.metaKey)
            return false;
        event.preventDefault();
        const next = neighbour(listedItems(), current, event.key);
        if (!next)
            return true;
        focusItem(next);
        const target = this.itemAt(next);
        if (event.shiftKey && item && target) {
            if (!this.store.isSelected(item.path))
                this.store.select(item, true);
            this.store.select(target, true, true);
        }
        return true;
    }

    private itemAt(element: HTMLElement): LibraryItem | undefined {
        const path = element.getAttribute('data-path');
        return this.store.items().find(item => item.path === path);
    }

    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as Element;
        // A selection checkbox is an input, but not one being typed in.
        const typing = target.closest?.('input:not([type="checkbox"]), select, textarea, dialog');
        if (!typing && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a' && this.store.items().length) {
            event.preventDefault();
            this.store.selectAll();
            return;
        }
        if (!typing && this.onListingKey(event))
            return;
        // New folder: Ctrl+Shift+N as in Explorer where the browser leaves it to the page (Chrome keeps it), Shift+N.
        if (!typing && event.shiftKey && !event.altKey && event.key.toLowerCase() === 'n' && this.canCreateFolder()) {
            event.preventDefault();
            this.dialogs.name()?.newFolder();
            return;
        }
        if (typing || event.ctrlKey || event.metaKey)
            return;
        const { path, q } = this.store.route();
        if (event.key === 'F2') {
            const item = this.focusedItem();
            if (item && this.menu.canRename(item)) {
                event.preventDefault();
                this.dialogs.name()?.rename(item);
            }
            return;
        }
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
