import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { TaskInfo } from '../../core/task.models';
import { IconComponent } from '../../shared/icon.component';
import { formatRelative, OUTCOMES, TASK_ICONS, TASK_NAMES } from '../../tasks/tasks';

/** A line of the card: a task running now, or one whose last run went wrong. */
interface TaskLine {
    task: TaskInfo;
    running: boolean;
    text: string;
    queryParams: Record<string, string | number>;
}

/** The outcomes of a last run the card tells. */
const WENT_WRONG = new Set(['failed', 'interrupted']);

/**
 * The dashboard's Background tasks: those running now (with their progress), then those whose last run failed or
 * was interrupted (by a restart), each opening the Background tasks page, a run its log in the history. Read once
 * when the dashboard opens (`api/tasks`, as that page does).
 */
@Component({
    selector: 'sfe-tasks-widget',
    imports: [RouterLink, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tasks-widget' },
    template: `
        @if (lines(); as lines) {
            <ul class="dashboard-list">
                @for (line of lines; track line.task.task) {
                    <li class="dashboard-task" [class.warning]="!line.running" [attr.data-task]="line.task.task">
                        <a routerLink="/admin" [queryParams]="line.queryParams">
                            <svg [sfeIcon]="icons[line.task.task]"></svg>
                            <span class="quick-link-name">{{ names[line.task.task] }}</span>
                        </a>
                        @if (line.running) {
                            <div class="storage-meter" role="progressbar" aria-valuemin="0" aria-valuemax="100"
                                 [attr.aria-valuenow]="percent(line.task)" [attr.aria-label]="names[line.task.task]">
                                <span [style.width.%]="percent(line.task) ?? 0"></span>
                            </div>
                        }
                        <span class="dashboard-card-note">{{ line.text }}</span>
                    </li>
                }
            </ul>
            @if (!lines.length) {
                <p class="dashboard-card-note" i18n>Nothing running, and no last run went wrong.</p>
            }
        } @else if (failed()) {
            <p class="dashboard-card-note" i18n>The background tasks could not be read.</p>
        } @else {
            <span class="spinner"></span>
        }
    `,
})
export class TasksWidgetComponent implements OnInit {
    private readonly api = inject(Api);

    protected readonly names = TASK_NAMES;
    protected readonly icons = TASK_ICONS;
    private readonly tasks = signal<TaskInfo[] | null>(null);
    protected readonly failed = signal(false);

    protected readonly lines = computed(() => {
        const tasks = this.tasks();
        if (!tasks) return null;
        const running = tasks.filter((task) => task.state === 'running').map((task) => this.runningLine(task));
        const wrong = tasks
            .filter((task) => task.state !== 'running' && task.lastRun && WENT_WRONG.has(task.lastRun.outcome))
            .map((task) => this.wrongLine(task));
        return [...running, ...wrong];
    });

    async ngOnInit(): Promise<void> {
        try {
            this.tasks.set((await this.api.tasks()).tasks);
        } catch {
            this.failed.set(true);
        }
    }

    protected percent(task: TaskInfo): number | null {
        return task.progress === null ? null : Math.round(task.progress * 100);
    }

    private runningLine(task: TaskInfo): TaskLine {
        const since = task.startedAt ? formatRelative(task.startedAt) : '';
        const text = task.detail || $localize`:a task running:Started ${since}:when:`;
        return { task, running: true, text, queryParams: { section: 'tasks', page: 'tasks' } };
    }

    private wrongLine(task: TaskInfo): TaskLine {
        const run = task.lastRun!;
        const when = formatRelative(run.finished ?? run.started);
        const outcome = OUTCOMES[run.outcome];
        const text = [$localize`:a task's last run:${outcome}:outcome: ${when}:when:`, run.summary]
            .filter(Boolean)
            .join(' · ');
        return { task, running: false, text, queryParams: { section: 'tasks', page: 'history', run: run.id } };
    }
}
