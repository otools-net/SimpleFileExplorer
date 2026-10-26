import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatElapsed, formatTime, joinParts, plural } from '../core/format';
import { TrickplayActivity } from '../core/models';
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
    template: `
        <div class="settings-status" aria-live="polite" [attr.data-state]="activity()?.status?.state">
            <div class="settings-status-line"><span class="settings-status-dot"></span><span class="settings-status-text">{{ stateText() }}</span></div>
            <div class="trickplay-progress" [hidden]="activity()?.status?.state !== 'running'" [class.indeterminate]="progress().indeterminate">
                <span [style.width]="progress().width"></span>
            </div>
            <span class="field-hint">{{ counts() }}</span>
            <div class="settings-action inline">
                <button type="button" class="button small" title="Go over the trickplay folders now for videos without trickplay"
                        [sfeBusy]="checking()" [disabled]="checking() || cannotCheck()" (click)="check()">Look for new videos now</button>
            </div>
        </div>
        @for (section of sections(); track section.title) {
            <section class="trickplay-section">
                <header><h3>{{ section.title }}</h3><span class="trickplay-count">{{ section.total ? section.total.toLocaleString() : '' }}</span></header>
                <ul class="trickplay-list">
                    @for (item of section.rows; track $index) {
                        <li class="trickplay-row" [class]="item.status" [title]="item.path">
                            @if (item.status === 'running') {
                                <span class="trickplay-spinner"></span>
                            } @else {
                                <svg [sfeIcon]="icons[item.status]"></svg>
                            }
                            <span class="trickplay-file">
                                <span class="trickplay-name">{{ item.name }}</span>
                                @if (item.folder) {
                                    <span class="trickplay-folder">{{ item.folder }}</span>
                                }
                                @if (item.note) {
                                    <span class="trickplay-error">{{ item.note }}</span>
                                }
                            </span>
                            @if (item.detail) {
                                <span class="trickplay-detail">{{ item.detail }}</span>
                            }
                        </li>
                    }
                    @if (section.total > section.rows.length) {
                        <li class="trickplay-more">and {{ more(section) }}</li>
                    }
                </ul>
                <p class="muted" [hidden]="section.total > 0">{{ section.empty }}</p>
            </section>
        }
    `,
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
        if (error)
            return `Could not read the activity: ${error}`;
        const status = this.activity()?.status;
        if (!status)
            return '…';
        const left = status.current.length + status.queued + status.pending;
        switch (status.state) {
            case 'disabled': return 'Turned off: tick “Create trickplay” in the Settings tab and save.';
            case 'unavailable': return 'ffmpeg can\'t be found: set its path (Thumbnails:FfmpegPath) in the server\'s configuration file.';
            case 'running': return status.discovering ? 'Looking for videos without trickplay…' : `Creating trickplay: ${plural(left, 'video')} left`;
            default: return status.lastPassFinished ? 'Up to date.' : 'Waiting.';
        }
    });
    /** While the pass still looks for videos the total isn't known: the bar sweeps instead. */
    protected readonly progress = computed(() => {
        const status = this.activity()?.status;
        if (!status)
            return { indeterminate: true, width: '0%' };
        const done = status.generated + status.failed;
        const left = status.current.length + status.queued + status.pending;
        return {
            indeterminate: status.discovering || done + left === 0,
            width: `${done + left ? (100 * done / (done + left)).toFixed(1) : 0}%`,
        };
    });
    protected readonly counts = computed(() => {
        const status = this.activity()?.status;
        return status ? joinParts([
            plural(status.checked, 'video') + ' checked',
            plural(status.generated, 'created', 'created'),
            status.failed ? plural(status.failed, 'failed', 'failed') : null,
            status.lastPassFinished ? `last complete check ${formatDate(status.lastPassFinished)}` : null,
        ]) : '';
    });
    protected readonly cannotCheck = computed(() => {
        const state = this.activity()?.status.state;
        return state === 'disabled' || state === 'unavailable';
    });
    protected readonly sections = computed((): Section[] => {
        const activity = this.activity();
        if (!activity)
            return [];
        const { status } = activity;
        return [
            {
                title: 'Working on',
                rows: activity.running.map(r => row(r.path, 'running', formatElapsed((Date.now() - Date.parse(r.started)) / 1000))),
                total: activity.running.length,
                empty: 'Nothing right now.',
            },
            {
                title: 'Waiting',
                rows: [
                    ...activity.queued.map(path => row(path, 'pending', 'opened in the player')),
                    ...activity.pending.map(path => row(path, 'pending', null)),
                ],
                total: status.queued + status.pending,
                empty: status.discovering ? 'Looking for videos…' : 'No video is waiting.',
            },
            {
                title: 'Recently finished',
                rows: activity.recent.map(r => row(r.path, r.succeeded ? 'done' : 'failed', `${formatElapsed(r.seconds)} · ${formatTime(r.finished)}`, r.succeeded ? null : r.error)),
                total: activity.recent.length,
                empty: 'No video processed since the server started.',
            },
        ];
    });

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => this.refresh(), ACTIVITY_REFRESH_MS));
    }

    protected more(section: Section): string {
        return plural(section.total - section.rows.length, 'more video', 'more videos');
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
