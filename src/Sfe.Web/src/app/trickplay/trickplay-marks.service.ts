import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { TrickplayFileState, TrickplayManifest } from '../core/models';
import { ListingStore } from '../library/listing.store';
import { waitForTrickplay } from './trickplay';

const MAX_STATE_PATHS = 5000;       // TrickplayService.MaxStatePaths
const BUSY_REFRESH_MS = 3000;       // while the job runs
const IDLE_REFRESH_MS = 20000;      // while it doesn't: only to notice a pass started elsewhere

/**
 * Which listed videos wait for their trickplay (clock mark) or are being processed (spinner). The states of the
 * loaded videos are asked for when the listing changes and now and then, more often while the job is busy.
 */
@Injectable({ providedIn: 'root' })
export class TrickplayMarksService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly enabled = computed(() => Boolean(this.config.config()?.features.trickplay));
    private readonly videoPaths = computed(() => this.store.items().filter(item => item.kind === 'video').map(item => item.path),
        { equal: (a, b) => a.length === b.length && a.every((path, i) => path === b[i]) });
    private timer = 0;
    private busy = false;
    private polling: Promise<void> | null = null;

    /** Path → state, for the videos with something going on. */
    readonly states = signal<ReadonlyMap<string, TrickplayFileState>>(new Map());

    constructor() {
        // A new page of the listing: ask for the states now.
        effect(() => {
            this.videoPaths();
            untracked(() => void this.poll());
        });
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden)
                void this.poll();
        });
    }

    /** See `waitForTrickplay`; the listing shows the clock of a video it queued. */
    load(path: string, isCurrent: () => boolean): Promise<TrickplayManifest | null> {
        if (!this.enabled())
            return Promise.resolve(null);
        return waitForTrickplay(this.api, path, isCurrent, () => {
            this.busy = true;
            void this.poll();
        });
    }

    private schedule(): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => this.poll(), this.busy ? BUSY_REFRESH_MS : IDLE_REFRESH_MS);
    }

    private async poll(): Promise<void> {
        clearTimeout(this.timer);
        if (this.polling)
            return this.polling;
        this.polling = (async () => {
            const paths = this.enabled() && !document.hidden ? this.videoPaths().slice(0, MAX_STATE_PATHS) : [];
            let next = new Map<string, TrickplayFileState>();
            if (paths.length) {
                try {
                    const result = await this.api.trickplayStates(paths);
                    this.busy = result.running;
                    next = new Map(Object.entries(result.files));
                } catch {
                    next = new Map(this.states());  // keep the marks through a hiccup
                }
            }
            const states = this.states();
            if (states.size !== next.size || [...next].some(([path, state]) => states.get(path) !== state))
                this.states.set(next);
        })();
        try {
            await this.polling;
        } finally {
            this.polling = null;
            this.schedule();
        }
    }
}
