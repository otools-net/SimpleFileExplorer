import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, signal, untracked, viewChild } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { ToastService } from '../../core/toast.service';
import { formatDate, formatSize, joinParts } from '../../core/format';
import { LibraryItem, TagSummary } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { tagQuery } from '../../tags/tags';
import { TrickplayMarksService } from '../../trickplay/trickplay-marks.service';
import { VideoPlayerComponent } from '../../player/video-player.component';
import { Dialogs, registerDialog } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { PinButtonComponent } from '../quick-links/pin-button.component';

// Mouse wheel: down = next, up = previous. A touchpad sends a long burst of small deltas (with inertia), so they add
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

/**
 * Full-screen preview for the images, videos and audio of the listing, with keyboard, swipe and wheel navigation;
 * reaching the end loads the next page. Large images show their thumbnail while the original downloads. A video can
 * move to a window of its own (W), where it goes on from the same position. A picture shown smaller than it is can
 * be seen at its actual size (click it, or Z) and dragged around, as browsers do with a big picture on its own.
 */
@Component({
    selector: 'sfe-viewer',
    imports: [IconComponent, TagChipComponent, VideoPlayerComponent, PinButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { '(window:resize)': 'onResize()' },
    template: `
        <dialog #dialog class="viewer" aria-label="Preview" [class.has-video]="item()?.kind === 'video'" (close)="onClose()" (keydown)="onKeydown($event)">
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
                    @if (config.can('tags.view')) {
                        <button type="button" class="icon-button" title="Tags (T)" aria-label="Tags" (click)="openTags()"><svg sfeIcon="tag"></svg></button>
                    }
                    @if (item(); as shown) {
                        <button [sfePin]="shown.path" [name]="shown.name"></button>
                    }
                    @if (zoomed()) {
                        <button type="button" class="icon-button" title="Fit to the screen (Z, or click the picture)" aria-label="Fit to the screen" (click)="zoomOut()"><svg sfeIcon="zoom-out"></svg></button>
                    } @else if (zoomable()) {
                        <button type="button" class="icon-button" title="Actual size, 100% (Z, or click the picture)" aria-label="Actual size" (click)="zoomIn()"><svg sfeIcon="zoom-in"></svg></button>
                    }
                    @if (editable()) {
                        <button type="button" class="icon-button" title="Edit: crop, then save a copy (E)" aria-label="Edit" (click)="edit()"><svg sfeIcon="crop"></svg></button>
                    }
                    @if (copyPaths.enabled()) {
                        <button type="button" class="icon-button" title="Copy the full path" aria-label="Copy the full path" (click)="copyPath()"><svg sfeIcon="copy"></svg></button>
                    }
                    @if (item()?.kind === 'video') {
                        <button type="button" class="icon-button" title="Open in a new window (W)" aria-label="Open in a new window" (click)="popOut()"><svg sfeIcon="window"></svg></button>
                    }
                    @if (external()) {
                        <button type="button" class="icon-button" [title]="'Open in ' + urls()?.externalName + ' (a link working without signing in, for a while)'" [attr.aria-label]="'Open in ' + urls()?.externalName" (click)="openExternal()"><svg sfeIcon="play"></svg></button>
                    }
                    <a class="icon-button" [href]="fileUrl()" target="_blank" rel="noopener" title="Open in new tab" aria-label="Open in new tab"><svg sfeIcon="external"></svg></a>
                    @if (config.can('files.download')) {
                        <a class="icon-button" [href]="downloadUrl()" title="Download" aria-label="Download"><svg sfeIcon="download"></svg></a>
                    }
                    <button type="button" class="icon-button" title="Close (Esc)" aria-label="Close" (click)="close()"><svg sfeIcon="close"></svg></button>
                </div>
            </header>
            <div #stage class="viewer-stage" [class.zoomed]="zoomed()" (touchstart)="onTouchStart($event)" (touchend)="onTouchEnd($event)" (wheel)="onWheel($event)" (click)="onStageClick($event)">
                @for (shown of shownItem(); track shown.path) {
                    @if (unsupported()) {
                        <div class="viewer-message">
                            <p>This browser cannot play this format{{ converted() ? ', and the server could not convert it (it may be converting as many videos as it can).' : '.' }}</p>
                            <p>{{ urls()?.externalName ? 'Use “Open in ' + urls()?.externalName + '” or download it.' : 'Download it to play it in another app.' }}</p>
                        </div>
                    } @else {
                        @switch (shown.kind) {
                            @case ('image') {
                                @if (progressive() && !originalReady()) {
                                    <img class="preview" [src]="urls()!.thumbnail(shown.path)" [alt]="shown.name" [style.visibility]="thumbnailFailed() ? 'hidden' : null" (error)="thumbnailFailed.set(true)">
                                } @else {
                                    <img #picture [src]="urls()!.file(shown.path)" [alt]="shown.name" decoding="async"
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
                            @default {
                                <audio [src]="urls()!.file(shown.path)" controls autoplay preload="metadata" (error)="unsupported.set(true)"></audio>
                            }
                        }
                    }
                }
            </div>
            <button type="button" class="viewer-nav prev" aria-label="Previous (←)" title="Previous (← or wheel up)" [disabled]="index() <= 0" (click)="go(-1)"><svg sfeIcon="left"></svg></button>
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
    protected readonly urls = this.config.urls;
    private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
    private readonly stage = viewChild.required<ElementRef<HTMLElement>>('stage');
    private readonly player = viewChild(VideoPlayerComponent);
    private readonly picture = viewChild<ElementRef<HTMLImageElement>>('picture');

    protected readonly index = signal(-1);
    protected readonly item = computed((): LibraryItem | null => this.store.media()[this.index()] ?? null);
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
    protected readonly nextDisabled = computed(() =>
        this.loadingMore() || (this.index() >= this.store.media().length - 1 && !this.store.hasMore()));
    protected readonly meta = computed(() => {
        const item = this.item();
        if (!item)
            return '';
        const position = `${this.index() + 1} / ${this.store.media().length}${this.store.hasMore() ? '+' : ''}`;
        return joinParts([position, this.config.resolution(item), this.config.duration(item), formatSize(item.size), formatDate(item.modified), item.directory], '  ·  ');
    });
    protected readonly fileUrl = computed(() => this.item() ? this.urls()!.file(this.item()!.path) : null);
    protected readonly downloadUrl = computed(() => this.item() ? this.urls()!.download(this.item()!.path) : null);
    /** The item can be opened in the external player (videos and audio, when the server has one for this session). */
    protected readonly external = computed(() => Boolean(this.item() && this.item()!.kind !== 'image' && this.urls()?.externalName));

    /** The picture can be edited (the right, and a server with the editor). */
    protected readonly editable = computed(() => this.item()?.kind === 'image' && this.config.can('files.edit') && Boolean(this.config.config()?.editor));

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

    open(item: LibraryItem): void {
        const position = this.store.media().findIndex(media => media.path === item.path);
        if (position < 0)
            return;
        const dialog = this.dialog().nativeElement;
        if (!dialog.open)
            dialog.showModal();
        this.show(position);
    }

    close(): void {
        this.dialog().nativeElement.close();
    }

    private show(index: number): void {
        const list = this.store.media();
        if (index < 0 || index >= list.length)
            return;
        this.stopPending();
        this.resetZoom();
        this.index.set(index);
        const item = list[index];
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
        const target = this.index() + step;
        if (target >= this.store.media().length && this.store.hasMore()) {
            this.loadingMore.set(true);
            try {
                await this.store.loadMore();
            } finally {
                this.loadingMore.set(false);
            }
        }
        this.show(target);
    }

    protected onClose(): void {
        this.stopPending();
        this.resetZoom();
        this.index.set(-1);
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
        if (item && this.config.can('tags.view'))
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
        this.close();
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
        const onControls = (event.target as Element).closest?.('.player-controls');
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
        if (event.ctrlKey || (event.target as Element).closest?.('.player-controls'))
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
