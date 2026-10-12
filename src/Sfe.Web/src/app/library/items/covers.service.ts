import { effect, inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { LibraryItem } from '../../core/models';
import { ListingStore } from '../listing.store';

const MAX_COVER_PATHS = 100;    // folders per covers request (LibraryBrowser.MaxCoverPaths)

/**
 * Folder covers: a card shows the folder's first pictures. They are asked for as the cards come near the screen,
 * the cards seen together in one request, and kept for the current listing (a layout switch reuses them).
 */
@Injectable({ providedIn: 'root' })
export class CoversService {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    /** Folders scrolled near view, not asked for yet. */
    private readonly queue = new Set<string>();
    private readonly cards = new Map<Element, string>();
    private timer = 0;
    private readonly observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            const path = this.cards.get(entry.target);
            if (entry.isIntersecting && path !== undefined) {
                this.unwatch(entry.target);
                this.queue.add(path);
            }
        }
        if (this.queue.size && !this.timer)
            this.timer = setTimeout(() => this.fetch());
    }, { rootMargin: '600px 0px' });

    /** Folder path → its first pictures, for the current listing. */
    readonly covers = signal<ReadonlyMap<string, LibraryItem[]>>(new Map());

    constructor() {
        // A new listing: nothing known or pending any more.
        effect(() => {
            this.store.generation();
            this.queue.clear();
            clearTimeout(this.timer);
            this.timer = 0;
            this.covers.set(new Map());
        });
    }

    /** Asks for the cover of the folder at `path` once `card` comes near the screen, unless it is known. */
    watch(card: Element, path: string): void {
        if (this.covers().has(path))
            return;
        this.cards.set(card, path);
        this.observer.observe(card);
    }

    unwatch(card: Element): void {
        this.cards.delete(card);
        this.observer.unobserve(card);
    }

    private async fetch(): Promise<void> {
        this.timer = 0;
        const generation = this.store.generation();
        const paths = [...this.queue].slice(0, MAX_COVER_PATHS);
        for (const path of paths)
            this.queue.delete(path);
        if (this.queue.size)
            this.timer = setTimeout(() => this.fetch());

        let result;
        try {
            result = await this.api.folderCovers(paths, this.store.signal);
        } catch {
            return;     // cancelled by navigating away, or the cards keep their folder icon
        }
        if (generation !== this.store.generation())
            return;
        const found = new Map(result.map(cover => [cover.path, cover.items]));
        this.covers.update(covers => {
            const next = new Map(covers);
            for (const path of paths)
                next.set(path, found.get(path) ?? []);
            return next;
        });
    }
}
