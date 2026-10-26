import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatElapsed, joinParts, plural } from '../core/format';
import { TaskInfo, TaskKind, TaskOverview, TaskRunSummary, TaskSchedule } from '../core/models';
import { ToastService } from '../core/toast.service';
import { visibleInterval } from '../core/visible-interval';
import { ListingStore } from '../library/listing.store';
import { ScanService } from '../library/scan/scan.service';
import { BusyDirective } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { ScheduleDraft, ScheduleFormComponent } from './schedule-form.component';
import { TaskHistoryComponent } from './task-history.component';
import {
    describeSchedule, formatInterval, formatMoment, formatRelative, NextRun, nextRunOf, OUTCOMES, scheduleStatus, target,
    TASK_ICONS, TASK_NAMES, TASK_PURPOSES,
} from './tasks';

const REFRESH_MS = 2000;

export type TasksTab = 'tasks' | 'schedules' | 'history';

const STATES: Record<TaskInfo['state'], string> = {
    idle: 'Idle',
    running: 'Running',
    disabled: 'Off',
    unavailable: 'Unavailable',
};

const CANCEL_QUESTIONS: Partial<Record<TaskKind, string>> = {
    scan: 'Cancel the scan? Folders already scanned stay indexed.',
    trickplay: 'Stop creating trickplay? The videos waiting are forgotten until the next check.',
};

/**
 * "Background tasks" on the administration page: the jobs the server runs on its own (scans, trickplay, similar-video
 * comparisons, thumbnails) and when each runs next, the schedules that start them, and the history of their runs with
 * logs, one per tab (`tab`; `selectTab` asks the page to show another). Refreshed while shown.
 */
@Component({
    selector: 'sfe-tasks-section',
    imports: [BusyDirective, IconComponent, ScheduleFormComponent, TaskHistoryComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tasks-section' },
    template: `
        @if (summary(); as summary) {
            <p class="task-summary" aria-live="polite">{{ summary }}</p>
        }
        @if (error()) {
            <p class="form-error">Could not read the tasks: {{ error() }}</p>
        }

        <div id="tasks-panel-tasks" class="task-cards" [hidden]="tab() !== 'tasks'">
            @for (task of overview()?.tasks ?? []; track task.task) {
                @let next = nextRun(task);
                <article class="task-card" [attr.data-state]="task.state">
                    <header class="task-card-head">
                        <span class="task-icon"><svg [sfeIcon]="icons[task.task]"></svg></span>
                        <div class="task-card-title">
                            <h3>{{ names[task.task] }}</h3>
                            <span class="task-state" [attr.data-state]="task.state">
                                @if (task.state === 'running') {
                                    <span class="trickplay-spinner"></span>
                                }
                                {{ states[task.state] }}{{ task.state === 'running' && task.startedAt ? ' · ' + elapsed(task.startedAt) : '' }}
                            </span>
                        </div>
                        <div class="task-card-actions">
                            @if (task.canCancel) {
                                <button type="button" class="button small danger" [title]="'Stop ' + names[task.task].toLowerCase() + (cancelQuestions[task.task] ? ' (asks first)' : '')"
                                        [sfeBusy]="busy() === task.task + ':cancel'" [disabled]="busy() !== null" (click)="cancel(task)">Cancel</button>
                            }
                            @if (task.task !== 'thumbnails') {
                                <button type="button" class="button small" [title]="startTitle(task)" [sfeBusy]="busy() === task.task + ':start'"
                                        [disabled]="busy() !== null || !task.canStart" (click)="start(task)"><svg sfeIcon="play"></svg>Run now</button>
                            }
                            @if (task.canSchedule) {
                                <button type="button" class="button small" [title]="'Plan runs of ' + names[task.task].toLowerCase() + ' at set times'" (click)="newSchedule(task.task)"><svg sfeIcon="clock"></svg>Schedule</button>
                            }
                            @if (task.task === 'trickplay') {
                                <button type="button" class="icon-button small ghost" aria-label="Details" title="The videos being given trickplay, and those waiting" (click)="openTrickplay.emit()"><svg sfeIcon="more"></svg></button>
                            }
                        </div>
                    </header>
                    <p class="task-purpose">{{ purposes[task.task] }}</p>
                    @if (task.state === 'running') {
                        <div class="trickplay-progress" [class.indeterminate]="task.progress === null">
                            <span [style.width]="((task.progress ?? 0) * 100).toFixed(1) + '%'"></span>
                        </div>
                    }
                    <p class="task-detail">
                        @if (task.state === 'running' && target(task.task, task.path); as where) {
                            <strong>{{ where }}:</strong>
                        }
                        {{ task.detail }}
                    </p>
                    <dl class="task-times">
                        @if (task.lastRun; as last) {
                            <div>
                                <dt>Last run</dt>
                                <dd>
                                    <button type="button" class="link-button" title="Show this run and its log in the history" (click)="showRun(last)">
                                        <span class="task-outcome" [class]="last.outcome">{{ outcomes[last.outcome] }}</span>
                                        <time [attr.datetime]="last.started" [title]="formatDate(last.started)">{{ formatMoment(last.started, now()) }}</time>
                                    </button>
                                </dd>
                            </div>
                        }
                        @if (task.canSchedule) {
                            <div class="task-next" [class.planned]="next">
                                <dt>Next run</dt>
                                <dd>
                                    @if (next) {
                                        <time [attr.datetime]="next.at" [title]="formatDate(next.at)">{{ formatMoment(next.at, now()) }}</time>
                                        <span class="task-relative">{{ formatRelative(next.at, now()) }}</span>
                                        <span class="field-hint">{{ nextSource(task, next) }}</span>
                                    } @else {
                                        <span class="field-hint">Not planned: only when started{{ task.task === 'scan' ? ' from the menu or here' : ' by hand' }}.</span>
                                    }
                                </dd>
                            </div>
                        }
                    </dl>
                </article>
            } @empty {
                @if (!error()) {
                    <p class="muted">…</p>
                }
            }
        </div>

        <div id="tasks-panel-schedules" class="task-schedules" [hidden]="tab() !== 'schedules'">
            <div class="task-schedules-intro">
                <p class="settings-intro">Start tasks at set times, once or again and again. Times are those of this browser{{ zoneNote() }}. A run that comes due while the task is already running, or while the server is off, is skipped.</p>
                @if (!draft()) {
                    <button type="button" class="button small primary" title="Add a schedule" (click)="newSchedule('scan')"><svg sfeIcon="plus"></svg>New schedule</button>
                }
            </div>
            @if (draft(); as draft) {
                <sfe-schedule-form [draft]="draft" [now]="now()" (saved)="onSaved($event)" (cancelled)="closeForm()" />
            }
            <ul class="schedule-list">
                @for (schedule of sortedSchedules(); track schedule.id) {
                    <li class="schedule-row" [class.off]="!schedule.enabled">
                        <label class="schedule-toggle" [title]="schedule.enabled ? 'Turn this schedule off (kept, but it doesn\\'t run)' : 'Turn this schedule on'">
                            <input type="checkbox" [checked]="schedule.enabled" (change)="toggle(schedule)">
                            <span class="visually-hidden">On</span>
                        </label>
                        <span class="task-icon small"><svg [sfeIcon]="icons[schedule.task]"></svg></span>
                        <span class="schedule-main">
                            <span class="schedule-title">
                                <strong>{{ names[schedule.task] }}</strong>
                                @if (target(schedule.task, schedule.path); as where) {
                                    <span>{{ where }}</span>
                                }
                            </span>
                            <span class="schedule-when">{{ describe(schedule) }}</span>
                        </span>
                        <span class="schedule-next">
                            @if (schedule.nextRun) {
                                <time [attr.datetime]="schedule.nextRun" [title]="formatDate(schedule.nextRun)">{{ formatMoment(schedule.nextRun, now()) }}</time>
                                <span class="task-relative">{{ formatRelative(schedule.nextRun, now()) }}</span>
                            } @else {
                                <span class="field-hint">{{ status(schedule) }}</span>
                            }
                            @if (schedule.lastRun) {
                                <span class="field-hint" [title]="formatDate(schedule.lastRun)">last {{ formatMoment(schedule.lastRun, now()) }}</span>
                            }
                        </span>
                        <span class="schedule-actions">
                            <button type="button" class="icon-button small ghost" aria-label="Edit" title="Change this schedule" (click)="edit(schedule)"><svg sfeIcon="settings"></svg></button>
                            <button type="button" class="icon-button small ghost danger" aria-label="Delete" title="Delete this schedule (asks first)" (click)="remove(schedule)"><svg sfeIcon="close"></svg></button>
                        </span>
                    </li>
                }
            </ul>
            @if (overview() && !overview()!.schedules.length && !draft()) {
                <p class="muted task-empty">No schedules yet. Trickplay still checks for new videos by itself{{ trickplayEvery() }}.</p>
            }
        </div>

        <sfe-task-history id="tasks-panel-history" [hidden]="tab() !== 'history'"
                          [retentionDays]="overview()?.logRetentionDays ?? 30" (openSettings)="selectTab.emit('settings')" />
    `,
})
export class TasksSectionComponent {
    /** The tab shown: tasks, schedules or history. */
    readonly tab = input.required<string>();
    /** Asks the page to show another tab of the section (e.g. `settings`). */
    readonly selectTab = output<string>();
    /** Asks the page to show the trickplay activity. */
    readonly openTrickplay = output<void>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly store = inject(ListingStore);
    private readonly scan = inject(ScanService);
    private readonly history = viewChild.required(TaskHistoryComponent);

    protected readonly names = TASK_NAMES;
    protected readonly purposes = TASK_PURPOSES;
    protected readonly icons = TASK_ICONS;
    protected readonly states = STATES;
    protected readonly outcomes = OUTCOMES;
    protected readonly cancelQuestions = CANCEL_QUESTIONS;
    protected readonly target = target;
    protected readonly plural = plural;
    protected readonly formatDate = formatDate;
    protected readonly formatMoment = formatMoment;
    protected readonly formatRelative = formatRelative;
    protected readonly describe = describeSchedule;

    protected readonly overview = signal<TaskOverview | null>(null);
    protected readonly error = signal<string | null>(null);
    /** `task:action` being sent. */
    protected readonly busy = signal<string | null>(null);
    protected readonly draft = signal<ScheduleDraft | null>(null);
    /** Ticks the elapsed and relative times with each refresh. */
    protected readonly now = signal(Date.now());

    protected readonly runningCount = computed(() => this.overview()?.tasks.filter(t => t.state === 'running').length ?? 0);
    protected readonly sortedSchedules = computed(() => [...this.overview()?.schedules ?? []].sort((a, b) =>
        (a.nextRun ? Date.parse(a.nextRun) : Infinity) - (b.nextRun ? Date.parse(b.nextRun) : Infinity) || a.id - b.id));

    /** The counts on the tabs: tasks running, schedules. */
    readonly badges = computed((): Partial<Record<TasksTab, string>> => ({
        tasks: this.runningCount() ? String(this.runningCount()) : '',
        schedules: this.overview()?.schedules.length ? String(this.overview()!.schedules.length) : '',
    }));

    /** What runs and the next thing planned, at the top: the user sees when the server will be busy next. */
    protected readonly summary = computed(() => {
        const overview = this.overview();
        if (!overview)
            return null;
        const next = overview.tasks
            .map(task => ({ task, next: nextRunOf(task, overview.schedules) }))
            .filter((entry): entry is { task: TaskInfo; next: NextRun } => entry.next !== null)
            .sort((a, b) => Date.parse(a.next.at) - Date.parse(b.next.at))[0];
        const running = overview.tasks.filter(t => t.state === 'running').map(t => TASK_NAMES[t.task].toLowerCase());
        return joinParts([
            running.length ? `Running: ${running.join(', ')}` : 'Nothing running',
            next ? `next: ${TASK_NAMES[next.task.task].toLowerCase()}${next.next.schedule?.task === 'scan' ? ` of ${target('scan', next.next.schedule.path)}` : ''} ${formatMoment(next.next.at, this.now())}` : 'nothing planned',
        ]);
    });

    /** Weekly times follow the server's clock: said when its zone isn't the browser's. */
    protected readonly zoneNote = computed(() => {
        const zone = this.overview()?.timeZone;
        const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return zone && zone !== browser ? `; weekly and daily times follow the server's clock (${zone})` : '';
    });

    protected readonly trickplayEvery = computed(() => {
        const every = this.overview()?.tasks.find(t => t.task === 'trickplay')?.automaticEvery;
        return every ? ` every ${plural(every, 'hour')}` : ' when it is turned on';
    });

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => void this.refresh(), REFRESH_MS));
        // The runs are read when their tab is shown, then with each refresh.
        effect(() => {
            if (this.tab() === 'history')
                untracked(() => void this.history().refresh());
        });
    }

    protected nextRun(task: TaskInfo): NextRun | null {
        return nextRunOf(task, this.overview()?.schedules ?? []);
    }

    protected nextSource(task: TaskInfo, next: NextRun): string {
        if (!next.schedule)
            return task.automaticEvery ? `automatic check, ${formatInterval(task.automaticEvery * 60)} (Trickplay › Settings)` : 'automatic';
        const where = target(next.schedule.task, next.schedule.path);
        return `schedule: ${uncapitalize(describeSchedule(next.schedule))}${where && task.task === 'scan' ? `, ${where}` : ''}`;
    }

    protected elapsed(startedAt: string): string {
        return formatElapsed((this.now() - Date.parse(startedAt)) / 1000);
    }

    protected startTitle(task: TaskInfo): string {
        if (!task.canStart)
            return task.state === 'running' ? 'Already running' : 'Turned off, or ffmpeg is missing';
        return task.task === 'scan' ? 'Scan the folder browsed last and its sub-folders (asks first)' : `Start ${TASK_NAMES[task.task].toLowerCase()} now`;
    }

    protected async start(task: TaskInfo): Promise<void> {
        if (task.task === 'scan') {
            // Same as the menu's "Scan this folder", for the folder browsed last: asks, then its card shows the progress.
            await this.scan.start();
            void this.refresh();
            return;
        }
        this.busy.set(`${task.task}:start`);
        try {
            const result = await this.api.startTask(task.task);
            if (result.result === 'unavailable')
                this.toasts.error(`${TASK_NAMES[task.task]} can't run: it is turned off, or ffmpeg is missing.`);
            else if (result.result === 'alreadyRunning')
                this.toasts.show(task.task === 'trickplay' ? 'A new check follows the one running.' : `${TASK_NAMES[task.task]} is already running.`);
        } catch (error) {
            this.toasts.error(`Could not start ${TASK_NAMES[task.task].toLowerCase()}: ${(error as Error).message}`);
        } finally {
            this.busy.set(null);
        }
        void this.refresh();
    }

    protected async cancel(task: TaskInfo): Promise<void> {
        const question = CANCEL_QUESTIONS[task.task];
        if (question && !confirm(question))
            return;
        this.busy.set(`${task.task}:cancel`);
        try {
            await this.api.cancelTask(task.task);
        } catch (error) {
            this.toasts.error(`Could not stop ${TASK_NAMES[task.task].toLowerCase()}: ${(error as Error).message}`);
        } finally {
            this.busy.set(null);
        }
        void this.refresh();
    }

    protected newSchedule(task: TaskKind): void {
        this.draft.set({ task, path: task === 'scan' ? this.store.route().path : undefined });
        this.selectTab.emit('schedules');
    }

    protected edit(schedule: TaskSchedule): void {
        this.draft.set({ schedule });
        window.scrollTo({ top: 0 });
    }

    protected closeForm(): void {
        this.draft.set(null);
    }

    protected onSaved(schedule: TaskSchedule): void {
        this.draft.set(null);
        this.toasts.show(schedule.nextRun
            ? `${TASK_NAMES[schedule.task]} scheduled: next run ${formatMoment(schedule.nextRun)}.`
            : `${TASK_NAMES[schedule.task]} schedule saved.`);
        void this.refresh();
    }

    protected async toggle(schedule: TaskSchedule): Promise<void> {
        try {
            await this.api.updateSchedule(schedule.id, { ...schedule, enabled: !schedule.enabled });
        } catch (error) {
            this.toasts.error(`Could not change the schedule: ${(error as Error).message}`);
        }
        void this.refresh();
    }

    protected async remove(schedule: TaskSchedule): Promise<void> {
        if (!confirm(`Delete the schedule “${TASK_NAMES[schedule.task]}: ${describeSchedule(schedule)}”? Its past runs stay in the history.`))
            return;
        try {
            await this.api.deleteSchedule(schedule.id);
            if (this.draft()?.schedule?.id === schedule.id)
                this.draft.set(null);
        } catch (error) {
            this.toasts.error(`Could not delete the schedule: ${(error as Error).message}`);
        }
        void this.refresh();
    }

    protected status(schedule: TaskSchedule): string {
        return scheduleStatus(schedule, this.now());
    }

    protected showRun(run: TaskRunSummary): void {
        this.selectTab.emit('history');
        void this.history().show(run);
    }

    private async refresh(): Promise<void> {
        try {
            this.overview.set(await this.api.tasks());
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        }
        this.now.set(Date.now());
        if (this.tab() === 'history')
            void this.history().refresh();
    }
}

/** "Every day at 3:00" → "every day at 3:00", leaving the dates and times inside as they are. */
function uncapitalize(text: string): string {
    return text.charAt(0).toLowerCase() + text.slice(1);
}
