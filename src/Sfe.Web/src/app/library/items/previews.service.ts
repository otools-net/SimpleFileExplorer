import { inject, Injectable, signal } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { LibraryItem } from '../../core/models';

/**
 * Previews of the listed files (thumbnails or originals), and the tile shapes learned from them: a tile is as wide
 * as its preview at the row height (see styles/items.css).
 */
@Injectable({ providedIn: 'root' })
export class PreviewsService {
    private readonly config = inject(ConfigService);
    /** Preview shape (width / height) by path, learned as previews load. */
    private readonly ratios = new Map<string, number>();
    private guessFrame = 0;

    /**
     * The typical preview shape of the listed files (geometric mean of the known ones): tiles whose preview hasn't
     * loaded yet take it, and it sets the row height of the justified layouts.
     */
    readonly ratioGuess = signal<number | null>(null);

    /**
     * The sources of a preview, tried in turn. Images use the original first, with the thumbnail as the fallback for
     * formats the browser can't show (e.g. TIFF). Large images only use their thumbnail: a folder of 20 MB photos
     * would otherwise download gigabytes. `original`: images use the original whatever their size (the one-per-row
     * view with `config.singleViewOriginals`, where a thumbnail would be blown up).
     */
    sources(item: LibraryItem, original = false): string[] {
        const config = this.config.value;
        const urls = this.config.links;
        const thumbnail = config.features.thumbnails ? urls.thumbnail(item.path) : null;
        const sources = item.kind === 'image'
            ? (this.config.isLargeImage(item) && !original ? [thumbnail] : [urls.file(item.path), thumbnail])
            : item.kind === 'video' ? [thumbnail] : [];
        return sources.filter((source): source is string => Boolean(source));
    }

    /** Known from an earlier render (other layout, back navigation): sized right away instead of on load. */
    knownRatio(path: string): number | null {
        return this.ratios.get(path) ?? null;
    }

    learn(path: string, ratio: number): void {
        this.ratios.set(path, ratio);
    }

    /** Works out `ratioGuess` for `items` on the next frame (once for a burst of loads). */
    scheduleGuess(items: () => LibraryItem[]): void {
        cancelAnimationFrame(this.guessFrame);
        this.guessFrame = requestAnimationFrame(() => {
            let sum = 0;
            let count = 0;
            for (const item of items()) {
                const ratio = this.ratios.get(item.path);
                if (ratio) {
                    sum += Math.log(Math.min(Math.max(ratio, 0.25), 4));    // a panorama shouldn't skew the rows
                    count++;
                }
            }
            this.ratioGuess.set(count ? Number(Math.exp(sum / count).toFixed(3)) : null);
        });
    }
}
