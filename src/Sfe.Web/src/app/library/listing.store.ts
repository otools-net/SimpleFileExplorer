import { afterNextRender, computed, inject, Injectable, Injector, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { MEDIA_KINDS } from '../core/format';
import { FileTags, FolderSiblings, LibraryItem, ListingPage, TagDetails, TagScope, TagSummary } from '../core/models';
import { baseName, parentPath } from '../core/paths';
import { readPreference, writePreference } from '../core/preferences';
import { ToastService } from '../core/toast.service';
import { restorePosition } from './items/listing-position';
import { Dialogs } from './dialogs.service';
import { SidebarService } from './sidebar/sidebar.service';
import { currentSort, LibraryRoute, RestoreState, routeParams } from './library-route';

export type View = 'grid' | 'list' | 'single';
export const VIEWS: readonly View[] = ['grid', 'list', 'single'];
const VIEW_KEY = 'sfe.view';
/** A file shown by `reveal` is looked for among this many items of its folder at most (pages are loaded until then). */
export const REVEAL_LIMIT = 2000;
/** How long (ms) a revealed file stays highlighted. */
const REVEAL_HIGHLIGHT = 2500;

/**
 * The listing page's state: the route (folder or search), the items loaded so far page by page, the folders around
 * the browsed one, and the files selected for tagging. Components render it; navigation goes through `navigate`.
 */
@Injectable({ providedIn: 'root' })
export class ListingStore {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);
    private readonly toasts = inject(ToastService);
    private readonly sidebar = inject(SidebarService);
    private readonly injector = inject(Injector);
    private readonly dialogs = inject(Dialogs);

    readonly route = signal<LibraryRoute>({ path: '', q: '', sort: '', desc: false });
    readonly view = signal<View>(readView());
    readonly items = signal<LibraryItem[]>([]);
    /** The last browse or search page received. */
    readonly listing = signal<ListingPage | null>(null);
    /** The folders before and after the one browsed, see `loadSiblings`. */
    readonly siblings = signal<FolderSiblings | null>(null);
    readonly hasMore = signal(false);
    /** Number of items of the folder, or of the search's matches (`capped`: at least that many); null when unknown. */
    readonly total = signal<{ count: number; capped: boolean } | null>(null);
    readonly status = signal<'loading' | 'ready' | 'error'>('loading');
    readonly error = signal<ApiError | null>(null);
    /** Bumped on navigation so stale responses are dropped; components reset their per-listing state on it. */
    readonly generation = signal(0);
    /** Paths of the selected files (tagged together); cleared on navigation. */
    readonly selection = signal<ReadonlySet<string>>(new Set());
    /** The file `reveal` just showed, highlighted for a moment. */
    readonly revealed = signal<string | null>(null);

    /** The media among the loaded items, in order: what the viewer steps through. */
    readonly media = computed(() => this.items().filter(item => MEDIA_KINDS.has(item.kind)));
    readonly folders = computed(() => this.items().filter(item => item.kind === 'directory'));
    /** The loaded files (not folders), in listing order. */
    readonly files = computed(() => this.items().filter(item => item.kind !== 'directory'));
    readonly searching = computed(() => Boolean(this.route().q));
    readonly sort = computed(() => currentSort(this.route()));

    /** Path of the file last (de)selected: shift-click extends from it. */
    private anchor: string | null = null;
    private loading: Promise<boolean> | null = null;
    /** The current listing's requests: navigating cancels them, on the server too. */
    private requests = new AbortController();

    /** The file to show once its folder is listed, see `reveal`. */
    private revealing: string | null = null;
    private revealTimer = 0;

    /** Cancelled when the listing changes. */
    get signal(): AbortSignal {
        return this.requests.signal;
    }

    navigate(changes: Partial<LibraryRoute>, { replace = false } = {}): void {
        const route = { ...this.route(), ...changes };
        void this.router.navigate(['/'], { queryParams: routeParams(route), replaceUrl: replace });
    }

    /** Query parameters of the link to a folder, keeping the sort. */
    browseParams(path: string) {
        const { sort, desc } = this.route();
        return routeParams({ path, q: '', sort, desc });
    }

    setView(view: View): void {
        this.view.set(view);
        writePreference(VIEW_KEY, view);
    }

    /** Loads the first page of the route; `restore` brings back the amount of items and scroll position. */
    async load(restore: RestoreState = {}): Promise<void> {
        const generation = this.generation() + 1;
        this.generation.set(generation);
        const route = this.route();
        // A search typed letter by letter starts a query per pause: the outdated ones stop running on the server.
        this.requests.abort();
        this.requests = new AbortController();
        this.items.set([]);
        this.hasMore.set(false);
        this.total.set(null);
        this.listing.set(null);
        this.siblings.set(null);
        this.loading = null;
        this.selection.set(new Set());
        this.anchor = null;
        this.status.set('loading');
        this.error.set(null);

        try {
            const minimum = Math.max(this.pageSize(), restore.loaded || 0);
            const page = await this.fetchPage(null, minimum);
            if (generation !== this.generation())
                return;
            this.accept(page);
            // The server caps a page (`MaxPageSize`): coming back to more items than that takes the next pages too,
            // or the scroll position below them couldn't be restored.
            while (restore.loaded && this.items().length < restore.loaded && this.hasMore()) {
                const loaded = await this.loadMore();
                if (generation !== this.generation())
                    return;
                if (!loaded)
                    break;
            }
            this.status.set('ready');
            this.loadSiblings(generation);
            // A search naming tags makes them the "last searched" ones.
            if (/(^|[\s,-])(t|tag):/i.test(route.q))
                this.sidebar.refresh();
            afterNextRender(() => restorePosition(restore), { injector: this.injector });
            const reveal = this.revealing;
            this.revealing = null;
            if (reveal !== null && !route.q && parentPath(reveal) === route.path)
                void this.show(reveal, generation);
        } catch (error) {
            if (generation !== this.generation())
                return;
            this.error.set(error instanceof ApiError ? error : new ApiError(0, String(error)));
            this.status.set('error');
        }
    }

    /**
     * Lists the folder of the file `path` and shows the file there (a quick link): media opens in the viewer, other
     * files are scrolled to and highlighted. Pages of a big folder are loaded until the file is found (`REVEAL_LIMIT`).
     */
    reveal(path: string): void {
        const route = this.route();
        if (!route.q && route.path === parentPath(path) && this.status() === 'ready') {
            void this.show(path, this.generation());
            return;
        }
        this.revealing = path;
        this.navigate({ path: parentPath(path), q: '' });
    }

    private async show(path: string, generation: number): Promise<void> {
        while (!this.items().some(item => item.path === path) && this.hasMore() && this.items().length < REVEAL_LIMIT) {
            if (!await this.loadMore() || generation !== this.generation())
                return;
        }
        const item = this.items().find(candidate => candidate.path === path);
        if (generation !== this.generation())
            return;
        if (!item) {
            this.toasts.show(this.hasMore()
                ? `“${baseName(path)}” is not among the first ${REVEAL_LIMIT.toLocaleString()} items of this folder: scroll down to find it.`
                : `“${baseName(path)}” is not in this folder any more.`);
            return;
        }
        clearTimeout(this.revealTimer);
        this.revealed.set(path);
        this.revealTimer = setTimeout(() => this.revealed.set(null), REVEAL_HIGHLIGHT);
        afterNextRender(() => {
            const element = [...document.querySelectorAll('sfe-listing [data-path]')].find(candidate => candidate.getAttribute('data-path') === path);
            element?.scrollIntoView?.({ block: 'center' });
            if (MEDIA_KINDS.has(item.kind))
                this.dialogs.viewer()?.open(item);
        }, { injector: this.injector });
    }

    /** Fetches the next page (once at a time) and appends it. Resolves false when nothing more is available. */
    loadMore(): Promise<boolean> {
        if (!this.hasMore())
            return Promise.resolve(false);
        if (this.loading)
            return this.loading;

        const generation = this.generation();
        this.loading = this.fetchPage(this.listing())
            .then(page => {
                if (generation !== this.generation())
                    return false;
                this.accept(page);
                return true;
            })
            .catch((error: Error) => {
                if (generation !== this.generation())
                    return false;   // cancelled by navigating away
                this.toasts.error(`Could not load more items: ${error.message}`);
                this.hasMore.set(false);
                return false;
            })
            .finally(() => {
                if (generation === this.generation())
                    this.loading = null;
            });
        return this.loading;
    }

    /* ---------- Selection ---------- */

    isSelected(path: string): boolean {
        return this.selection().has(path);
    }

    /** Selects or deselects a file; with `range`, every file between the last one clicked and this one too. */
    select(item: LibraryItem, selected: boolean, range = false): void {
        let items = [item];
        if (range && this.anchor && this.anchor !== item.path) {
            const files = this.files();
            const from = files.findIndex(file => file.path === this.anchor);
            const to = files.findIndex(file => file.path === item.path);
            if (from >= 0 && to >= 0)
                items = files.slice(Math.min(from, to), Math.max(from, to) + 1);
        }
        const selection = new Set(this.selection());
        for (const file of items) {
            if (selected)
                selection.add(file.path);
            else
                selection.delete(file.path);
        }
        this.anchor = item.path;
        this.selection.set(selection);
    }

    selectAll(): void {
        this.selection.set(new Set(this.files().map(file => file.path)));
    }

    clearSelection(): void {
        if (!this.selection().size)
            return;
        this.anchor = null;
        this.selection.set(new Set());
    }

    selectedFiles(): LibraryItem[] {
        const selection = this.selection();
        return this.files().filter(item => selection.has(item.path));
    }

    /* ---------- Tags of the listed files, kept in step with the tag dialogs ---------- */

    applyFileTags(files: FileTags[]): void {
        const byPath = new Map(files.map(file => [file.path, file.tags]));
        // Tagging indexes the files it changes.
        this.updateItems(item => byPath.has(item.path) ? { ...item, tags: byPath.get(item.path), indexed: item.indexed === false || item.indexed } : item);
        this.sidebar.refresh();
    }

    applyTagChanged(tag: TagDetails): void {
        const summary: TagSummary = { id: tag.id, name: tag.name, displayName: tag.displayName, color: tag.color, isCategory: tag.isCategory, isHidden: tag.isHidden };
        // The display name of tags disambiguated by this one may change too: their chips are refreshed on next load.
        this.updateItems(item => item.tags?.some(t => t.id === tag.id)
            ? { ...item, tags: item.tags.map(t => t.id === tag.id ? summary : t) }
            : item);
        this.sidebar.refresh();
    }

    applyTagDeleted(id: number): void {
        this.updateItems(item => item.tags?.some(t => t.id === id) ? { ...item, tags: item.tags.filter(t => t.id !== id) } : item);
        this.sidebar.refresh();
    }

    /**
     * After tagging every file of a folder or every match of a search (ScopeTagsDialog): the listed files of that scope
     * get the change too, without loading the listing again (which would lose the scroll position).
     */
    applyScopeTags(scope: TagScope, change: { add?: TagSummary; remove?: number }): void {
        const route = this.route();
        // A search scope is the listing's own search: every file listed is one of its matches.
        const inScope = scope.query
            ? () => route.q.trim() === scope.query && route.path === scope.path
            : (item: LibraryItem) => {
                const folder = parentPath(item.path);
                return folder === scope.path || scope.recursive && (scope.path === '' || folder.startsWith(`${scope.path}/`));
            };
        this.updateItems(item => {
            if (item.kind === 'directory' || !inScope(item))
                return item;
            // A folder scope is indexed before it is tagged.
            if (item.indexed === false)
                item = { ...item, indexed: true };
            const tags = item.tags ?? [];
            if (change.add && !tags.some(t => t.id === change.add!.id))
                return { ...item, tags: [...tags, change.add] };
            if (change.remove !== undefined && tags.some(t => t.id === change.remove))
                return { ...item, tags: tags.filter(t => t.id !== change.remove) };
            return item;
        });
        this.sidebar.refresh();
    }

    /** The tags of a viewed file become the sidebar's "last viewed" tags. */
    reportView(item: LibraryItem): void {
        if (!item.tags?.length)
            return;
        this.api.fileViewed(item.path)
            .then(() => this.sidebar.refresh())
            .catch(() => { /* not worth bothering the user */ });
    }

    private updateItems(change: (item: LibraryItem) => LibraryItem): void {
        this.items.update(items => {
            let changed = false;
            const updated = items.map(item => {
                const next = change(item);
                changed ||= next !== item;
                return next;
            });
            return changed ? updated : items;
        });
    }

    /* ---------- Pages ---------- */

    /** Items loaded at a time: the search and browse page sizes are separate settings. */
    pageSize(): number {
        const config = this.config.value;
        return this.route().q ? config.searchPageSize : config.pageSize;
    }

    /** The first page, or the one following `previous` (the last page received). */
    private fetchPage(previous: ListingPage | null, limit = this.pageSize()): Promise<ListingPage> {
        const { path, q, desc } = this.route();
        const sort = this.sort();
        const signal = this.requests.signal;
        if (q)
            return this.api.search({ q, path, after: previous && 'next' in previous ? previous.next : null, limit, sort, desc }, signal);
        const offset = previous && 'offset' in previous ? previous.offset + previous.items.length : 0;
        return this.api.browse({ path, offset, limit, sort, desc }, signal);
    }

    private accept(page: ListingPage): void {
        this.listing.set(page);
        this.items.update(items => [...items, ...page.items]);
        this.hasMore.set('hasMore' in page ? page.hasMore : page.offset + page.items.length < page.total);
        // A search counts its matches on its first page only.
        if (!('hasMore' in page))
            this.total.set({ count: page.total, capped: false });
        else if (page.total != null)
            this.total.set({ count: page.total, capped: Boolean(page.totalCapped) });
    }

    /**
     * Fetched after the listing, which comes first: the siblings list the parent folder, which can be a large one.
     * Asked in the sort of the current folder, so "next" is the folder the parent lists after this one.
     */
    private loadSiblings(generation: number): void {
        const { path, q, desc } = this.route();
        if (q || !path)
            return;
        this.api.siblings({ path, sort: this.sort(), desc }, this.requests.signal)
            .then(siblings => {
                if (generation === this.generation())
                    this.siblings.set(siblings);
            })
            .catch(() => { /* the previous/next buttons stay disabled */ });
    }
}

function readView(): View {
    const view = readPreference(VIEW_KEY);
    return VIEWS.includes(view as View) ? view as View : 'grid';
}
