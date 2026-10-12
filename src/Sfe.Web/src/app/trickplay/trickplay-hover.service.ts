import { computed, DestroyRef, effect, inject, Injectable } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { TrickplayManifest } from '../core/models';
import { ListingStore } from '../library/listing.store';

const HOVER_DELAY_MS = 300;         // before a hovered thumbnail asks for its trickplay
const HOVER_FRAME_MS = 250;         // 4 frames a second
const HOVER_MAX_FRAMES = 40;        // a loop goes through the whole video in at most ~10 s
const HOLD_DELAY_MS = 350;          // touch: pressing this long, without sliding, plays the preview
const HOLD_SLOP_PX = 10;            // finger movement still counted as holding still
const HOLD_CLICK_MS = 600;          // after a hold, the click the browser may send on release is dropped

/**
 * Plays the existing trickplay of a video over its thumbnail while the mouse rests on its tile or row (touch: while
 * a finger holds it): frames picked evenly along the whole video, looping, with a thin bar showing where they are.
 * Only what exists is shown: hovering doesn't queue a video. One preview plays at a time; the descriptions are kept
 * for the listing (`Trickplay:HoverPreview`, off in the settings).
 */
@Injectable({ providedIn: 'root' })
export class TrickplayHoverService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    /** Path → promise of the description, or of null when there is none. */
    private readonly manifests = new Map<string, Promise<TrickplayManifest | null>>();
    /** Path → the description once answered, for decisions that can't wait. */
    private readonly known = new Map<string, TrickplayManifest | null>();
    /** The playing preview's `stop`. */
    private active: (() => void) | null = null;
    private readonly enabled = computed(() => {
        const config = this.config.config();
        return Boolean(config?.features.trickplay && config.trickplayHover);
    });

    constructor() {
        // A new listing: videos may have got (new) trickplay since.
        effect(() => {
            this.store.generation();
            this.manifests.clear();
            this.known.clear();
        });
    }

    /**
     * Hovering `card` (the listing's tile or row) plays the trickplay of the video at `path()` in `container()` (its
     * thumbnail's box, positioned). Touch screens have no hover: pressing and holding the card plays it until the
     * finger lifts; a tap still opens the video and a scroll cancels the hold (the browser takes the touch over).
     * Stops listening when `destroyRef` is destroyed.
     */
    attach(card: HTMLElement, container: () => HTMLElement, path: () => string, destroyRef: DestroyRef): void {
        let hover: { timer: number; stop: (() => void) | null; touch: boolean } | null = null;
        let origin: { x: number; y: number } | null = null;     // where the finger went down
        let swallowClick = false;                               // the click that follows a hold

        card.classList.add('trickplay-holdable');

        const start = (delay: number, touch: boolean) => {
            const current = hover = { timer: 0, stop: null as (() => void) | null, touch };
            current.timer = setTimeout(async () => {
                const info = await this.load(path());
                if (hover === current && info && info.count > 0) {
                    this.active?.();
                    current.stop = this.active = this.play(container(), path(), info);
                }
            }, delay);
        };

        const leave = () => {
            if (!hover)
                return;
            clearTimeout(hover.timer);
            hover.stop?.();
            hover = null;
        };

        const listeners: [string, (event: never) => void, boolean?][] = [
            ['pointerenter', (event: PointerEvent) => {
                // Moving across the listing doesn't ask for anything.
                if (event.pointerType !== 'touch' && this.enabled())
                    start(HOVER_DELAY_MS, false);
            }],
            ['pointerleave', (event: PointerEvent) => {
                if (event.pointerType !== 'touch')
                    leave();
            }],
            ['pointerdown', (event: PointerEvent) => {
                if (event.pointerType !== 'touch' || !event.isPrimary || !this.enabled())
                    return;
                void this.load(path());     // asked while the finger rests, so the frames come right after the hold delay
                origin = { x: event.clientX, y: event.clientY };
                start(HOLD_DELAY_MS, true);
            }],
            ['pointermove', (event: PointerEvent) => {
                // A finger sliding before the hold is up is a scroll or a swipe, not a hold.
                if (hover?.touch && !hover.stop && origin && Math.hypot(event.clientX - origin.x, event.clientY - origin.y) > HOLD_SLOP_PX)
                    leave();
            }],
            ['pointerup', () => release()],
            ['pointercancel', () => release()],
            // A long press opens the browser's menu for the link (Android), which would end the touch.
            ['contextmenu', (event: Event) => {
                if (hover?.touch && (hover.stop || (this.known.get(path())?.count ?? 0) > 0))
                    event.preventDefault();
            }],
            // Capturing, so it comes before the link's own handler.
            ['click', (event: MouseEvent) => {
                if (swallowClick) {
                    swallowClick = false;
                    event.preventDefault();
                    event.stopImmediatePropagation();
                } else if ((event.target as Element).closest('a')) {
                    leave();    // opening the viewer leaves the pointer where it is; the check box and tag button don't
                }
            }, true],
        ];

        const release = () => {
            if (!hover?.touch)
                return;
            // The hold played the preview: lifting the finger doesn't also open the video.
            if (hover.stop) {
                swallowClick = true;
                setTimeout(() => swallowClick = false, HOLD_CLICK_MS);
            }
            leave();
        };

        for (const [type, listener, capture] of listeners)
            card.addEventListener(type, listener as EventListener, capture);
        destroyRef.onDestroy(() => {
            leave();
            for (const [type, listener, capture] of listeners)
                card.removeEventListener(type, listener as EventListener, capture);
        });
    }

    private load(path: string): Promise<TrickplayManifest | null> {
        let manifest = this.manifests.get(path);
        if (!manifest) {
            manifest = this.api.existingTrickplay(path).catch(() => null).then(info => {
                this.known.set(path, info);
                return info;
            });
            this.manifests.set(path, manifest);
        }
        return manifest;
    }

    private play(container: HTMLElement, path: string, info: TrickplayManifest): () => void {
        const { tileWidth: width, tileHeight: height, columns } = info;
        const perSheet = columns * info.rows;
        const step = Math.max(1, Math.ceil(info.count / HOVER_MAX_FRAMES));
        const urls = this.config.links;
        const sheets = Array.from({ length: info.sheets }, (_, index) => Object.assign(new Image(), { src: urls.trickplaySheet(path, index, info.version) }));
        const canvas = Object.assign(document.createElement('canvas'), { className: 'trickplay-hover', width, height });
        const progress = Object.assign(document.createElement('span'), { className: 'trickplay-hover-progress' });
        const bar = progress.appendChild(document.createElement('span'));
        const context = canvas.getContext('2d')!;
        let frame = 0;
        let shown = false;

        const tick = () => {
            if (!container.isConnected) {
                stop();
                return;
            }
            const sheet = sheets[Math.floor(frame / perSheet)];
            if (!sheet.complete || !sheet.naturalWidth)
                return;     // same frame again once its sheet is there
            const cell = frame % perSheet;
            context.drawImage(sheet, (cell % columns) * width, Math.floor(cell / columns) * height, width, height, 0, 0, width, height);
            bar.style.width = `${(100 * (frame + 1) / info.count).toFixed(1)}%`;
            if (!shown) {
                // Under the marks (badge, HR/LR, trickplay), over the thumbnail.
                const before = container.querySelector(':scope > .preview-marks, :scope > .resolution-mark, :scope > .trickplay-mark');
                container.insertBefore(canvas, before);
                container.insertBefore(progress, before);
                shown = true;
            }
            frame += step;
            if (frame >= info.count)
                frame = 0;
        };

        const timer = setInterval(tick, HOVER_FRAME_MS);
        const stop = () => {
            clearInterval(timer);
            canvas.remove();
            progress.remove();
            if (this.active === stop)
                this.active = null;
        };
        tick();
        return stop;
    }
}
