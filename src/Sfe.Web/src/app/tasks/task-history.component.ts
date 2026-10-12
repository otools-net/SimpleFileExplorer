import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDate, formatElapsed, joinParts, plural } from '../core/format';
import { TaskKind, TaskRunDetails, TaskRunSummary } from '../core/models';
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
    template: `
        <div class="task-history-bar">
            <label class="visually-hidden" for="task-history-filter">Task</label>
            <select id="task-history-filter" class="task-history-filter" title="Show the runs of one task" (change)="setFilter(valueOf($event))">
                <option value="" [selected]="!filter()">All tasks</option>
                @for (kind of kinds; track kind) {
                    <option [value]="kind" [selected]="filter() === kind">{{ names[kind] }}</option>
                }
            </select>
            <span class="field-hint">{{ retentionText() }}</span>
            <span class="spacer"></span>
            <button type="button" class="button small" title="Change how long the runs are kept" (click)="openSettings.emit()"><svg sfeIcon="settings"></svg>Keep for…</button>
            <button type="button" class="button small danger" title="Delete the history of finished runs (asks first)" [sfeBusy]="clearing()"
                    [disabled]="clearing() || !runs().length" (click)="clear()">Clear history</button>
        </div>
        @if (error()) {
            <p class="form-error">Could not read the history: {{ error() }}</p>
        }
        <ul class="task-runs">
            @for (run of runs(); track run.id) {
                @let open = openId() === run.id;
                <li class="task-run" [class]="run.outcome" [class.open]="open">
                    <button type="button" class="task-run-head" [attr.aria-expanded]="open" [title]="open ? 'Hide the log' : 'Show the log of this run'" (click)="toggle(run)">
                        @if (run.outcome === 'running') {
                            <span class="trickplay-spinner"></span>
                        } @else {
                            <svg [sfeIcon]="icons[run.outcome]"></svg>
                        }
                        <span class="task-run-main">
                            <span class="task-run-title">
                                <strong>{{ names[run.task] }}</strong>
                                @if (target(run.task, run.path); as where) {
                                    <span>{{ where }}</span>
                                }
                                <span class="task-outcome" [class]="run.outcome">{{ outcomes[run.outcome] }}</span>
                            </span>
                            <span class="task-run-sub">{{ subtitle(run) }}</span>
                            @if (run.summary) {
                                <span class="task-run-summary">{{ run.summary }}</span>
                            }
                        </span>
                    </button>
                    @if (open) {
                        <div class="task-run-log">
                            @if (details(); as details) {
                                @if (details.log.length) {
                                    <ol>
                                        @for (line of details.log; track $index) {
                                            @if (details.omitted && $index === half(details)) {
                                                <li class="task-log-omitted">… {{ plural(details.omitted, 'line') }} left out …</li>
                                            }
                                            <li [class]="'level-' + line.level.toLowerCase()">
                                                <time>{{ time(line.time) }}</time><span class="task-log-level">{{ line.level }}</span><span>{{ line.message }}</span>
                                            </li>
                                        }
                                    </ol>
                                } @else {
                                    <p class="muted">Nothing was logged.</p>
                                }
                            } @else {
                                <p class="muted">Loading the log…</p>
                            }
                        </div>
                    }
                </li>
            }
        </ul>
        @if (!runs().length && loaded()) {
            <p class="muted task-empty">No runs recorded{{ filter() ? ' for this task' : '' }} yet.</p>
        }
        @if (next() !== null) {
            <button type="button" class="button small task-more" title="Show older runs" [sfeBusy]="loading()" [disabled]="loading()" (click)="loadMore()">Load older runs</button>
        }
    `,
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
    protected readonly plural = plural;

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
        return days > 0 ? `Runs are kept ${plural(days, 'day')}.` : 'Runs are kept until cleared.';
    });

    /** The first page again (and the open log), unless older runs were loaded. */
    async refresh(): Promise<void> {
        if (this.paged)
            return;
        await this.load();
        const open = this.runs().find(run => run.id === this.openId());
        // A finished run's log doesn't change any more.
        if (open && (open.outcome === 'running' || this.details()?.outcome === 'running'))
            void this.loadDetails(open.id);
    }

    /** Opens the history on one run (e.g. a task's last run). */
    async show(run: TaskRunSummary): Promise<void> {
        this.filter.set(null);
        this.paged = false;
        await this.load();
        this.openId.set(run.id);
        this.details.set(null);
        void this.loadDetails(run.id);
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
        if (before === null)
            return;
        try {
            const page = await whileBusy(this.loading, () => this.api.taskRuns({ task: this.filter(), before }));
            this.paged = true;
            this.runs.update(runs => [...runs, ...page.runs]);
            this.next.set(page.next);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    protected async clear(): Promise<void> {
        if (!confirm('Delete the history of the finished runs, with their logs?'))
            return;
        try {
            await whileBusy(this.clearing, () => this.api.clearTaskRuns());
            this.paged = false;
            await this.load();
        } catch (error) {
            this.toasts.error(`Could not clear the history: ${(error as Error).message}`);
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
            if (filter !== this.filter() || this.paged)
                return;
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
            if (this.openId() === id)
                this.details.set(details);
        } catch (error) {
            if (this.openId() === id)
                this.toasts.error(`Could not read the log: ${(error as Error).message}`);
        }
    }
}
