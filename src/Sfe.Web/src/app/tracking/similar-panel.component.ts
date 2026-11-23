import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDuration, formatSize } from '../core/format';
import { icu } from '../core/i18n/icu';
import { SimilarVideoResults } from './tracking.models';
import { readPreference, writePreference } from '../core/preferences';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf } from '../shared/events';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';
import { moreVideos, similarStatus } from './tracking-texts';

const SCOPE_KEY = 'sfe.similarVideos.folderOnly';
const POLL_MS = 1000;

type Group = SimilarVideoResults['groups'][number];

/**
 * Groups of videos showing the same pictures in other files (another resolution, quality or format, trimmed), from
 * the last comparison of the fingerprints made with the trickplay. The first video of a group is the longest; the
 * others show their match: the share of frames they have in common with it. A comparison runs on the server in the
 * background: the status is polled while `isOpen()`.
 */
@Component({
    selector: 'sfe-similar-panel',
    imports: [BusyDirective, StatusBoxComponent, TrackingFileComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel', role: 'tabpanel' },
    templateUrl: './similar-panel.component.html',
})
export class SimilarPanelComponent {
    readonly folder = input('');
    readonly isOpen = input(false);
    readonly openFolder = output<string>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    protected readonly checkedOf = checkedOf;
    protected readonly folderOnly = signal(readPreference(SCOPE_KEY) === '1');
    protected readonly groups = signal<Group[]>([]);
    protected readonly hasMore = signal(false);
    protected readonly loading = signal(false);
    protected readonly loadingMore = signal(false);
    protected readonly comparing = signal(false);
    private readonly result = signal<SimilarVideoResults | null>(null);
    private readonly failure = signal<string | null>(null);
    private next: string | null = null;
    private generation = 0;
    private poll = 0;

    protected readonly running = computed(() => this.result()?.status.state === 'running');
    protected readonly status = computed(() => similarStatus(this.result(), this.failure()));
    /** For the tab. */
    readonly badge = computed(() =>
        this.result()?.summary?.groups ? this.result()!.summary!.groups.toLocaleString() : '',
    );

    protected readonly size = formatSize;
    protected readonly duration = formatDuration;
    protected videosText = (count: number) =>
        icu($localize`{count, plural, one {# video} other {# videos}}`, { count });
    protected moreText = (group: Group) => moreVideos(group.count - group.videos.length);

    constructor() {
        inject(DestroyRef).onDestroy(() => clearTimeout(this.poll));
    }

    /** `quiet`: a poll during a comparison; the list is only drawn again once the comparison is over. */
    async load({ append = false, quiet = false } = {}): Promise<void> {
        clearTimeout(this.poll);
        const current = append ? this.generation : ++this.generation;
        if (!append && !quiet) {
            this.groups.set([]);
            this.hasMore.set(false);
            this.loading.set(true);
            this.next = null;
        }
        try {
            const result = await this.api.similarVideos({
                path: this.folderOnly() ? this.folder() : '',
                after: append ? this.next : null,
            });
            if (current !== this.generation) return;
            const running = result.status.state === 'running';
            if (running && this.isOpen()) this.poll = setTimeout(() => this.load({ quiet: true }), POLL_MS);
            if (append) {
                this.groups.update((groups) => [...groups, ...result.groups]);
            } else {
                this.result.set(result);
                this.failure.set(null);
                if (quiet && running) return;
                this.groups.set(result.groups);
            }
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current !== this.generation) return;
            if (append) {
                // The page of an older comparison: start over with the new one.
                this.toasts.error($localize`Could not load more similar videos: ${(error as Error).message}:reason:`);
                void this.load();
                return;
            }
            this.groups.set([]);
            this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation) this.loading.set(false);
        }
    }

    protected more(): void {
        void whileBusy(this.loadingMore, () => this.load({ append: true }));
    }

    protected async compare(): Promise<void> {
        try {
            await whileBusy(this.comparing, () => this.api.compareVideos());
        } catch (error) {
            this.toasts.error($localize`Could not compare the videos: ${(error as Error).message}:reason:`);
        }
        void this.load({ quiet: true });
    }

    protected setFolderOnly(only: boolean): void {
        this.folderOnly.set(only);
        writePreference(SCOPE_KEY, only ? '1' : '0');
        void this.load();
    }
}
