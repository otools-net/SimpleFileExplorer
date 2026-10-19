import { NgComponentOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, Injector, signal, Type, untracked, viewChild } from '@angular/core';
import { ConfigService, withVersion } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { ToastService } from '../../core/toast.service';
import { formatDate, formatSize, joinParts } from '../../core/format';
import { LibraryItem, TagSummary } from '../../core/models';
import { FILE_VIEW_HOST, FileViewHost, formatOf } from '../../formats/formats';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { tagQuery } from '../../tags/tags';
import { TrickplayMarksService } from '../../trickplay/trickplay-marks.service';
import { VideoPlayerComponent } from '../../player/video-player.component';
import { Dialogs, registerDialog } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { QuickLinksService } from '../quick-links/quick-links.service';

// Mouse wheel (not over a view that scrolls itself, such as text): down = next, up = previous. A touchpad sends a long burst of small deltas (with inertia), so they add
// up to a step and the rest of the gesture is ignored until it pauses, otherwise one flick would skip several files.
// Wheel notches are large: they only wait a short fixed time, so each one still moves.
const WHEEL_STEP = 50;
const WHEEL_PAUSE_MS = 250;
/** A press on the picture that moves less than this (CSS pixels) is a click (zoom), not a drag. */
const CLICK_SLOP = 4;

/** Where a picture of `size` goes along one side of a stage `room` long: centred if it fits, else never leaving a gap. */
function clampAxis(position: number, room: number, size: number): number {
    return size <= room ? (room - size) / 2 : Math.min(0, Math.max(room - size, position));
}

/** Where keys, the wheel and swipes belong to what is shown (a text view's own scrolling, cursor and shortcuts). */
const OWNS_KEYS = '[data-owns-keys]';

/** An entry of the viewer's ⋮ menu: an action, or a link (a new tab, a download). */
interface ViewerMenuEntry {
    id: string;
    label: string;
    icon: string;
    shortcut?: string;
    title: string;
    run?: () => void;
    href?: string | null;
    newTab?: boolean;
}

/**
 * Full-screen preview for the files of the listing that have a viewer (images, videos, audio, and the formats of
 * `formats/formats.ts`, whose components it shows), with keyboard, swipe and wheel navigation;
 * reaching the end loads the next page. Large images show their thumbnail while the original downloads. A video can
 * move to a window of its own (W), where it goes on from the same position. A picture shown smaller than it is can
 * be seen at its actual size (click it, or Z) and dragged around, as browsers do with a big picture on its own.
 */
@Component({
    selector: 'sfe-viewer',
    imports: [NgComponentOutlet, IconComponent, TagChipComponent, VideoPlayerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { '(window:resize)': 'onResize()' },
    template: `
        <dialog #dialog class="viewer" aria-label="Preview" tabindex="-1" [class.has-video]="item()?.kind === 'video'" (close)="onClose()" (cancel)="onCancel($event)" (pointerdown)="onPointerDown($event)" (keydown)="onKeydown($event)">
            <header class="viewer-bar">
                <div class="viewer-title">
                    <strong>{{ item()?.name }}</strong>
                    <span>{{ meta() }}</span>
                    <div class="viewer-tags" [hidden]="!item()?.tags?.length">
                        @for (tag of item()?.tags ?? []; track tag.id) {
                            <sfe-tag-chip [tag]="tag" [title]="'Find files tagged ' + tag.displayName" [clickable]="true" (pick)="searchTag($event)" />
                        }
                    </div>
                </div>
                <div class="viewer-actions">
                    @if (can('tags.view')) {
                        <button type="button" class="icon-button" title="Tags (T)" aria-label="Tags" (click)="openTags()"><svg sfeIcon="tag"></svg></button>
                    }
                    @if (menuEntries().length) {
                        <button #menuButton type="button" class="icon-button viewer-menu-button" title="More actions" aria-label="More actions" aria-haspopup="menu"
                                [attr.aria-expanded]="menuOpen()" [class.active]="menuOpen()" (click)="toggleMenu()"><svg sfeIcon="more"></svg></button>
                    }
                    <button type="button" class="icon-button" title="Close (Esc)" aria-label="Close" (click)="close()"><svg sfeIcon="close"></svg></button>
                    @if (menuOpen()) {
                        <div class="item-menu-backdrop" aria-hidden="true" (click)="menuOpen.set(false)"></div>
                        <div #menu class="item-menu viewer-menu" role="menu" [attr.aria-label]="'Actions for ' + item()?.name" (keydown)="onMenuKeydown($event)">
                            <div class="item-menu-title">{{ item()?.name }}</div>
                            @for (entry of menuEntries(); track entry.id) {
                                @if (entry.href) {
                                    <a role="menuitem" class="item-menu-entry" [attr.data-entry]="entry.id" [href]="entry.href" [title]="entry.title"
                                       [attr.target]="entry.newTab ? '_blank' : null" [attr.rel]="entry.newTab ? 'noopener' : null" (click)="menuOpen.set(false)">
                                        <svg [sfeIcon]="entry.icon"></svg><span>{{ entry.label }}</span>
                                    </a>
                                } @else {
                                    <button type="button" role="menuitem" class="item-menu-entry" [attr.data-entry]="entry.id" [title]="entry.title" (click)="runEntry(entry)">
                                        <svg [sfeIcon]="entry.icon"></svg><span>{{ entry.label }}</span>
                                        @if (entry.shortcut) {
                                            <kbd>{{ entry.shortcut }}</kbd>
                                        }
                                    </button>
                                }
                            }
                        </div>
                    }
                </div>
            </header>
            <div #stage class="viewer-stage" [class.zoomed]="zoomed()" [class.document]="!builtin()" (touchstart)="onTouchStart($event)" (touchend)="onTouchEnd($event)" (wheel)="onWheel($event)" (click)="onStageClick($event)">
                @for (shown of shownItem(); track shown.path) {
                    @if (unsupported()) {
                        <div class="viewer-message">
                            <p>This browser cannot play this format{{ converted() ? ', and the server could not convert it (it may be converting as many videos as it can).' : '.' }}</p>
                            <p>{{ urls()?.externalName ? 'Use “Open in ' + urls()?.externalName + '” or download it.' : 'Download it to play it in another app.' }}</p>
                        </div>
                    } @else {
                        @switch (builtin()) {
                            @case ('image') {
                                @if (progressive() && !originalReady()) {
                                    <img class="preview" [src]="pictureUrls().thumbnail" [alt]="shown.name" [style.visibility]="thumbnailFailed() ? 'hidden' : null" (error)="thumbnailFailed.set(true)">
                                } @else {
                                    <img #picture [src]="pictureUrls().file" [alt]="shown.name" decoding="async"
                                         [class.zoomable]="zoomable()" [class.actual-size]="zoomed()" [class.dragging]="dragging()"
                                         [style.width.px]="zoomed() ? actualSize().width : null" [style.height.px]="zoomed() ? actualSize().height : null"
                                         [style.transform]="zoomed() ? 'translate(' + pan().x + 'px, ' + pan().y + 'px)' : null"
                                         (load)="measure()" (dragstart)="$event.preventDefault()" (pointerdown)="onPicturePointerDown($event)"
                                         (pointermove)="onPicturePointerMove($event)" (pointerup)="onPicturePointerUp($event)" (pointercancel)="endDrag()">
                                }
                            }
                            @case ('video') {
                                <sfe-video-player [src]="urls()!.file(shown.path)" [name]="shown.name" [poster]="urls()!.thumbnail(shown.path)"
                                                  [seekStep]="config.config()?.seekSeconds ?? 10" [loop]="config.config()?.loop ?? false" [stream]="videoStream()" [cinemaButton]="true"
                                                  (unsupported)="unsupported.set(true); converted.set($event)" />
                            }
                            @case ('audio') {
                                <audio [src]="urls()!.file(shown.path)" controls autoplay preload="metadata" (error)="unsupported.set(true)"></audio>
                            }
                            @default {
                                @if (viewComponent(); as component) {
                                    <ng-container *ngComponentOutlet="component; inputs: { item: shown, format: format(), edit: editRequested() }; injector: viewInjector" />
                                } @else if (viewFailed()) {
                                    <div class="viewer-message"><p>This viewer could not be loaded: reload the page to try again.</p></div>
                                }
                            }
                        }
                    }
                }
            </div>
            <button type="button" class="viewer-nav prev" aria-label="Previous (←)" title="Previous (← or wheel up)" [disabled]="!neighbours().previous" (click)="go(-1)"><svg sfeIcon="left"></svg></button>
            <button type="button" class="viewer-nav next" aria-label="Next (→)" title="Next (→ or wheel down)" [disabled]="nextDisabled()" (click)="go(1)"><svg sfeIcon="right"></svg></button>
        </dialog>
    `,
})
export class ViewerComponent {
    private readonly store = inject(ListingStore);
    private readonly dialogs = inject(Dialogs);
    private readonly marks = inject(TrickplayMarksService);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly copyPaths = inject(CopyPathService);
    private readonly quickLinks = inject(QuickLinksService);
    protected readonly urls = this.config.urls;
    private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
    private readonly stage = viewChild.required<ElementRef<HTMLElement>>('stage');
    private readonly player = viewChild(VideoPlayerComponent);
    private readonly picture = viewChild<ElementRef<HTMLImageElement>>('picture');
    private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');
    private readonly menuButton = viewChild<ElementRef<HTMLElement>>('menuButton');

    private readonly injector = inject(Injector);

    /**
     * The file shown, by path: a refresh of the listing in place keeps it (with its new tags...), even when the files
     * around it change. `asText`: opened with "Open as text"; `edit`: ready to be changed (a new file).
     */
    private readonly opened = signal<{ item: LibraryItem; asText: boolean; edit: boolean } | null>(null);
    protected readonly editRequested = computed(() => this.opened()?.edit ?? false);
    protected readonly item = computed((): LibraryItem | null => {
        const opened = this.opened();
        return opened ? this.store.items().find(item => item.path === opened.item.path) ?? opened.item : null;
    });
    /** Its place among the files the viewer steps through; -1 when it isn't one of them (opened as text). */
    protected readonly index = computed(() => {
        const path = this.item()?.path;
        return path === undefined ? -1 : this.store.viewable().findIndex(item => item.path === path);
    });
    /** The viewable files loaded before and after the one shown, in listing order. */
    protected readonly neighbours = computed(() => {
        const items = this.store.items();
        const path = this.item()?.path;
        const at = items.findIndex(item => item.path === path);
        const find = (step: number): LibraryItem | null => {
            for (let i = at + step; at >= 0 && i >= 0 && i < items.length; i += step) {
                if (formatOf(items[i]))
                    return items[i];
            }
            return null;
        };
        return { previous: find(-1), next: find(1) };
    });
    protected readonly format = computed(() => formatOf(this.item(), { asText: this.opened()?.asText }));
    /** The viewer's own markup for media; null for a format with a component. */
    protected readonly builtin = computed(() => {
        const view = this.format()?.view;
        return view && 'builtin' in view ? view.builtin : null;
    });
    /** The component of the format shown, once loaded. */
    protected readonly viewComponent = signal<Type<unknown> | null>(null);
    protected readonly viewFailed = signal(false);
    /** What the view shown asks before it is left (unsaved changes). */
    private readonly guards = new Set<() => boolean>();
    private readonly host: FileViewHost = {
        place: 'viewer',
        can: right => this.can(right),
        open: (item, options) => this.open(item, { edit: options?.edit }),
        guard: canLeave => {
            this.guards.add(canLeave);
            return () => this.guards.delete(canLeave);
        },
    };
    protected readonly viewInjector = Injector.create({ providers: [{ provide: FILE_VIEW_HOST, useValue: this.host }], parent: this.injector });
    /** The item on the stage, as a list of one (or none) so that each item gets elements of its own. */
    protected readonly shownItem = computed(() => {
        const item = this.item();
        return item ? [item] : [];
    }, { equal: (a, b) => a[0]?.path === b[0]?.path });
    protected readonly unsupported = signal(false);
    /** The server's conversion failed too. */
    protected readonly converted = signal(false);
    /** The server's conversion of the video shown, one per item (a new one would start the player over). */
    protected readonly videoStream = computed(() => {
        const item = this.shownItem()[0];
        return item?.kind === 'video' ? this.config.videoStream(item.path) : null;
    });
    private readonly loadingMore = signal(false);
    protected readonly nextDisabled = computed(() => this.loadingMore() || (!this.neighbours().next && !this.store.hasMore()));
    protected readonly meta = computed(() => {
        const item = this.item();
        if (!item)
            return '';
        const index = this.index();
        const position = index < 0 ? null : `${index + 1} / ${this.store.viewable().length}${this.store.hasMore() ? '+' : ''}`;
        return joinParts([position, this.config.resolution(item), this.config.duration(item), formatSize(item.size), formatDate(item.modified), item.directory], '  ·  ');
    });
    /** "Open in new tab": the file itself, or its viewer's page for formats that have one. */
    protected readonly fileUrl = computed(() => {
        const item = this.item();
        return item ? (this.format()?.page ? this.urls()!.view(item.path) : this.urls()!.file(item.path)) : null;
    });
    /** The picture shown and its thumbnail, marked with its time: one saved over (the picture editor) shows again. */
    protected readonly pictureUrls = computed(() => {
        const item = this.item();
        const urls = this.urls();
        return item && urls
            ? { file: withVersion(urls.file(item.path), item), thumbnail: withVersion(urls.thumbnail(item.path), item) }
            : { file: null, thumbnail: null };
    });
    protected readonly downloadUrl = computed(() => this.item() ? this.urls()!.download(this.item()!.path) : null);
    /** The item can be opened in the external player (videos and audio, when the server has one for this session). */
    protected readonly external = computed(() => Boolean((this.item()?.kind === 'video' || this.item()?.kind === 'audio') && this.urls()?.externalName));

    /** The picture can be edited (the right, and a server with the editor). */
    protected readonly editable = computed(() => this.item()?.kind === 'image' && this.can('files.edit') && Boolean(this.config.config()?.editor));

    /** The ⋮ menu of the bar: what the file shown allows besides its tags (zoom, edit, quick link, links...). */
    protected readonly menuOpen = signal(false);
    protected readonly menuEntries = computed((): ViewerMenuEntry[] => {
        const item = this.item();
        if (!item)
            return [];
        const entries: ViewerMenuEntry[] = [];
        if (this.zoomed())
            entries.push({ id: 'fit', label: 'Fit to the screen', icon: 'zoom-out', shortcut: 'Z', title: 'Fit to the screen (Z, or click the picture)', run: () => this.zoomOut() });
        else if (this.zoomable())
            entries.push({ id: 'actualSize', label: 'Actual size', icon: 'zoom-in', shortcut: 'Z', title: 'Actual size, 100% (Z, or click the picture)', run: () => this.zoomIn() });
        if (this.editable())
            entries.push({ id: 'crop', label: 'Crop…', icon: 'crop', shortcut: 'E', title: 'Edit: crop, then save a copy (E)', run: () => this.edit() });
        if (this.quickLinks.enabled()) {
            const pinned = this.quickLinks.paths().has(item.path);
            entries.push({
                id: 'quickLink', label: pinned ? 'Remove from quick links' : 'Add to quick links', icon: 'pin',
                title: pinned ? `Take ${item.name} out of your quick links` : `Show ${item.name} in your quick links, in the sidebar`,
                run: () => void this.quickLinks.toggle(item.path),
            });
        }
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => this.copyPath() });
        if (item.kind === 'video')
            entries.push({ id: 'popOut', label: 'Open in a new window', icon: 'window', shortcut: 'W', title: 'Play it in a window of its own, from here (W)', run: () => this.popOut() });
        if (this.external())
            entries.push({ id: 'external', label: `Open in ${this.urls()?.externalName}`, icon: 'play', title: 'A link working without signing in, for a while', run: () => this.openExternal() });
        entries.push({ id: 'newTab', label: 'Open in new tab', icon: 'external', title: 'Open it in a tab of its own', href: this.fileUrl(), newTab: true });
        if (this.can('files.download'))
            entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save this file to this device', href: this.downloadUrl() });
        return entries;
    });

    protected toggleMenu(): void {
        this.menuOpen.update(open => !open);
        if (this.menuOpen())
            afterNextRender(() => this.menu()?.nativeElement.querySelector<HTMLElement>('.item-menu-entry')?.focus({ preventScroll: true }), { injector: this.injector });
    }

    protected runEntry(entry: ViewerMenuEntry): void {
        this.menuOpen.set(false);
        entry.run?.();
    }

    /** Arrows move between the entries; Esc closes the menu only (not the viewer), back on its button. */
    protected onMenuKeydown(event: KeyboardEvent): void {
        const entries = [...this.menu()?.nativeElement.querySelectorAll<HTMLElement>('.item-menu-entry') ?? []];
        const index = entries.indexOf(document.activeElement as HTMLElement);
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            event.stopPropagation();
            const step = event.key === 'ArrowDown' ? 1 : -1;
            entries[(index + step + entries.length) % entries.length]?.focus();
        } else if (event.key === 'Escape' || event.key === 'Tab') {
            event.preventDefault();
            event.stopPropagation();
            this.menuOpen.set(false);
            this.menuButton()?.nativeElement.focus();
        }
    }

    /** A click outside the open menu closes it. */
    protected onPointerDown(event: PointerEvent): void {
        const target = event.target as Node;
        if (this.menuOpen() && !this.menu()?.nativeElement.contains(target) && !this.menuButton()?.nativeElement.contains(target))
            this.menuOpen.set(false);
    }

    /** The session may use `right` on the item shown (a right of the space that the folder rules let through there). */
    protected can(right: string): boolean {
        return this.store.can(right, this.item());
    }

    /** A large image shows its thumbnail, stretched to the stage, until the original has loaded and decoded. */
    protected readonly progressive = signal(false);
    protected readonly originalReady = signal(false);
    protected readonly thumbnailFailed = signal(false);
    /** The original of a large image, loading behind its thumbnail. */
    private pending: HTMLImageElement | null = null;

    /** The original is shown smaller than it is (fitted to the stage): clicking it shows it at its actual size. */
    protected readonly zoomable = signal(false);
    /** The picture at its actual size (one picture pixel per CSS pixel, as a browser shows a picture at 100%). */
    protected readonly zoomed = signal(false);
    protected readonly actualSize = signal({ width: 0, height: 0 });
    /** The zoomed picture's top-left corner, relative to the stage. */
    protected readonly pan = signal({ x: 0, y: 0 });
    protected readonly dragging = signal(false);
    private drag: { pointerId: number; startX: number; startY: number; panX: number; panY: number; moved: boolean } | null = null;

    private touchStart: Touch | null = null;
    private wheelDelta = 0;
    private wheelBlockedUntil = 0;

    constructor() {
        registerDialog(dialogs => dialogs.viewer, this);
        // The component of a format, loaded the first time one of its files is shown.
        effect(() => {
            const format = this.format();
            const view = format?.view;
            untracked(() => {
                this.viewFailed.set(false);
                if (!view || !('load' in view)) {
                    this.viewComponent.set(null);
                    return;
                }
                void view.load().then(component => {
                    if (this.format() === format)
                        this.viewComponent.set(component);
                }, () => {
                    if (this.format() === format)
                        this.viewFailed.set(true);
                });
            });
        });
        // Scrub previews once the trickplay is there, if the player is still showing this video.
        effect(() => {
            const player = this.player();
            const item = untracked(() => this.item());
            if (!player || item?.kind !== 'video')
                return;
            untracked(() => {
                void this.marks.load(item.path, () => this.player() === player).then(trickplay => {
                    if (trickplay && this.player() === player)
                        player.setTrickplay(trickplay, index => this.urls()!.trickplaySheet(item.path, index, trickplay.version));
                });
            });
        });
    }

    /**
     * Shows `item` (`asText`: any file, as text; `edit`: ready to be changed). Nothing happens for a file without a
     * viewer, nor when the file shown has changes the person keeps.
     */
    open(item: LibraryItem, options?: { asText?: boolean; edit?: boolean }): void {
        if (!formatOf(item, options))
            return;
        const dialog = this.dialog().nativeElement;
        if (dialog.open && this.item()?.path !== item.path && !this.confirmLeave())
            return;
        if (!dialog.open)
            dialog.showModal();
        this.show(item, options?.asText ?? false, options?.edit ?? false);
    }

    /** Closes the viewer, unless the file shown has changes the person keeps; false then. */
    close(): boolean {
        const dialog = this.dialog().nativeElement;
        if (dialog.open && !this.confirmLeave())
            return false;
        dialog.close();
        return true;
    }

    /** The file shown may be left: nothing unsaved in it, or the person agrees to lose it. */
    confirmLeave(): boolean {
        for (const canLeave of this.guards) {
            if (!canLeave())
                return false;
        }
        return true;
    }

    /** Esc closes the dialog by itself: not over unsaved changes the person keeps. */
    protected onCancel(event: Event): void {
        if (!this.confirmLeave())
            event.preventDefault();
    }

    private show(item: LibraryItem, asText = false, edit = false): void {
        this.menuOpen.set(false);
        this.stopPending();
        this.resetZoom();
        this.opened.set({ item, asText, edit });
        this.unsupported.set(false);
        this.converted.set(false);
        this.thumbnailFailed.set(false);
        this.originalReady.set(false);
        this.progressive.set(this.config.isLargeImage(item));
        if (this.progressive())
            this.loadOriginal(item);
        this.store.reportView(item);
    }

    /** If the browser can't show the original (e.g. TIFF), the thumbnail stays. */
    private loadOriginal(item: LibraryItem): void {
        const original = new Image();
        this.pending = original;
        original.src = this.urls()!.file(item.path);
        // Rejected when the original can't be shown, or when the viewer moved on (see stopPending).
        original.decode().then(() => {
            if (this.pending === original) {
                this.pending = null;
                this.originalReady.set(true);
            }
        }, () => {
            if (this.pending === original)
                this.pending = null;
        });
    }

    /** Moving on: stop downloading the original nobody will see. */
    private stopPending(): void {
        this.pending?.removeAttribute('src');
        this.pending = null;
    }

    protected async go(step: number): Promise<void> {
        const from = this.item()?.path;
        let target = step < 0 ? this.neighbours().previous : this.neighbours().next;
        // Past the last one loaded: the next pages, until one of them has a file with a viewer.
        while (!target && step > 0 && this.store.hasMore()) {
            this.loadingMore.set(true);
            let loaded: boolean;
            try {
                loaded = await this.store.loadMore();
            } finally {
                this.loadingMore.set(false);
            }
            if (!loaded || this.item()?.path !== from)
                return;
            target = this.neighbours().next;
        }
        if (target && this.confirmLeave())
            this.show(target);
    }

    protected onClose(): void {
        this.menuOpen.set(false);
        this.stopPending();
        this.resetZoom();
        this.opened.set(null);
    }

    /* ---------- Actual size (zoom) and drag ---------- */

    private resetZoom(): void {
        this.zoomed.set(false);
        this.zoomable.set(false);
        this.endDrag();
    }

    /** Once the original is drawn, and when the window changes size: is it shown smaller than it is? */
    protected measure(): void {
        const picture = this.picture()?.nativeElement;
        if (!picture || this.zoomed())
            return;
        const box = picture.getBoundingClientRect();
        this.zoomable.set(picture.naturalWidth > box.width + 1 || picture.naturalHeight > box.height + 1);
    }

    protected onResize(): void {
        if (this.zoomed())
            this.pan.update(pan => this.clampPan(pan.x, pan.y));
        else
            this.measure();
    }

    /** Shows the picture at its actual size, keeping the point clicked (else the centre) where it is on the screen. */
    protected zoomIn(clientX?: number, clientY?: number): void {
        const picture = this.picture()?.nativeElement;
        if (!picture || this.zoomed() || !this.zoomable())
            return;
        const box = picture.getBoundingClientRect();
        const stage = this.stage().nativeElement.getBoundingClientRect();
        const x = clientX ?? box.left + box.width / 2;
        const y = clientY ?? box.top + box.height / 2;
        const width = picture.naturalWidth;
        const height = picture.naturalHeight;
        this.actualSize.set({ width, height });
        this.zoomed.set(true);
        this.pan.set(this.clampPan(
            x - stage.left - (box.width ? (x - box.left) / box.width : 0.5) * width,
            y - stage.top - (box.height ? (y - box.top) / box.height : 0.5) * height));
    }

    protected zoomOut(): void {
        this.zoomed.set(false);
        this.endDrag();
    }

    private clampPan(x: number, y: number): { x: number; y: number } {
        const stage = this.stage().nativeElement.getBoundingClientRect();
        const { width, height } = this.actualSize();
        return { x: clampAxis(x, stage.width, width), y: clampAxis(y, stage.height, height) };
    }

    protected onPicturePointerDown(event: PointerEvent): void {
        if (event.button !== 0 || !(this.zoomed() || this.zoomable()))
            return;
        const pan = this.pan();
        this.drag = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, panX: pan.x, panY: pan.y, moved: false };
        if (this.zoomed()) {
            // Keeps the moves coming when the pointer leaves the picture, and stops the browser selecting it.
            (event.target as Element).setPointerCapture?.(event.pointerId);
            event.preventDefault();
        }
    }

    protected onPicturePointerMove(event: PointerEvent): void {
        const drag = this.drag;
        if (!drag || event.pointerId !== drag.pointerId)
            return;
        const dx = event.clientX - drag.startX;
        const dy = event.clientY - drag.startY;
        if (!drag.moved && Math.abs(dx) <= CLICK_SLOP && Math.abs(dy) <= CLICK_SLOP)
            return;
        drag.moved = true;
        if (this.zoomed()) {
            this.dragging.set(true);
            this.pan.set(this.clampPan(drag.panX + dx, drag.panY + dy));
        }
    }

    /** A press that didn't move is a click: actual size, or back to fitting the screen. */
    protected onPicturePointerUp(event: PointerEvent): void {
        const drag = this.drag;
        if (!drag || event.pointerId !== drag.pointerId)
            return;
        this.endDrag();
        if (drag.moved)
            return;
        if (this.zoomed())
            this.zoomOut();
        else
            this.zoomIn(event.clientX, event.clientY);
    }

    protected endDrag(): void {
        this.drag = null;
        this.dragging.set(false);
    }

    protected openTags(): void {
        const item = this.item();
        if (item && this.can('tags.view'))
            this.dialogs.fileTags()?.open(item);
    }

    /** Opens the picture in the editor, over the viewer. */
    protected edit(): void {
        const item = this.item();
        if (item && this.editable())
            void this.dialogs.imageEditor()?.open(item);
    }

    protected openExternal(): void {
        const item = this.item();
        if (item)
            this.config.openExternal(item.path).catch((error: Error) => this.toasts.error(`Could not open it in ${this.urls()?.externalName}: ${error.message}`));
    }

    protected copyPath(): void {
        const item = this.item();
        if (item)
            void this.copyPaths.copy(item.path);
    }

    protected searchTag(tag: TagSummary): void {
        if (!this.close())
            return;
        this.store.navigate({ q: tagQuery(tag) });
    }

    /** Plays the current video in a window of its own, from where it is now; the viewer closes. */
    protected popOut(): void {
        const item = this.item();
        if (!item || item.kind !== 'video')
            return;
        const width = Math.min(1280, Math.round(screen.availWidth * 0.8));
        const height = Math.min(Math.round(width * 9 / 16), Math.round(screen.availHeight * 0.8));
        const opened = window.open(this.urls()!.player(item.path, this.player()?.currentTime() ?? 0), '_blank', `popup,width=${width},height=${height}`);
        // A blocked pop-up leaves the video playing here.
        if (opened)
            this.close();
    }

    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as Element;
        // Esc with the menu open closes the menu, not the viewer.
        if (event.key === 'Escape' && this.menuOpen()) {
            event.preventDefault();
            this.menuOpen.set(false);
            return;
        }
        if (target.closest?.(OWNS_KEYS)) {
            // The view's keys are its own (arrows move its cursor); Esc leaves it first (unless it used it), then closes.
            if (event.key === 'Escape' && !event.defaultPrevented) {
                event.preventDefault();
                this.dialog().nativeElement.focus();
            }
            return;
        }
        if (target instanceof HTMLMediaElement && event.key === ' ')
            return;
        // A focused button or link handles Space/Enter itself; the seek bar handles the arrows itself.
        if ((event.key === ' ' || event.key === 'Enter') && target.closest?.('button, a'))
            return;
        if (target.matches?.('input[type="range"]') && event.key.startsWith('Arrow'))
            return;
        const player = this.player();
        if (player?.handleKey(event)) {
            event.preventDefault();
            return;
        }
        const plain = !event.ctrlKey && !event.metaKey && !event.altKey;
        if (event.key === 'Escape' && this.zoomed()) {
            // Back to the whole picture first; the next Esc closes.
            event.preventDefault();
            this.zoomOut();
        } else if (event.key.toLowerCase() === 'z' && plain && (this.zoomed() || this.zoomable())) {
            event.preventDefault();
            if (this.zoomed())
                this.zoomOut();
            else
                this.zoomIn();
        } else if (event.key === 'ArrowLeft') {
            event.preventDefault();
            void this.go(-1);
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            void this.go(1);
        } else if (event.key.toLowerCase() === 'w' && plain && player) {
            event.preventDefault();
            this.popOut();
        } else if (event.key.toLowerCase() === 't' && plain && this.item()) {
            event.preventDefault();
            this.openTags();
        } else if (event.key.toLowerCase() === 'e' && plain && this.editable()) {
            event.preventDefault();
            this.edit();
        }
    }

    /* ---------- Swipe left/right on touch screens ---------- */

    protected onTouchStart(event: TouchEvent): void {
        // Dragging the video seek bar is not a swipe, nor is moving a picture shown at its actual size.
        const onControls = (event.target as Element).closest?.(`.player-controls, ${OWNS_KEYS}`);
        this.touchStart = event.touches.length === 1 && !onControls && !this.zoomed() ? event.touches[0] : null;
    }

    protected onTouchEnd(event: TouchEvent): void {
        if (!this.touchStart)
            return;
        const touch = event.changedTouches[0];
        const dx = touch.clientX - this.touchStart.clientX;
        const dy = touch.clientY - this.touchStart.clientY;
        this.touchStart = null;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5)
            void this.go(dx < 0 ? 1 : -1);
    }

    /** Clicking the dark area around the media closes the viewer. */
    protected onStageClick(event: MouseEvent): void {
        if (event.target === this.stage().nativeElement)
            this.close();
    }

    protected onWheel(event: WheelEvent): void {
        // Ctrl+wheel is the browser zoom; the player's controls (seek bar) keep the wheel to themselves.
        if (event.ctrlKey || (event.target as Element).closest?.(`.player-controls, ${OWNS_KEYS}`))
            return;
        event.preventDefault();
        if (this.zoomed()) {
            // A picture at its actual size scrolls instead, as a page would (Shift: sideways).
            const lines = event.deltaMode === 0 ? 1 : WHEEL_STEP;
            const dx = (event.shiftKey ? event.deltaY : event.deltaX) * lines;
            const dy = (event.shiftKey ? 0 : event.deltaY) * lines;
            this.pan.update(pan => this.clampPan(pan.x - dx, pan.y - dy));
            return;
        }
        const raw = Math.abs(event.deltaY) >= Math.abs(event.deltaX) ? event.deltaY : event.deltaX;
        // deltaMode 1 = lines, 2 = pages: count each as a full step.
        const delta = event.deltaMode === 0 ? raw : Math.sign(raw) * WHEEL_STEP;
        const now = performance.now();
        if (now < this.wheelBlockedUntil) {
            if (Math.abs(delta) < WHEEL_STEP)
                this.wheelBlockedUntil = now + WHEEL_PAUSE_MS;
            return;
        }
        this.wheelDelta += delta;
        if (Math.abs(this.wheelDelta) < WHEEL_STEP)
            return;
        const step = this.wheelDelta > 0 ? 1 : -1;
        this.wheelDelta = 0;
        this.wheelBlockedUntil = now + WHEEL_PAUSE_MS;
        void this.go(step);
    }
}
