import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api, ApiError } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatDate, formatSize } from '../../core/format';
import { ArchiveAnswer, ArchiveItem, ArchiveListing, LibraryItem } from '../../core/models';
import { pathToken } from '../../core/paths';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { VirtualRows } from '../virtual-rows';

/** The first page of a folder, then larger ones while scrolling (keyset pages: one indexed range each on the server). */
const FIRST_PAGE = 200;
const NEXT_PAGE = 1000;
/** Rows left below the screen when the next page is asked for. */
const AHEAD = 60;
/** How often the reading of a big archive's list is asked about. */
const READING_POLL_MS = 600;
const ROW_HEIGHT = 32;

type ArchiveState = 'loading' | 'reading' | 'ready' | 'failed';

/** What a folder shows: its first page (totals, trail, flags) and the rows loaded so far. */
interface Shown {
    listing: ArchiveListing;
    rows: ArchiveItem[];
    next: string | null;
}

/**
 * An archive (zip, 7z, rar, tar), explored without being unpacked (agents/docs/features/archive-reading.md): a folder
 * at a time from the server's catalog of it, its rows drawn only where they show (`VirtualRows`) and loaded in pages
 * as they scroll in, however many the folder holds; the way to the folder collapsed when deep. Files open in a tab
 * (what the browser shows safely) or download, with the right to download.
 */
@Component({
    selector: 'sfe-archive-view',
    imports: [IconComponent, ContextMenuDirective, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view archive-view',
        'data-owns-keys': '',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="text-toolbar archive-toolbar" role="toolbar" aria-label="Archive" sfeOverflowRow="Archive">
            <button type="button" class="icon-button" data-up [sfeOverflowItem]="null" overflowStay title="Up to the folder above (Backspace)" aria-label="Up"
                    [disabled]="!parent()" (click)="goUp()"><svg sfeIcon="folder-up"></svg></button>
            <nav class="archive-trail" aria-label="Folders" [sfeOverflowItem]="null" overflowStay>
                <button type="button" class="archive-crumb" data-crumb-root [title]="'The top of ' + item().name" (click)="enter(null)">{{ item().name }}</button>
                @if (shown()?.listing?.trail; as trail) {
                    @if (trail.skipped > 0) {
                        <span class="archive-crumb skipped" data-skipped [title]="trail.skipped.toLocaleString() + ' folders in between'">…</span>
                    }
                    @for (crumb of trail.last; track crumb.id; let last = $last) {
                        <span class="archive-separator" aria-hidden="true">›</span>
                        <button type="button" class="archive-crumb" [class.current]="last" [attr.aria-current]="last ? 'location' : null"
                                [title]="last ? 'This folder' : 'Open ' + crumb.name" (click)="enter(crumb.id)">{{ crumb.name }}</button>
                    }
                }
            </nav>
            <input #goTo type="search" class="json-filter archive-go" data-go [sfeOverflowItem]="null" overflowStay placeholder="Go to folder" aria-label="Go to a folder of the archive"
                   title="A folder's path inside the archive (folders separated by /), then Enter" (keydown.enter)="goToPath(goTo.value)" />
            <button class="icon-button" sfeOverflowMore></button>
        </div>
        @switch (state()) {
            @case ('loading') {
                <p class="text-message">Loading…</p>
            }
            @case ('reading') {
                <p class="text-message" data-reading role="status">
                    Reading the list of entries…{{ read() > 0 ? ' ' + read().toLocaleString() + ' so far' : '' }}
                </p>
            }
            @case ('failed') {
                <div class="text-message" data-failed>
                    <p>{{ message() }}</p>
                    @if (archiveDownload(); as url) {
                        <p><a class="button" [href]="url" download><svg sfeIcon="download"></svg> Download the archive</a></p>
                    }
                </div>
            }
        }
        @if (shown(); as current) {
            @if (current.listing.needsPassword) {
                <p class="archive-note" data-password role="status"><svg sfeIcon="lock"></svg> This archive is protected by a password: its list can't be read without it.</p>
            } @else if (!current.listing.complete) {
                <p class="archive-note" data-stopped role="status">Only part of the list is shown: {{ current.listing.stopped }}</p>
            }
            @if (current.listing.leftOut > 0) {
                <p class="archive-note" data-left-out>{{ current.listing.leftOut.toLocaleString() }} {{ current.listing.leftOut === 1 ? 'entry is' : 'entries are' }} not listed: {{ current.listing.leftOut === 1 ? 'its name leads' : 'their names lead' }} out of the archive, or {{ current.listing.leftOut === 1 ? 'it is' : 'they are' }} nested too deep.</p>
            }
        }
        <div #scroller class="archive-scroller" role="grid" tabindex="0" [hidden]="state() !== 'ready'" [attr.aria-label]="item().name" [attr.aria-rowcount]="count() + 1">
            <div class="archive-row archive-head" role="row">
                <span role="columnheader">Name</span>
                <span role="columnheader" class="archive-size">Size</span>
                <span role="columnheader" class="archive-packed">Packed</span>
                <span role="columnheader" class="archive-modified">Modified</span>
            </div>
            @if (state() === 'ready' && count() === 0) {
                <p class="text-message" data-empty>This folder is empty.</p>
            }
            <div class="archive-body" [style.height.px]="virtual.total()">
                @for (row of virtual.items(); track row.key) {
                    @let entry = rows()[row.index];
                    @if (entry) {
                        <div class="archive-row" role="row" [attr.data-kind]="entry.kind" [attr.data-index]="row.index" [class.selected]="selected() === row.index"
                             [style.transform]="'translateY(' + row.start + 'px)'" [style.height.px]="rowHeight" [sfeContextMenu]="menuOf(entry)"
                             (click)="select(row.index)" (dblclick)="activate(entry)">
                            <span class="archive-name" role="gridcell">
                                <svg [sfeIcon]="iconOf(entry)"></svg>
                                @if (entry.kind === 'folder') {
                                    <button type="button" class="archive-open" [title]="'Open ' + entry.name" (click)="enter(entry.id); $event.stopPropagation()">{{ entry.name }}</button>
                                } @else if (entry.kind === 'file' && canDownload() && !entry.encrypted) {
                                    <a class="archive-open" [href]="entryUrl(entry, false)" target="_blank" rel="noopener" [title]="'Open ' + entry.name + ' in a new tab'">{{ entry.name }}</a>
                                } @else {
                                    <span class="archive-open" [title]="titleOf(entry)">{{ entry.name }}</span>
                                }
                                @if (entry.encrypted) {
                                    <svg class="archive-lock" sfeIcon="lock" aria-label="Protected by a password"></svg>
                                }
                            </span>
                            <span class="archive-size" role="gridcell" [title]="sizeTitle(entry)">{{ sizeOf(entry) }}</span>
                            <span class="archive-packed" role="gridcell">{{ entry.kind === 'file' && entry.packed > 0 ? bytes(entry.packed) : '' }}</span>
                            <span class="archive-modified" role="gridcell">{{ date(entry.modified) }}</span>
                        </div>
                    } @else {
                        <div class="archive-row placeholder" role="row" aria-busy="true" [style.transform]="'translateY(' + row.start + 'px)'" [style.height.px]="rowHeight">
                            <span class="archive-name" role="gridcell">…</span>
                        </div>
                    }
                }
            </div>
        </div>
        @if (shown(); as current) {
            <p class="text-status" data-counts>{{ countsOf(current.listing) }}</p>
        }
    `,
})
export class ArchiveViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat>();

    private readonly api = inject(Api);
    private readonly host = inject(FILE_VIEW_HOST);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');

    protected readonly state = signal<ArchiveState>('loading');
    protected readonly read = signal(0);
    protected readonly message = signal('');
    protected readonly shown = signal<Shown | null>(null);
    protected readonly rows = computed(() => this.shown()?.rows ?? []);
    protected readonly selected = signal(-1);
    protected readonly rowHeight = ROW_HEIGHT;
    protected readonly virtual = new VirtualRows(() => this.scroller()?.nativeElement ?? null, ROW_HEIGHT);

    /** The folder's rows in all (folders and entries right inside it), loaded or not. */
    protected readonly count = computed(() => {
        const folder = this.shown()?.listing.folder;
        return folder ? (folder.folders ?? 0) + (folder.files ?? 0) : 0;
    });

    /** The folder above: from the trail (its last crumb is this folder); the root has none. */
    protected readonly parent = computed<number | null | undefined>(() => {
        const trail = this.shown()?.listing.trail;
        if (!trail || trail.last.length === 0)
            return undefined;
        return trail.last.length >= 2 ? trail.last[trail.last.length - 2].id : null;
    });

    protected readonly canDownload = computed(() => this.host.can(Rights.download));
    protected readonly archiveDownload = computed(() => this.canDownload() ? this.config.urls()?.download(this.item().path) ?? null : null);

    private abort: AbortController | null = null;
    private loadingMore = false;

    constructor() {
        afterNextRender(() => this.virtual.mount());
        inject(DestroyRef).onDestroy(() => {
            this.abort?.abort();
            this.virtual.destroy();
        });
        effect(() => {
            this.item();
            untracked(() => void this.open({}));
        });
        effect(() => this.virtual.setCount(this.count()));
        // More rows as they scroll in: the next page when the last rows drawn near the end of those loaded.
        effect(() => {
            const items = this.virtual.items();
            const shown = this.shown();
            if (!shown?.next || items.length === 0)
                return;
            if (items[items.length - 1].index >= shown.rows.length - AHEAD)
                untracked(() => void this.loadMore());
        });
    }

    protected enter(folder: number | null): void {
        void this.open({ folder });
    }

    protected goUp(): void {
        const parent = this.parent();
        if (parent !== undefined)
            this.enter(parent);
    }

    protected goToPath(path: string): void {
        void this.open({ at: path.trim().replace(/^\/+|\/+$/g, '') });
    }

    protected select(index: number): void {
        this.selected.set(index);
    }

    protected activate(entry: ArchiveItem): void {
        if (entry.kind === 'folder')
            this.enter(entry.id);
        else if (entry.kind === 'file' && this.canDownload() && !entry.encrypted)
            window.open(this.entryUrl(entry, false), '_blank', 'noopener');
    }

    protected onKeydown(event: KeyboardEvent): void {
        if ((event.target as HTMLElement).closest('input'))
            return;
        const rows = this.rows();
        if (event.key === 'Backspace' && this.parent() !== undefined) {
            event.preventDefault();
            this.goUp();
        } else if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && rows.length > 0) {
            event.preventDefault();
            const next = Math.max(0, Math.min(rows.length - 1, this.selected() + (event.key === 'ArrowDown' ? 1 : -1)));
            this.selected.set(next);
            this.virtual.scrollTo(next);
        } else if (event.key === 'Enter' && rows[this.selected()]) {
            event.preventDefault();
            this.activate(rows[this.selected()]);
        }
    }

    /** The address of an entry's data: inline in a tab for what the browser shows safely, else (or `download`) a download. */
    protected entryUrl(entry: ArchiveItem, download: boolean): string {
        return `api/files/explore/entry/${pathToken(this.item().path)}/${entry.id}/${encodeURIComponent(entry.name)}${download ? '?download=true' : ''}`;
    }

    protected menuOf(entry: ArchiveItem): MenuTarget | null {
        const entries: MenuEntry[] = [];
        if (entry.kind === 'folder')
            entries.push({ id: 'open', label: 'Open', icon: 'folder', title: 'Show this folder of the archive', run: () => this.enter(entry.id) });
        else if (entry.kind === 'file' && this.canDownload() && !entry.encrypted) {
            entries.push({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'Open it in a new tab', href: this.entryUrl(entry, false), newTab: true });
            entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save it from the archive', href: this.entryUrl(entry, true) });
        }
        return entries.length ? { kind: 'file', key: `archive:${this.item().path}:${entry.id}`, name: entry.name, entries: () => entries } : null;
    }

    protected iconOf(entry: ArchiveItem): string {
        if (entry.kind === 'folder')
            return 'folder';
        if (entry.kind !== 'file')
            return 'external';
        const extension = entry.name.slice(entry.name.lastIndexOf('.') + 1).toLowerCase();
        if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'avif', 'bmp'].includes(extension))
            return 'image';
        if (['mp4', 'webm', 'mkv', 'mov', 'avi'].includes(extension))
            return 'video';
        if (['mp3', 'flac', 'ogg', 'wav', 'm4a', 'opus'].includes(extension))
            return 'audio';
        return 'file';
    }

    protected titleOf(entry: ArchiveItem): string {
        if (entry.encrypted)
            return 'Protected by a password';
        if (entry.kind === 'link')
            return 'A link: listed, never created when extracting';
        if (entry.kind === 'other')
            return 'A device or pipe: listed, never created when extracting';
        return entry.name;
    }

    protected sizeOf(entry: ArchiveItem): string {
        if (entry.kind === 'folder')
            return `${(entry.allFiles ?? 0).toLocaleString()} ${entry.allFiles === 1 ? 'file' : 'files'}`;
        return entry.kind === 'file' ? formatSize(entry.size) : '';
    }

    protected sizeTitle(entry: ArchiveItem): string {
        return entry.kind === 'folder' ? `${formatSize(entry.size)} in all` : `${entry.size.toLocaleString()} bytes`;
    }

    protected bytes(bytes: number): string {
        return formatSize(bytes);
    }

    protected date(value: string | null): string {
        return formatDate(value);
    }

    protected countsOf(listing: ArchiveListing): string {
        const folder = listing.folder;
        const parts = [`${(folder.folders ?? 0).toLocaleString()} ${folder.folders === 1 ? 'folder' : 'folders'}`,
            `${(folder.files ?? 0).toLocaleString()} ${folder.files === 1 ? 'file' : 'files'} here`,
            `${formatSize(folder.size)} in ${(folder.allFiles ?? 0).toLocaleString()} ${folder.allFiles === 1 ? 'file' : 'files'} below`];
        return `${parts.join(' · ')} · ${listing.format.toUpperCase()}`;
    }

    /** Shows a folder (or the root): asks until the archive's list is read, then its first page. */
    private async open(where: { folder?: number | null; at?: string | null }): Promise<void> {
        this.abort?.abort();
        const abort = this.abort = new AbortController();
        this.loadingMore = false;
        if (!this.shown())
            this.state.set('loading');
        try {
            for (;;) {
                const answer = await this.api.exploreArchive(this.item().path, { ...where, limit: FIRST_PAGE }, abort.signal);
                if (abort.signal.aborted)
                    return;
                if (isListing(answer)) {
                    this.shown.set({ listing: answer, rows: answer.items, next: answer.next });
                    this.selected.set(-1);
                    this.state.set('ready');
                    this.scroller().nativeElement.scrollTop = 0;
                    return;
                }
                if (!isReading(answer)) {
                    this.fail(answer.detail ?? 'This file can\'t be explored.');
                    return;
                }
                this.state.set('reading');
                this.read.set(answer.read);
                await pause(READING_POLL_MS, abort.signal);
            }
        } catch (error) {
            if (abort.signal.aborted)
                return;
            this.fail(error instanceof ApiError && error.status === 404 ? 'There is no such folder in the archive.' : error instanceof Error ? error.message : String(error));
        }
    }

    private async loadMore(): Promise<void> {
        const shown = this.shown();
        if (!shown?.next || this.loadingMore)
            return;
        this.loadingMore = true;
        const abort = this.abort;
        try {
            const answer = await this.api.exploreArchive(this.item().path, { folder: shown.listing.folder.id, after: shown.next, limit: NEXT_PAGE }, abort?.signal);
            if (abort?.signal.aborted || this.shown() !== shown || !isListing(answer))
                return;
            this.shown.set({ listing: shown.listing, rows: [...shown.rows, ...answer.items], next: answer.next });
        } catch {
            // The next scroll asks again.
        } finally {
            this.loadingMore = false;
        }
    }

    /** Nothing shown yet: the view says why. A folder that isn't there (Go to): the one shown stays, and a toast says so. */
    private fail(message: string): void {
        if (this.shown()) {
            this.state.set('ready');
            this.toasts.error(message);
            return;
        }
        this.message.set(message);
        this.state.set('failed');
    }
}

function isListing(answer: ArchiveAnswer): answer is ArchiveListing {
    return 'items' in answer;
}

function isReading(answer: ArchiveAnswer): answer is { read: number } {
    return 'read' in answer;
}

/** Waits `ms`, longer while the tab is hidden (nothing is asked then: battery). */
function pause(ms: number, signal: AbortSignal): Promise<void> {
    return new Promise(resolve => {
        const done = () => {
            document.removeEventListener('visibilitychange', check);
            signal.removeEventListener('abort', done);
            resolve();
        };
        const check = () => {
            if (!document.hidden)
                done();
        };
        signal.addEventListener('abort', done, { once: true });
        setTimeout(() => document.hidden ? document.addEventListener('visibilitychange', check) : done(), ms);
    });
}
