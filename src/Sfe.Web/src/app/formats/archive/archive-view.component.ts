import { NgComponentOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, input, signal, Type, untracked, viewChild } from '@angular/core';
import { Api, ApiError } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatDate, formatSize } from '../../core/format';
import { ArchiveAnswer, ArchiveItem, ArchiveListing, LibraryItem } from '../../core/models';
import { pathToken } from '../../core/paths';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { Dialogs } from '../../library/dialogs.service';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { IconComponent } from '../../shared/icon.component';
import { FILE_SOURCE, FileSource } from '../file-source';
import { extensionOf, FILE_VIEW_HOST, FileFormat, FileViewHost, formatOf } from '../formats';
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

/** A search's results so far: `next` goes on; `searching` while a page is asked for; `folder`: in that folder only. */
interface Found {
    query: string;
    folder: number | null;
    rows: ArchiveItem[];
    next: string | null;
    searching: boolean;
}

/** A search starts this long after the last key typed, with this many characters at least. */
const FIND_DELAY_MS = 300;
const FIND_MIN = 2;

/** The formats an entry is shown with in place (read only, read whole); others open in a tab (videos, audio) or download. */
const IN_PLACE: ReadonlySet<string> = new Set(['text', 'prose', 'markdown', 'json', 'csv', 'subtitles', 'pdf']);

/** An entry shown in place of the list: a picture (`format` null) or a format's view of it, reading it from the archive. */
interface Viewing {
    entry: ArchiveItem;
    /** Its row (previous / next go from it). */
    index: number;
    item: LibraryItem;
    format: FileFormat | null;
    component: Type<unknown> | null;
    injector: Injector | null;
}

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
    imports: [NgComponentOutlet, IconComponent, ContextMenuDirective, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view archive-view',
        'data-owns-keys': '',
        '(keydown)': 'onKeydown($event)',
    },
    template: `
        <div class="text-toolbar archive-toolbar" role="toolbar" aria-label="Archive" sfeOverflowRow="Archive">
            <button type="button" class="icon-button" data-up [sfeOverflowItem]="null" overflowStay title="Up to the folder above (Backspace)" aria-label="Up"
                    [disabled]="parent() === undefined" (click)="goUp()"><svg sfeIcon="folder-up"></svg></button>
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
            <input #find type="search" class="json-filter archive-find" data-find [sfeOverflowItem]="null" overflowStay
                   [placeholder]="findHere() ? 'Find in this folder' : 'Find in archive'" [attr.aria-label]="findHere() ? 'Find in this folder by name' : 'Find in the archive by name'"
                   [title]="(findHere() ? 'Folders and files right inside this folder' : 'Folders and files of the whole archive') + ' whose name holds what is typed (Esc: back to the folder)'"
                   (input)="findTyped(find.value)" (keydown.escape)="endFind()" />
            <button type="button" class="icon-button" data-find-here [sfeOverflowItem]="null" overflowStay [class.active]="findHere()" [attr.aria-pressed]="findHere()"
                    title="Find in this folder only" aria-label="Find in this folder only" (click)="toggleFindHere(find.value)"><svg sfeIcon="folder"></svg></button>
            @if (canExtract() && shown()) {
                <button type="button" class="button archive-extract" data-extract-shown [sfeOverflowItem]="extractEntry" [title]="extractTitle()" (click)="extractShown()">
                    <svg sfeIcon="extract"></svg><span>Extract…</span>
                </button>
            }
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
            @if (asking()) {
                <form class="archive-password" data-password (submit)="unlockWith(passwordBox.value); $event.preventDefault()">
                    <p class="archive-password-why">
                        <svg sfeIcon="lock"></svg>
                        {{ current.listing.needsPassword ? lockedList : lockedFiles }}
                        @if (current.listing.format === 'zip') {
                            <span class="archive-password-hint">A zip's password protects only the files' content: their names stay readable.</span>
                        }
                    </p>
                    <div class="archive-password-row">
                        <input #passwordBox [type]="showPassword() ? 'text' : 'password'" class="json-filter" data-password-box autocomplete="off" aria-label="Password"
                               placeholder="Password" [disabled]="unlocking()" />
                        <label class="archive-password-show" title="Show what is typed"><input type="checkbox" [checked]="showPassword()" (change)="showPassword.set(!showPassword())" /> Show</label>
                        <button type="submit" class="button primary" data-unlock [disabled]="unlocking()" title="Open the archive with this password">Open</button>
                        @if (!current.listing.needsPassword) {
                            <button type="button" class="button" title="Keep browsing without the password" (click)="asking.set(false)">Not now</button>
                        }
                    </div>
                    @if (passwordMessage(); as problem) {
                        <p class="archive-password-problem" role="alert" data-password-problem>{{ problem }}</p>
                    }
                </form>
            } @else if (current.listing.protected && !current.listing.unlocked) {
                <p class="archive-note" data-protected><svg sfeIcon="lock"></svg> Some files are protected by a password.
                    <button type="button" class="button" data-ask-password title="Give the archive's password to open them" (click)="asking.set(true)">Enter the password</button></p>
            }
            @if (!current.listing.complete && !current.listing.needsPassword) {
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
            @if (found(); as results) {
                @if (!results.searching && !results.next && results.rows.length === 0) {
                    <p class="text-message" data-found-none>No name {{ results.folder === null ? 'in this archive' : 'in this folder' }} holds “{{ results.query }}”.</p>
                }
            } @else if (state() === 'ready' && count() === 0 && !shown()?.listing?.needsPassword) {
                <p class="text-message" data-empty>This folder is empty.</p>
            }
            <div class="archive-body" [style.height.px]="virtual.total()">
                @for (row of virtual.items(); track row.key) {
                    @let entry = rows()[row.index];
                    @if (entry) {
                        <div class="archive-row" role="row" [attr.data-kind]="entry.kind" [attr.data-index]="row.index" [class.selected]="selected() === row.index"
                             [style.transform]="'translateY(' + row.start + 'px)'" [style.height.px]="rowHeight" [sfeContextMenu]="menuOf(entry)"
                             (click)="select(row.index)" (dblclick)="activate(entry, row.index)">
                            <span class="archive-name" role="gridcell">
                                <svg [sfeIcon]="iconOf(entry)"></svg>
                                @if (entry.kind === 'folder') {
                                    <button type="button" class="archive-open" [title]="'Open ' + entry.name" (click)="enter(entry.id); $event.stopPropagation()">{{ entry.name }}</button>
                                } @else if (entry.kind === 'file' && canDownload() && (!entry.encrypted || unlock())) {
                                    <a class="archive-open" [href]="entryUrl(entry, false)" target="_blank" rel="noopener" [title]="inPlace(entry) ? 'Open ' + entry.name : 'Open ' + entry.name + ' in a new tab'"
                                       (click)="openFromLink($event, entry, row.index)">{{ entry.name }}</a>
                                } @else if (entry.kind === 'file' && canDownload()) {
                                    <button type="button" class="archive-open" [title]="'Give the password to open ' + entry.name" (click)="asking.set(true); $event.stopPropagation()">{{ entry.name }}</button>
                                } @else {
                                    <span class="archive-open" [title]="titleOf(entry)">{{ entry.name }}</span>
                                }
                                @if (entry.encrypted) {
                                    <svg class="archive-lock" sfeIcon="lock" aria-label="Protected by a password"></svg>
                                }
                                @if (found() && entry.where !== undefined && entry.where !== null) {
                                    <button type="button" class="archive-where" data-where [title]="'Show the folder it is in: ' + (entry.where || item().name)"
                                            (click)="showWhere(entry); $event.stopPropagation()">in {{ whereOf(entry) }}</button>
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
        @if (viewing(); as shown) {
            <div #entryView class="archive-entry" data-entry tabindex="-1" role="region" [attr.aria-label]="shown.entry.name">
                <div class="text-toolbar archive-toolbar archive-entry-bar" role="toolbar" aria-label="File of the archive" sfeOverflowRow="File of the archive">
                    <button type="button" class="icon-button" data-back [sfeOverflowItem]="null" overflowStay title="Back to the folder (Esc)" aria-label="Back to the folder"
                            (click)="closeEntry()"><svg sfeIcon="left"></svg></button>
                    <span class="archive-entry-name" [sfeOverflowItem]="null" overflowStay [title]="shown.entry.name">{{ shown.entry.name }}</span>
                    <button type="button" class="icon-button" data-previous [sfeOverflowItem]="previousEntry" overflowStay title="The file before" aria-label="The file before"
                            [disabled]="neighbour(-1) === null" (click)="step(-1)"><svg sfeIcon="up"></svg></button>
                    <button type="button" class="icon-button" data-next [sfeOverflowItem]="nextEntry" overflowStay title="The file after" aria-label="The file after"
                            [disabled]="neighbour(1) === null" (click)="step(1)"><svg sfeIcon="down"></svg></button>
                    <a class="icon-button" data-new-tab [sfeOverflowItem]="newTabEntry" [href]="entryUrl(shown.entry, false)" target="_blank" rel="noopener"
                       title="Open it in a new tab" aria-label="Open it in a new tab"><svg sfeIcon="external"></svg></a>
                    <a class="icon-button" data-download [sfeOverflowItem]="downloadEntry" [href]="entryUrl(shown.entry, true)" title="Save it from the archive" aria-label="Download"><svg sfeIcon="download"></svg></a>
                    <button class="icon-button" sfeOverflowMore></button>
                </div>
                <div class="archive-entry-content">
                    @if (!shown.format) {
                        <img class="archive-entry-picture" data-entry-picture [src]="entryUrl(shown.entry, false)" [alt]="shown.entry.name" decoding="async" />
                    } @else if (shown.component) {
                        <ng-container *ngComponentOutlet="shown.component; inputs: { item: shown.item, format: shown.format }; injector: shown.injector ?? undefined" />
                    } @else {
                        <p class="text-message">Loading…</p>
                    }
                </div>
            </div>
        }
        @if (found(); as results) {
            <p class="text-status" data-found role="status">{{ foundStatus(results) }}</p>
        } @else if (shown(); as current) {
            @if (!current.listing.needsPassword) {
                <p class="text-status" data-counts>{{ countsOf(current.listing) }}</p>
            }
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
    private readonly dialogs = inject(Dialogs);
    private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');
    private readonly findBox = viewChild<ElementRef<HTMLInputElement>>('find');
    private readonly entryView = viewChild<ElementRef<HTMLElement>>('entryView');
    private readonly injector = inject(Injector);

    protected readonly state = signal<ArchiveState>('loading');
    protected readonly read = signal(0);
    protected readonly message = signal('');
    protected readonly shown = signal<Shown | null>(null);
    /** A search of the whole archive by name, while one is typed: its results replace the folder's rows. */
    protected readonly found = signal<Found | null>(null);
    /** Find looks only right inside the folder shown (else in the whole archive). */
    protected readonly findHere = signal(false);
    protected readonly rows = computed(() => this.found()?.rows ?? this.shown()?.rows ?? []);
    protected readonly selected = signal(-1);
    protected readonly rowHeight = ROW_HEIGHT;
    protected readonly virtual = new VirtualRows(() => this.scroller()?.nativeElement ?? null, ROW_HEIGHT);

    /** The folder's rows in all (folders and entries right inside it), loaded or not. */
    protected readonly count = computed(() => {
        const found = this.found();
        if (found)
            return found.rows.length;
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
    /** Extract… is offered: the right (to view, download and extract), and the dialog of the listing page. */
    protected readonly canExtract = computed(() => this.canDownload() && this.host.can(Rights.viewFiles) && this.config.can(Rights.extractFiles) && Boolean(this.dialogs.extract()));
    protected readonly extractTitle = computed(() => this.parent() === undefined
        ? 'Extract the whole archive into a folder of the library, picked next'
        : 'Extract this folder of the archive into a folder of the library, picked next');
    protected readonly extractEntry = (): MenuEntry => ({ id: 'extract', label: 'Extract…', icon: 'extract', title: this.extractTitle(), run: () => this.extractShown() });

    /** An entry shown in place of the list (Back: the list as it was). */
    protected readonly viewing = signal<Viewing | null>(null);
    protected readonly previousEntry = (): MenuEntry => ({ id: 'previous', label: 'The file before', icon: 'up', title: 'The file before', disabled: this.neighbour(-1) === null, run: () => this.step(-1) });
    protected readonly nextEntry = (): MenuEntry => ({ id: 'next', label: 'The file after', icon: 'down', title: 'The file after', disabled: this.neighbour(1) === null, run: () => this.step(1) });
    protected readonly newTabEntry = (): MenuEntry => ({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'Open it in a new tab', href: this.entryUrl(this.viewing()!.entry, false), newTab: true });
    protected readonly downloadEntry = (): MenuEntry => ({ id: 'download', label: 'Download', icon: 'download', title: 'Save it from the archive', href: this.entryUrl(this.viewing()!.entry, true) });
    private readonly components = new Map<string, Promise<Type<unknown>>>();

    /** The unlock the password given answered: kept by this view only (never the password), sent with its requests. */
    protected readonly unlock = signal<string | null>(null);
    /** The password form shows: the list is encrypted, or a protected file was asked for. */
    protected readonly asking = signal(false);
    protected readonly unlocking = signal(false);
    protected readonly showPassword = signal(false);
    protected readonly passwordMessage = signal<string | null>(null);
    protected readonly lockedList = "This archive is protected by a password: its list can't be read without it.";
    protected readonly lockedFiles = 'Its files are protected by a password.';
    protected readonly archiveDownload = computed(() => this.canDownload() ? this.config.urls()?.download(this.item().path) ?? null : null);

    private abort: AbortController | null = null;
    private loadingMore = false;
    private findAbort: AbortController | null = null;
    private findTimer: ReturnType<typeof setTimeout> | undefined;

    constructor() {
        afterNextRender(() => this.virtual.mount());
        inject(DestroyRef).onDestroy(() => {
            this.abort?.abort();
            this.endFind();
            this.virtual.destroy();
        });
        effect(() => {
            this.item();
            untracked(() => {
                // Another archive: the password given for this one isn't sent with it.
                this.unlock.set(null);
                this.asking.set(false);
                this.passwordMessage.set(null);
                this.viewing.set(null);
                this.endFind();
                void this.open({});
            });
        });
        effect(() => this.virtual.setCount(this.count()));
        // More rows as they scroll in: the next page when the last rows drawn near the end of those loaded.
        effect(() => {
            const items = this.virtual.items();
            const found = this.found();
            if (found) {
                if (found.next && !found.searching && items.length > 0 && items[items.length - 1].index >= found.rows.length - AHEAD)
                    untracked(() => void this.search(found.query, found.next));
                return;
            }
            const shown = this.shown();
            if (!shown?.next || items.length === 0)
                return;
            if (items[items.length - 1].index >= shown.rows.length - AHEAD)
                untracked(() => void this.loadMore());
        });
    }

    protected enter(folder: number | null): void {
        this.viewing.set(null);
        this.endFind();
        void this.open({ folder });
    }

    /** Typed in Find: the search starts after a pause; fewer than two characters, the folder again. */
    protected findTyped(text: string): void {
        clearTimeout(this.findTimer);
        const query = text.trim();
        if (query.length < FIND_MIN) {
            this.stopFind();
            return;
        }
        this.findTimer = setTimeout(() => void this.search(query, null), FIND_DELAY_MS);
    }

    /** This folder only, or the whole archive: what is typed is looked for again at once. */
    protected toggleFindHere(text: string): void {
        this.findHere.update(here => !here);
        clearTimeout(this.findTimer);
        const query = text.trim();
        if (query.length >= FIND_MIN)
            void this.search(query, null);
    }

    /** Back to the folder: the search stops and its box is emptied. */
    protected endFind(): void {
        clearTimeout(this.findTimer);
        const box = this.findBox()?.nativeElement;
        if (box)
            box.value = '';
        this.stopFind();
    }

    private stopFind(): void {
        this.findAbort?.abort();
        this.findAbort = null;
        if (this.found()) {
            this.found.set(null);
            this.selected.set(-1);
        }
    }

    /** A result's folder, opened (by its id: a deep one's path is shortened): the search ends. */
    protected showWhere(entry: ArchiveItem): void {
        this.endFind();
        void this.open(entry.in != null ? { folder: entry.in } : entry.where ? { at: entry.where } : { folder: null });
    }

    /**
     * A page of the search (`after`: the next one, added to those found). A page the server cut short with nothing
     * found goes on at once; more come as the results scroll in.
     */
    private async search(query: string, after: string | null): Promise<void> {
        if (!after) {
            this.findAbort?.abort();
            this.findAbort = new AbortController();
            this.found.set({ query, folder: this.findHere() ? this.shown()?.listing.folder.id ?? null : null, rows: [], next: null, searching: true });
            this.selected.set(-1);
            this.scroller().nativeElement.scrollTop = 0;
        } else {
            this.found.update(found => found && { ...found, searching: true });
        }
        const abort = this.findAbort!;
        const folder = this.found()?.folder ?? null;
        try {
            for (let cursor = after; ;) {
                const answer = await this.api.searchArchive(this.item().path, { q: query, ...(folder === null ? {} : { folder }), after: cursor, unlock: this.unlock() }, abort.signal);
                if (abort.signal.aborted)
                    return;
                if ('read' in answer) {
                    await pause(READING_POLL_MS, abort.signal);
                    continue;
                }
                const found = this.found();
                if (!found || found.query !== query || found.folder !== folder)
                    return;
                this.found.set({ query, folder, rows: [...found.rows, ...answer.items], next: answer.next, searching: answer.items.length === 0 && answer.next !== null });
                if (answer.items.length > 0 || !answer.next)
                    return;
                cursor = answer.next;
            }
        } catch (error) {
            if (abort.signal.aborted)
                return;
            this.found.update(found => found && { ...found, next: null, searching: false });
            this.toasts.error(error instanceof Error ? error.message : String(error));
        }
    }

    /** A result's folder, short: deep down, its last two folders ("…/Day 2/Notes"); the title has it whole. */
    protected whereOf(entry: ArchiveItem): string {
        const parts = entry.where ? entry.where.split('/') : [];
        if (parts.length === 0)
            return this.item().name;
        return parts.length > 3 ? `…/${parts.slice(-2).join('/')}` : entry.where!;
    }

    protected foundStatus(found: Found): string {
        const here = found.folder === null ? '' : ' in this folder';
        const count = `${found.rows.length.toLocaleString()} ${found.rows.length === 1 ? 'name' : 'names'}${here} ${found.rows.length === 1 ? 'holds' : 'hold'} “${found.query}”`;
        if (found.searching)
            return `Searching ${found.folder === null ? 'the archive' : 'this folder'}… ${found.rows.length ? count + ' so far' : ''}`.trim();
        return found.next ? `${count} so far: scroll for more` : count;
    }

    protected goUp(): void {
        const parent = this.parent();
        if (parent !== undefined)
            this.enter(parent);
    }

    protected goToPath(path: string): void {
        this.endFind();
        void this.open({ at: path.trim().replace(/^\/+|\/+$/g, '') });
    }

    protected select(index: number): void {
        this.selected.set(index);
    }

    protected activate(entry: ArchiveItem, index = this.rows().indexOf(entry)): void {
        if (entry.kind === 'folder')
            this.enter(entry.id);
        else if (entry.kind === 'file' && this.canDownload() && entry.encrypted && !this.unlock())
            this.asking.set(true);
        else if (this.inPlace(entry))
            void this.show(entry, index);
        else if (entry.kind === 'file' && this.canDownload())
            window.open(this.entryUrl(entry, false), '_blank', 'noopener');
    }

    /** A file's name clicked: shown in place when it can be; a new tab with Ctrl, Shift or the middle button, or for the rest. */
    protected openFromLink(event: MouseEvent, entry: ArchiveItem, index: number): void {
        event.stopPropagation();
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || !this.inPlace(entry))
            return;
        event.preventDefault();
        this.select(index);
        void this.show(entry, index);
    }

    /** How an entry is shown in place: `'picture'`, a format (text, PDF…), or null (a new tab, a download). */
    private placeOf(entry: ArchiveItem): FileFormat | 'picture' | null {
        if (entry.kind !== 'file' || !this.canDownload() || (entry.encrypted && !this.unlock()))
            return null;
        if (entry.media === 'image')
            return 'picture';
        if (entry.media)
            return null;
        const format = formatOf(this.itemOf(entry));
        return format && IN_PLACE.has(format.id) ? format : null;
    }

    protected inPlace(entry: ArchiveItem): boolean {
        return this.placeOf(entry) !== null;
    }

    /** The entry as the format views take it: its own path (positions remembered per entry), read through `FILE_SOURCE`. */
    private itemOf(entry: ArchiveItem): LibraryItem {
        const name = entry.name;
        const extension = extensionOf({ name } as LibraryItem);
        return {
            name, path: `${this.item().path}#${entry.id}`, kind: entry.media ?? 'file', extension: extension ? `.${extension}` : null,
            size: entry.size, modified: entry.modified,
        };
    }

    /** Shows `entry` in place of the list; its format's view loaded on demand, reading it from the archive. */
    private async show(entry: ArchiveItem, index: number): Promise<void> {
        const place = this.placeOf(entry);
        if (!place)
            return;
        const item = this.itemOf(entry);
        if (place === 'picture') {
            this.viewing.set({ entry, index, item, format: null, component: null, injector: null });
            this.focusEntry();
            return;
        }
        const injector = Injector.create({
            providers: [{ provide: FILE_VIEW_HOST, useValue: this.entryHost }, { provide: FILE_SOURCE, useValue: this.entrySource(entry) }],
            parent: this.injector,
        });
        const viewing: Viewing = { entry, index, item, format: place, component: null, injector };
        this.viewing.set(viewing);
        this.focusEntry();
        if (!('load' in place.view))
            return;
        let loading = this.components.get(place.id);
        if (!loading)
            this.components.set(place.id, loading = place.view.load());
        try {
            const component = await loading;
            if (this.viewing() === viewing)
                this.viewing.set({ ...viewing, component });
        } catch {
            this.components.delete(place.id);
            if (this.viewing() === viewing) {
                this.viewing.set(null);
                this.toasts.error('This viewer could not be loaded: reload the page to try again.');
            }
        }
    }

    /** What an entry's view may do: read it (and save it with the right to download); never write. */
    private readonly entryHost: FileViewHost = {
        place: 'viewer',
        can: right => (right === Rights.viewFiles || right === Rights.download) && this.host.can(right),
    };

    /** Reads `entry` from the archive, with the unlock given (the view's requests and addresses). */
    private entrySource(entry: ArchiveItem): FileSource {
        return {
            library: false,
            textFile: (_item, signal, encoding) => this.api.archiveText(this.item().path, entry.id, { encoding, unlock: this.unlock() }, signal),
            original: () => this.entryUrl(entry, false),
            download: () => this.canDownload() ? this.entryUrl(entry, true) : null,
        };
    }

    private focusEntry(): void {
        setTimeout(() => this.entryView()?.nativeElement.focus({ preventScroll: true }));
    }

    /** Back to the list as it was, the entry's row selected. */
    protected closeEntry(): void {
        const viewing = this.viewing();
        if (!viewing)
            return;
        this.viewing.set(null);
        this.selected.set(viewing.index);
        this.virtual.scrollTo(viewing.index);
        this.scroller().nativeElement.focus({ preventScroll: true });
    }

    /** The row of the next entry shown in place, before (-1) or after (1) the one shown, among the rows loaded; null at an end. */
    protected neighbour(direction: -1 | 1): number | null {
        const viewing = this.viewing();
        if (!viewing)
            return null;
        const rows = this.rows();
        for (let index = viewing.index + direction; index >= 0 && index < rows.length; index += direction) {
            if (rows[index] && this.inPlace(rows[index]))
                return index;
        }
        return null;
    }

    protected step(direction: -1 | 1): void {
        const index = this.neighbour(direction);
        if (index !== null) {
            this.selected.set(index);
            void this.show(this.rows()[index], index);
        }
    }

    /** Tries `password` on the archive; once it opens, the folder shown is asked again with the unlock. */
    protected async unlockWith(password: string): Promise<void> {
        if (!password || this.unlocking())
            return;
        this.unlocking.set(true);
        this.passwordMessage.set(null);
        try {
            for (;;) {
                const answer = await this.api.unlockArchive(this.item().path, password);
                if ('unlock' in answer) {
                    this.unlock.set(answer.unlock);
                    this.asking.set(false);
                    await this.open({ folder: this.shown()?.listing.needsPassword ? null : this.shown()?.listing.folder.id ?? null });
                    return;
                }
                if (!('read' in answer)) {
                    this.passwordMessage.set(answer.detail ?? 'Nothing in this archive is protected by a password.');
                    return;
                }
                await pause(READING_POLL_MS, new AbortController().signal);
            }
        } catch (error) {
            this.passwordMessage.set(error instanceof ApiError && error.status === 429
                ? 'Too many tries: wait a minute before the next one.'
                : error instanceof ApiError && error.type === 'wrong-password' ? "That password doesn't open this archive."
                : error instanceof Error ? error.message : String(error));
        } finally {
            this.unlocking.set(false);
        }
    }

    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as HTMLElement;
        const viewing = this.viewing();
        if (viewing) {
            // The entry's view keeps its keys (an editor, a find box); the bar and a picture: Esc back, ← → the files around.
            if (event.defaultPrevented || target.closest('input, textarea, [contenteditable="true"], .cm-editor'))
                return;
            if (event.key === 'Escape') {
                event.preventDefault();
                this.closeEntry();
            } else if (!viewing.format && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
                event.preventDefault();
                this.step(event.key === 'ArrowLeft' ? -1 : 1);
            }
            return;
        }
        if (target.closest('input'))
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
        const query = new URLSearchParams();
        if (download)
            query.set('download', 'true');
        if (this.unlock())
            query.set('unlock', this.unlock()!);
        const search = query.toString();
        return `api/files/explore/entry/${pathToken(this.item().path)}/${entry.id}/${encodeURIComponent(entry.name)}${search ? '?' + search : ''}`;
    }

    /** Extract…: the folder shown (the whole archive at its top), into a folder of the library picked in the dialog. */
    protected extractShown(): void {
        const listing = this.shown()?.listing;
        const item = this.item();
        if (!listing)
            return;
        const top = this.parent() === undefined;
        this.dialogs.extract()?.open({
            path: item.path, name: item.name, unlock: this.unlock(),
            folders: top ? undefined : [listing.folder.id], what: top ? undefined : `“${listing.folder.name}”`,
        });
    }

    /** Extract…: one entry (a folder with what it holds, or a file). */
    protected extractOne(entry: ArchiveItem): void {
        const item = this.item();
        this.dialogs.extract()?.open({
            path: item.path, name: item.name, unlock: this.unlock(), what: `“${entry.name}”`,
            folders: entry.kind === 'folder' ? [entry.id] : undefined,
            files: entry.kind === 'folder' ? undefined : [entry.id],
        });
    }

    protected menuOf(entry: ArchiveItem): MenuTarget | null {
        const entries: MenuEntry[] = [];
        if (entry.kind === 'folder')
            entries.push({ id: 'open', label: 'Open', icon: 'folder', title: 'Show this folder of the archive', run: () => this.enter(entry.id) });
        else if (this.inPlace(entry))
            entries.push({ id: 'open', label: 'Open', icon: 'file', title: 'Show it here', run: () => this.activate(entry) });
        else if (entry.kind === 'file' && this.canDownload() && (!entry.encrypted || this.unlock())) {
            entries.push({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'Open it in a new tab', href: this.entryUrl(entry, false), newTab: true });
            entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save it from the archive', href: this.entryUrl(entry, true) });
        }
        if ((entry.kind === 'folder' || entry.kind === 'file') && this.canExtract())
            entries.push({ id: 'extract', label: 'Extract…', icon: 'extract', title: `Extract ${entry.name} into a folder of the library, picked next`, run: () => this.extractOne(entry) });
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
                const answer = await this.api.exploreArchive(this.item().path, { ...where, limit: FIRST_PAGE, unlock: this.unlock() }, abort.signal);
                if (abort.signal.aborted)
                    return;
                if (isListing(answer)) {
                    this.shown.set({ listing: answer, rows: answer.items, next: answer.next });
                    if (answer.needsPassword)
                        this.asking.set(true);
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
            const answer = await this.api.exploreArchive(this.item().path, { folder: shown.listing.folder.id, after: shown.next, limit: NEXT_PAGE, unlock: this.unlock() }, abort?.signal);
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
