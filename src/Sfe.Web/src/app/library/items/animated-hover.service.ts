import { DestroyRef, effect, inject, Injectable } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { LibraryItem } from '../../core/models';
import { insertUnderMarks, TrickplayHoverService } from '../../trickplay/trickplay-hover.service';
import { ListingStore } from '../listing.store';

/** Formats that may hold an animation; the server tells whether a file does. */
const ANIMATABLE = new Set(['gif', 'png', 'apng', 'webp', 'avif']);
/** Originals kept once downloaded, so hovering one again shows it at once (images are sent `no-store`). */
const KEPT_ORIGINALS = 4;

/**
 * Plays an animated image (GIF, APNG, WebP, AVIF) that the listing shows through its still thumbnail (a large image)
 * while the mouse rests on its tile or row, or a finger holds it: the original is downloaded and shown over the
 * thumbnail, with a thin bar while it loads. Same gestures as the trickplay hover (`TrickplayHoverService`); off with
 * `Thumbnails:AnimatedHover`. Whether a file moves is asked once per listing.
 */
@Injectable({ providedIn: 'root' })
export class AnimatedHoverService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly hover = inject(TrickplayHoverService);
    /** Path → promise of whether the picture is animated. */
    private readonly checks = new Map<string, Promise<boolean>>();
    /** Path → the answer once it has come. */
    private readonly known = new Map<string, boolean>();
    /** Path → downloaded original, the most recent last. */
    private readonly originals = new Map<string, HTMLImageElement>();

    constructor() {
        // A new listing: files may have changed since.
        effect(() => {
            this.store.generation();
            this.checks.clear();
            this.known.clear();
            this.originals.clear();
        });
    }

    /**
     * Hovering `card` plays the image `item()` in `container()` (its preview's box) when `thumbnailShown()` (the
     * preview is the thumbnail, not the original, which the browser animates by itself). Stops with `destroyRef`.
     */
    attach(card: HTMLElement, container: () => HTMLElement, item: () => LibraryItem, thumbnailShown: () => boolean, destroyRef: DestroyRef): void {
        this.hover.attach(card, container, () => item().path, destroyRef, {
            enabled: () => Boolean(this.config.config()?.animatedHover) && thumbnailShown()
                && ANIMATABLE.has(item().extension?.toLowerCase() ?? ''),
            ready: path => this.check(path),
            isReady: path => this.known.get(path) === true,
            play: (box, path) => this.play(box, path),
        });
    }

    private check(path: string): Promise<boolean> {
        let check = this.checks.get(path);
        if (!check) {
            check = this.api.imageAnimation(path).then(info => info.animated, () => false).then(animated => {
                this.known.set(path, animated);
                return animated;
            });
            this.checks.set(path, check);
        }
        return check;
    }

    private play(container: HTMLElement, path: string): () => void {
        let image = this.originals.get(path);
        let loaded = Boolean(image);
        const loading = Object.assign(document.createElement('span'), { className: 'trickplay-hover-progress animated-hover-loading' });
        loading.appendChild(document.createElement('span'));

        if (!image) {
            // Hidden over the thumbnail until it has come, under a bar saying it is coming.
            const original = image = Object.assign(document.createElement('img'), { className: 'animated-hover', alt: '', decoding: 'async', hidden: true });
            original.onload = () => {
                loaded = true;
                original.hidden = false;
                loading.remove();
                this.keep(path, original);
            };
            original.onerror = () => {
                original.remove();
                loading.remove();
            };
            original.src = this.config.links.file(path);
        } else {
            this.keep(path, image);
        }
        insertUnderMarks(container, ...(loaded ? [image] : [image, loading]));

        return () => {
            loading.remove();
            image.remove();
            if (!loaded) {
                // Left before it came: the download stops.
                image.onload = image.onerror = null;
                image.removeAttribute('src');
            }
        };
    }

    /** Remembers a downloaded original as the most recent, forgetting the oldest beyond `KEPT_ORIGINALS`. */
    private keep(path: string, image: HTMLImageElement): void {
        this.originals.delete(path);
        this.originals.set(path, image);
        for (const old of this.originals.keys()) {
            if (this.originals.size <= KEPT_ORIGINALS)
                break;
            this.originals.delete(old);
        }
    }
}
