import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    effect,
    inject,
    input,
    output,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatElapsed, joinParts } from '../core/format';
import { textLocale } from '../core/i18n/icu';
import { TaskInfo, TaskKind, TaskRunSummary } from '../core/task.models';
import { TaskOverview, TaskSchedule } from './task.models';
import { ToastService } from '../core/toast.service';
import { visibleInterval } from '../core/visible-interval';
import { ListingStore } from '../library/listing.store';
import { ScanService } from '../library/scan/scan.service';
import { BusyDirective } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { ScheduleDraft, ScheduleFormComponent } from './schedule-form.component';
import { TaskHistoryComponent } from './task-history.component';
import {
    describeSchedule,
    formatInterval,
    formatMoment,
    formatRelative,
    NextRun,
    nextRunOf,
    OUTCOMES,
    scheduleStatus,
    target,
    TASK_ICONS,
    TASK_NAMES,
    TASK_PURPOSES,
} from './tasks';

const REFRESH_MS = 2000;

export type TasksTab = 'tasks' | 'schedules' | 'history';

const STATES: Record<TaskInfo['state'], string> = {
    idle: $localize`:state of a background task:Idle`,
    running: $localize`:state of a background task:Running`,
    disabled: $localize`:state of a background task:Off`,
    unavailable: $localize`:state of a background task:Unavailable`,
};

const CANCEL_QUESTIONS: Partial<Record<TaskKind, string>> = {
    scan: $localize`Cancel the scan? Folders already scanned stay indexed.`,
    trickplay: $localize`Stop making trickplay? The videos waiting are left until the next check.`,
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
    templateUrl: './tasks-section.component.html',
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
    protected readonly formatDate = formatDate;
    protected readonly formatMoment = formatMoment;
    protected readonly formatRelative = formatRelative;
    protected readonly describe = describeSchedule;
    protected readonly toggleTitles = {
        on: $localize`Turn this schedule off (it is kept but doesn't run)`,
        off: $localize`Turn this schedule on`,
    };

    protected readonly overview = signal<TaskOverview | null>(null);
    protected readonly error = signal<string | null>(null);
    /** `task:action` being sent. */
    protected readonly busy = signal<string | null>(null);
    protected readonly draft = signal<ScheduleDraft | null>(null);
    /** Ticks the elapsed and relative times with each refresh. */
    protected readonly now = signal(Date.now());

    protected readonly runningCount = computed(
        () => this.overview()?.tasks.filter((t) => t.state === 'running').length ?? 0,
    );
    protected readonly sortedSchedules = computed(() =>
        [...(this.overview()?.schedules ?? [])].sort(
            (a, b) =>
                (a.nextRun ? Date.parse(a.nextRun) : Infinity) - (b.nextRun ? Date.parse(b.nextRun) : Infinity) ||
                a.id - b.id,
        ),
    );

    /** The counts on the tabs: tasks running, schedules. */
    readonly badges = computed((): Partial<Record<TasksTab, string>> => ({
        tasks: this.runningCount() ? String(this.runningCount()) : '',
        schedules: this.overview()?.schedules.length ? String(this.overview()!.schedules.length) : '',
    }));

    /** What runs and the next thing planned, at the top: the user sees when the server will be busy next. */
    protected readonly summary = computed(() => {
        const overview = this.overview();
        if (!overview) return null;
        const next = overview.tasks
            .map((task) => ({ task, next: nextRunOf(task, overview.schedules) }))
            .filter((entry): entry is { task: TaskInfo; next: NextRun } => entry.next !== null)
            .sort((a, b) => Date.parse(a.next.at) - Date.parse(b.next.at))[0];
        const running = overview.tasks.filter((t) => t.state === 'running').map((t) => TASK_NAMES[t.task]);
        const list = new Intl.ListFormat(textLocale(), { type: 'conjunction' }).format(running);
        return joinParts([
            running.length ? $localize`Running: ${list}:tasks:` : $localize`Nothing running`,
            next ? nextRunText(next.task, next.next, this.now()) : $localize`Nothing planned`,
        ]);
    });

    /** Weekly times follow the server's clock: its zone, when it isn't the browser's. */
    protected readonly serverZone = computed(() => {
        const zone = this.overview()?.timeZone;
        const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return zone && zone !== browser ? zone : null;
    });

    /** Hours between trickplay's own checks; null when it doesn't check by itself. */
    protected readonly trickplayEvery = computed(
        () => this.overview()?.tasks.find((t) => t.task === 'trickplay')?.automaticEvery ?? null,
    );

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => void this.refresh(), REFRESH_MS));
        // The runs are read when their tab is shown, then with each refresh.
        effect(() => {
            if (this.tab() === 'history') untracked(() => void this.history().refresh());
        });
    }

    protected nextRun(task: TaskInfo): NextRun | null {
        return nextRunOf(task, this.overview()?.schedules ?? []);
    }

    protected nextSource(task: TaskInfo, next: NextRun): string {
        if (!next.schedule) {
            if (!task.automaticEvery) return $localize`:a run the task plans itself:Automatic`;
            const every = formatInterval(task.automaticEvery * 60);
            return $localize`Automatic check, ${every}:interval: (Trickplay › Settings)`;
        }
        const when = describeSchedule(next.schedule);
        const where = task.task === 'scan' ? target('scan', next.schedule.path) : null;
        return where
            ? $localize`From the schedule “${when}:schedule:”, scanning ${where}:folder:`
            : $localize`From the schedule “${when}:schedule:”`;
    }

    protected elapsed(startedAt: string): string {
        return formatElapsed((this.now() - Date.parse(startedAt)) / 1000);
    }

    protected startTitle(task: TaskInfo): string {
        if (!task.canStart)
            return task.state === 'running'
                ? $localize`It is already running.`
                : $localize`It is turned off, or ffmpeg is missing.`;
        return task.task === 'scan'
            ? $localize`Scan the folder browsed last and its sub-folders (asks first)`
            : $localize`Start the task “${TASK_NAMES[task.task]}:task:” now`;
    }

    protected cancelTitle(task: TaskInfo): string {
        const name = TASK_NAMES[task.task];
        return CANCEL_QUESTIONS[task.task]
            ? $localize`Stop the task “${name}:task:” (asks first)`
            : $localize`Stop the task “${name}:task:”`;
    }

    protected scheduleTitle(task: TaskInfo): string {
        return $localize`Plan runs of the task “${TASK_NAMES[task.task]}:task:” at set times`;
    }

    protected async start(task: TaskInfo): Promise<void> {
        if (task.task === 'scan') {
            // Same as the menu's "Scan this folder", for the folder browsed last: asks, then its card shows the
            // progress.
            await this.scan.start();
            void this.refresh();
            return;
        }
        this.busy.set(`${task.task}:start`);
        const name = TASK_NAMES[task.task];
        try {
            const result = await this.api.startTask(task.task);
            if (result.result === 'unavailable')
                this.toasts.error(
                    $localize`Could not start the task “${name}:task:”: it is turned off, or ffmpeg is missing.`,
                );
            else if (result.result === 'alreadyRunning')
                this.toasts.show(
                    task.task === 'trickplay'
                        ? $localize`A new check starts when the one running has finished.`
                        : $localize`The task “${name}:task:” is already running.`,
                );
        } catch (error) {
            this.toasts.error($localize`Could not start the task “${name}:task:”: ${(error as Error).message}:reason:`);
        } finally {
            this.busy.set(null);
        }
        void this.refresh();
    }

    protected async cancel(task: TaskInfo): Promise<void> {
        const question = CANCEL_QUESTIONS[task.task];
        if (question && !confirm(question)) return;
        this.busy.set(`${task.task}:cancel`);
        try {
            await this.api.cancelTask(task.task);
        } catch (error) {
            const [name, reason] = [TASK_NAMES[task.task], (error as Error).message];
            this.toasts.error($localize`Could not stop the task “${name}:task:”: ${reason}:reason:`);
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
        const name = TASK_NAMES[schedule.task];
        this.toasts.show(
            schedule.nextRun
                ? $localize`Schedule of “${name}:task:” saved. Next run ${formatMoment(schedule.nextRun)}:when:.`
                : $localize`Schedule of “${name}:task:” saved.`,
        );
        void this.refresh();
    }

    protected async toggle(schedule: TaskSchedule): Promise<void> {
        try {
            await this.api.updateSchedule(schedule.id, { ...schedule, enabled: !schedule.enabled });
        } catch (error) {
            this.toasts.error($localize`Could not change the schedule: ${(error as Error).message}:reason:`);
        }
        void this.refresh();
    }

    protected async remove(schedule: TaskSchedule): Promise<void> {
        const [name, when] = [TASK_NAMES[schedule.task], describeSchedule(schedule)];
        if (
            !confirm(
                $localize`Delete the schedule “${name}:task:: ${when}:when:”? \
Its past runs stay in the history. This cannot be undone.`,
            )
        )
            return;
        try {
            await this.api.deleteSchedule(schedule.id);
            if (this.draft()?.schedule?.id === schedule.id) this.draft.set(null);
        } catch (error) {
            this.toasts.error($localize`Could not delete the schedule: ${(error as Error).message}:reason:`);
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
        if (this.tab() === 'history') void this.history().refresh();
    }
}

/** A planned run, as the summary says it: "Next: Index scan of “Photos”, today 03:00" (a scan names its folder). */
function nextRunText(task: TaskInfo, next: NextRun, now: number): string {
    const name = TASK_NAMES[task.task];
    const when = formatMoment(next.at, now);
    if (next.schedule?.task !== 'scan') return $localize`Next: ${name}:task:, ${when}:moment:`;
    return $localize`Next: ${name}:task: of ${target('scan', next.schedule.path)}:folder:, ${when}:moment:`;
}
