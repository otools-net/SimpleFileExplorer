import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatDuration, formatSize, joinParts, plural } from '../core/format';
import { SimilarVideoResults } from '../core/models';
import { readPreference, writePreference } from '../core/preferences';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf } from '../shared/events';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';

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
    template: `
        <p class="settings-intro">Videos showing the same pictures in other files: another resolution, quality or format, or trimmed. They are compared from the fingerprints made with their trickplay (Trickplay › Settings): indexed videos without trickplay yet are left out.</p>
        @if (folder()) {
            <label class="check tracking-scope">
                <input type="checkbox" [checked]="folderOnly()" (change)="setFolderOnly(checkedOf($event))">
                <span>Only groups with a video in “{{ folder() }}”</span>
            </label>
        }
        <sfe-status-box [state]="status().state" [text]="status().text" [detail]="status().detail">
            <div class="settings-action inline">
                <button type="button" class="button small" title="Compare the videos that have trickplay now, in the background"
                        [sfeBusy]="comparing()" [disabled]="comparing() || running()" (click)="compare()">Compare now</button>
            </div>
        </sfe-status-box>
        <div class="tracking-groups">
            @if (loading()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            }
            @for (group of groups(); track $index) {
                <section class="tracking-group">
                    <header class="tracking-group-head">
                        <strong>{{ videosText(group.count) }}</strong>
                        <span>{{ size(group.wastedBytes) }} more than the biggest</span>
                    </header>
                    <ul class="tracking-list">
                        @for (video of group.videos; track video.file.path) {
                            <li [sfeTrackingFile]="video.file" (openFolder)="openFolder.emit($event)">
                                <span class="tracking-similar">
                                    @if (video.match === null || video.match === undefined) {
                                        <span class="tracking-match reference" title="The longest video of the group: the others are compared to it">Reference</span>
                                    } @else {
                                        <span class="tracking-match" title="Frames in common with the reference, out of the longer of the two">{{ video.match }}% match</span>
                                    }
                                    <span class="tracking-media">{{ duration(video.duration) }} · {{ size(video.file.size) }}</span>
                                </span>
                            </li>
                        }
                    </ul>
                    @if (group.count > group.videos.length) {
                        <p class="tracking-more">and {{ moreText(group) }}</p>
                    }
                </section>
            }
        </div>
        <button type="button" class="button" title="Show the next page" [hidden]="!hasMore()" [sfeBusy]="loadingMore()" [disabled]="loadingMore()" (click)="more()">Load more</button>
    `,
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
    protected readonly status = computed(() => {
        const failure = this.failure();
        if (failure)
            return { state: 'unavailable', text: `Could not read the similar videos: ${failure}`, detail: '' };
        const result = this.result();
        if (!result)
            return { state: null, text: '…', detail: '' };
        const { status: run, summary } = result;
        const running = run.state === 'running';
        const where = result.path ? `with a video in “${result.path}”` : 'in the library';
        return {
            state: running ? 'running' : run.state === 'failed' ? 'unavailable' : summary?.groups ? 'found' : 'idle',
            text: running
                ? `Comparing videos… ${run.total ? `${run.checked.toLocaleString()} of ${plural(run.total, 'video')} read` : ''}`
                : run.state === 'failed' ? `The comparison failed: ${run.message}`
                    : !summary ? 'The videos have not been compared since SFE started.'
                        : summary.groups ? `${plural(summary.groups, 'group')} of similar videos ${where}: ${formatSize(summary.wastedBytes)} more than the biggest of each`
                            : `No similar videos ${where}.`,
            detail: joinParts([
                summary ? `${summary.compared.toLocaleString()} of ${plural(summary.videos, 'indexed video')} compared${summary.compared < summary.videos ? ' (the others have no trickplay yet)' : ''}` : null,
                summary ? `listed from ${summary.minimumPercent}% match` : null,
                run.state === 'cancelled' ? 'the last comparison was cancelled: these are the groups of the one before' : null,
                summary && run.state === 'completed' && run.finished ? `compared ${formatDate(run.finished)}` : null,
                !result.trickplayEnabled ? 'trickplay is off (Trickplay › Settings): videos get no new fingerprints' : null,
            ]),
        };
    });
    /** For the tab. */
    readonly badge = computed(() => this.result()?.summary?.groups ? this.result()!.summary!.groups.toLocaleString() : '');

    protected readonly size = formatSize;
    protected readonly duration = formatDuration;
    protected videosText = (count: number) => plural(count, 'video');
    protected moreText = (group: Group) => plural(group.count - group.videos.length, 'more video', 'more videos');

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
            const result = await this.api.similarVideos({ path: this.folderOnly() ? this.folder() : '', after: append ? this.next : null });
            if (current !== this.generation)
                return;
            const running = result.status.state === 'running';
            if (running && this.isOpen())
                this.poll = setTimeout(() => this.load({ quiet: true }), POLL_MS);
            if (append) {
                this.groups.update(groups => [...groups, ...result.groups]);
            } else {
                this.result.set(result);
                this.failure.set(null);
                if (quiet && running)
                    return;
                this.groups.set(result.groups);
            }
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current !== this.generation)
                return;
            if (append) {
                // The page of an older comparison: start over with the new one.
                this.toasts.error(`Could not load more: ${(error as Error).message}`);
                void this.load();
                return;
            }
            this.groups.set([]);
            this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation)
                this.loading.set(false);
        }
    }

    protected more(): void {
        void whileBusy(this.loadingMore, () => this.load({ append: true }));
    }

    protected async compare(): Promise<void> {
        try {
            await whileBusy(this.comparing, () => this.api.compareVideos());
        } catch (error) {
            this.toasts.error(`Could not compare the videos: ${(error as Error).message}`);
        }
        void this.load({ quiet: true });
    }

    protected setFolderOnly(only: boolean): void {
        this.folderOnly.set(only);
        writePreference(SCOPE_KEY, only ? '1' : '0');
        void this.load();
    }
}
