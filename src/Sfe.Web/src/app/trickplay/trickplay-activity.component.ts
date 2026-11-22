import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatElapsed, formatTime, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { TrickplayActivity } from './trickplay.models';
import { lastCheck, passCounts } from './trickplay-texts';
import { moreVideos } from '../tracking/tracking-texts';
import { ToastService } from '../core/toast.service';
import { visibleInterval } from '../core/visible-interval';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';

const ACTIVITY_REFRESH_MS = 2000;
const ACTIVITY_LIMIT = 100;

type RowStatus = 'running' | 'pending' | 'done' | 'failed';

/** A video's line: its name, its folder below, and on the right `detail`. */
interface Row {
    path: string;
    name: string;
    folder: string | null;
    status: RowStatus;
    detail: string | null;
    note: string | null;
}

interface Section {
    /** Stable, for tracking (the title changes with the language). */
    key: 'working' | 'waiting' | 'finished';
    title: string;
    rows: Row[];
    total: number;
    empty: string;
}

const ROW_ICONS: Record<RowStatus, string> = { running: '', pending: 'clock', done: 'check', failed: 'close' };

function row(path: string, status: RowStatus, detail: string | null, note: string | null = null): Row {
    const slash = path.lastIndexOf('/');
    return { path, name: path.slice(slash + 1), folder: slash > 0 ? path.slice(0, slash) : null, status, detail, note };
}

/**
 * How the background trickplay job is going (Trickplay › Activity on the administration page): what it works on, what
 * waits (videos opened in the player first, then those found by the pass) and the last videos finished. Refreshed
 * while shown.
 */
@Component({
    selector: 'sfe-trickplay-activity',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'trickplay-activity' },
    templateUrl: './trickplay-activity.component.html',
})
export class TrickplayActivityComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    protected readonly icons = ROW_ICONS;
    protected readonly activity = signal<TrickplayActivity | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly checking = signal(false);

    protected readonly stateText = computed(() => {
        const error = this.error();
        if (error) return $localize`Could not read the activity: ${error}:reason:`;
        const status = this.activity()?.status;
        if (!status) return '…';
        const left = status.current.length + status.queued + status.pending;
        switch (status.state) {
            case 'disabled':
                return $localize`Turned off: tick “Create trickplay” in the Settings tab and save.`;
            case 'unavailable':
                return $localize`ffmpeg can't be found: set its path (Thumbnails:FfmpegPath) in the server's \
configuration file.`;
            case 'running':
                return status.discovering
                    ? $localize`Looking for videos without trickplay…`
                    : icu(
                          $localize`{left, plural, one {Creating trickplay: # video left.} \
other {Creating trickplay: # videos left.}}`,
                          {
                              left,
                          },
                      );
            default:
                return status.lastPassFinished ? $localize`Up to date.` : $localize`Waiting.`;
        }
    });
    /** While the pass still looks for videos the total isn't known: the bar sweeps instead. */
    protected readonly progress = computed(() => {
        const status = this.activity()?.status;
        if (!status) return { indeterminate: true, width: '0%' };
        const done = status.generated + status.failed;
        const left = status.current.length + status.queued + status.pending;
        return {
            indeterminate: status.discovering || done + left === 0,
            width: `${done + left ? ((100 * done) / (done + left)).toFixed(1) : 0}%`,
        };
    });
    protected readonly counts = computed(() => {
        const status = this.activity()?.status;
        return status ? joinParts([...passCounts(status), lastCheck(status)]) : '';
    });
    protected readonly cannotCheck = computed(() => {
        const state = this.activity()?.status.state;
        return state === 'disabled' || state === 'unavailable';
    });
    protected readonly sections = computed((): Section[] => {
        const activity = this.activity();
        if (!activity) return [];
        const { status } = activity;
        return [
            {
                key: 'working',
                title: $localize`:videos whose trickplay is being made:Working on`,
                rows: activity.running.map((r) =>
                    row(r.path, 'running', formatElapsed((Date.now() - Date.parse(r.started)) / 1000)),
                ),
                total: activity.running.length,
                empty: $localize`No video is being processed right now.`,
            },
            {
                key: 'waiting',
                title: $localize`:videos waiting for their trickplay:Waiting`,
                rows: [
                    ...activity.queued.map((path) => row(path, 'pending', $localize`opened in the player`)),
                    ...activity.pending.map((path) => row(path, 'pending', null)),
                ],
                total: status.queued + status.pending,
                empty: status.discovering ? $localize`Looking for videos…` : $localize`No video is waiting.`,
            },
            {
                key: 'finished',
                title: $localize`Recently finished`,
                rows: activity.recent.map((r) =>
                    row(
                        r.path,
                        r.succeeded ? 'done' : 'failed',
                        `${formatElapsed(r.seconds)} · ${formatTime(r.finished)}`,
                        r.succeeded ? null : r.error,
                    ),
                ),
                total: activity.recent.length,
                empty: $localize`No video has been processed since the server started.`,
            },
        ];
    });

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => this.refresh(), ACTIVITY_REFRESH_MS));
    }

    protected more(section: Section): string {
        return moreVideos(section.total - section.rows.length);
    }

    protected async check(): Promise<void> {
        try {
            await whileBusy(this.checking, () => this.api.checkTrickplay());
        } catch (error) {
            this.toasts.error(`Could not start the check: ${(error as Error).message}`);
        }
        void this.refresh();
    }

    async refresh(): Promise<void> {
        try {
            this.activity.set(await this.api.trickplayActivity(ACTIVITY_LIMIT));
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }
}
