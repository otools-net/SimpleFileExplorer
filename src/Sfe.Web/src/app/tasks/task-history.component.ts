import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatElapsed, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { TaskKind, TaskRunSummary } from '../core/task.models';
import { TaskRunDetails } from './task.models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { OUTCOME_ICONS, OUTCOMES, target, TASK_NAMES, TRIGGERS } from './tasks';

const timeFormat = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' });

/**
 * The runs of the background tasks, newest first, kept on the server `retentionDays`. A run opens on its log; the
 * log of a run in progress follows it while the dialog refreshes.
 */
@Component({
    selector: 'sfe-task-history',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'task-history' },
    templateUrl: './task-history.component.html',
})
export class TaskHistoryComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** Days the server keeps the runs; 0 = forever. */
    readonly retentionDays = input(30);
    readonly openSettings = output<void>();

    protected readonly kinds: TaskKind[] = ['scan', 'trickplay', 'similarVideos'];
    protected readonly names = TASK_NAMES;
    protected readonly outcomes = OUTCOMES;
    protected readonly icons = OUTCOME_ICONS;
    protected readonly target = target;
    protected readonly logTitles = {
        open: $localize`Hide the log`,
        closed: $localize`Show the log of this run`,
    };

    protected readonly filter = signal<TaskKind | null>(null);
    protected readonly runs = signal<TaskRunSummary[]>([]);
    protected readonly next = signal<number | null>(null);
    protected readonly loaded = signal(false);
    protected readonly loading = signal(false);
    protected readonly clearing = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly openId = signal<number | null>(null);
    protected readonly details = signal<TaskRunDetails | null>(null);
    /** More than the first page is shown: refreshing would drop the older runs. */
    private paged = false;

    protected readonly retentionText = computed(() => {
        const days = this.retentionDays();
        return days > 0
            ? icu($localize`Runs are kept {days, plural, one {# day} other {# days}}.`, { days })
            : $localize`Runs are kept until the history is cleared.`;
    });

    /** The first page again (and the open log), unless older runs were loaded. */
    async refresh(): Promise<void> {
        if (this.paged) return;
        await this.load();
        const open = this.runs().find((run) => run.id === this.openId());
        // A finished run's log doesn't change any more.
        if (open && (open.outcome === 'running' || this.details()?.outcome === 'running'))
            void this.loadDetails(open.id);
    }

    /** Opens the history on one run (a task's last run, a notification's link). */
    async show(id: number): Promise<void> {
        this.filter.set(null);
        this.paged = false;
        await this.load();
        this.openId.set(id);
        this.details.set(null);
        void this.loadDetails(id);
    }

    protected setFilter(value: string): void {
        this.filter.set((value || null) as TaskKind | null);
        this.paged = false;
        void this.load();
    }

    protected toggle(run: TaskRunSummary): void {
        if (this.openId() === run.id) {
            this.openId.set(null);
            return;
        }
        this.openId.set(run.id);
        this.details.set(null);
        void this.loadDetails(run.id);
    }

    protected async loadMore(): Promise<void> {
        const before = this.next();
        if (before === null) return;
        try {
            const page = await whileBusy(this.loading, () => this.api.taskRuns({ task: this.filter(), before }));
            this.paged = true;
            this.runs.update((runs) => [...runs, ...page.runs]);
            this.next.set(page.next);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    protected async clear(): Promise<void> {
        if (!confirm($localize`Delete the history of the finished runs, with their logs? This cannot be undone.`))
            return;
        try {
            await whileBusy(this.clearing, () => this.api.clearTaskRuns());
            this.paged = false;
            await this.load();
        } catch (error) {
            this.toasts.error($localize`Could not clear the task history: ${(error as Error).message}:reason:`);
        }
    }

    protected subtitle(run: TaskRunSummary): string {
        const end = run.finished ? Date.parse(run.finished) : Date.now();
        return joinParts([
            formatDate(run.started),
            run.outcome === 'skipped' ? null : formatElapsed((end - Date.parse(run.started)) / 1000),
            TRIGGERS[run.trigger],
        ]);
    }

    protected time(value: string): string {
        return timeFormat.format(new Date(value));
    }

    /** Where the left-out lines were: between the first and the last ones kept. */
    protected half(details: TaskRunDetails): number {
        return Math.ceil(details.log.length / 2);
    }

    protected valueOf(event: Event): string {
        return (event.target as HTMLSelectElement).value;
    }

    private async load(): Promise<void> {
        const filter = this.filter();
        try {
            const page = await this.api.taskRuns({ task: filter, before: null });
            if (filter !== this.filter() || this.paged) return;
            this.runs.set(page.runs);
            this.next.set(page.next);
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.loaded.set(true);
        }
    }

    private async loadDetails(id: number): Promise<void> {
        try {
            const details = await this.api.taskRun(id);
            if (this.openId() === id) this.details.set(details);
        } catch (error) {
            if (this.openId() === id)
                this.toasts.error($localize`Could not load the log: ${(error as Error).message}:reason:`);
        }
    }
}
