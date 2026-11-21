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
} from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { formatDate } from '../core/format';
import { TaskKind } from '../core/task.models';
import { TaskSchedule, TaskScheduleInput } from './task.models';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import {
    ALL_DAYS,
    DAY_NAMES,
    formatMoment,
    formatRelative,
    fromLocalInputs,
    RepeatChoice,
    repeatOf,
    SCHEDULABLE,
    TASK_NAMES,
    toLocalInputs,
    WEEK_ORDER,
} from './tasks';

const PREVIEW_DELAY_MS = 300;

type Unit = 'minutes' | 'hours' | 'days';
const UNIT_MINUTES: Record<Unit, number> = { minutes: 1, hours: 60, days: 1440 };
const UNIT_NAMES: Record<Unit, string> = {
    minutes: $localize`:unit of a schedule's interval:minutes`,
    hours: $localize`:unit of a schedule's interval:hours`,
    days: $localize`:unit of a schedule's interval:days`,
};

/** What the form starts from: a schedule to change, or the task (and folder) of a new one. */
export interface ScheduleDraft {
    schedule?: TaskSchedule;
    task?: TaskKind;
    path?: string;
}

/**
 * Creates or changes a schedule. While it is filled in, the server works out the next runs (the same calculation
 * that will start the task), so what is shown is exactly when it will run.
 */
@Component({
    selector: 'sfe-schedule-form',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'schedule-form settings-form' },
    templateUrl: './schedule-form.component.html',
})
export class ScheduleFormComponent {
    private readonly api = inject(Api);

    readonly draft = input.required<ScheduleDraft>();
    /** Ticks the relative times. */
    readonly now = input(Date.now());
    readonly saved = output<TaskSchedule>();
    readonly cancelled = output<void>();

    protected readonly tasks = SCHEDULABLE;
    protected readonly names = TASK_NAMES;
    protected readonly week = WEEK_ORDER;
    protected readonly dayNames = DAY_NAMES;
    protected readonly units: Unit[] = ['minutes', 'hours', 'days'];
    protected readonly unitNames = UNIT_NAMES;
    protected readonly repeats: [RepeatChoice, string][] = [
        ['once', $localize`:how often a schedule runs:Once`],
        ['interval', $localize`:how often a schedule runs:Repeatedly`],
        ['daily', $localize`:how often a schedule runs:Every day`],
        ['weekly', $localize`:how often a schedule runs:On some days of the week`],
    ];
    protected readonly formatDate = formatDate;
    protected readonly formatMoment = formatMoment;
    protected readonly formatRelative = formatRelative;

    protected readonly task = signal<TaskKind>('scan');
    protected readonly path = signal('');
    protected readonly repeat = signal<RepeatChoice>('daily');
    protected readonly every = signal(6);
    protected readonly unit = signal<Unit>('hours');
    protected readonly days = signal(ALL_DAYS);
    protected readonly date = signal('');
    protected readonly time = signal('03:00');
    protected readonly enabled = signal(true);
    protected readonly errors = signal<Record<string, string[]>>({});
    protected readonly saving = signal(false);
    protected readonly preview = signal<string[]>([]);
    protected readonly previewError = signal<string | null>(null);

    /** The schedule as the API takes it; null while the date or time is incomplete. */
    protected readonly value = computed((): TaskScheduleInput | null => {
        const repeat = this.repeat();
        // Weekly schedules only use the time of day: today's date carries it.
        const date =
            repeat === 'once' || repeat === 'interval' ? this.date() : toLocalInputs(new Date().toISOString()).date;
        const start = fromLocalInputs(date, this.time());
        if (!start) return null;
        return {
            task: this.task(),
            path: this.task() === 'scan' ? this.path().trim() : null,
            kind: repeat === 'daily' ? 'weekly' : repeat,
            start,
            intervalMinutes: repeat === 'interval' ? Math.round(this.every() * UNIT_MINUTES[this.unit()]) : 0,
            days: repeat === 'daily' ? ALL_DAYS : repeat === 'weekly' ? this.days() : 0,
            enabled: this.enabled(),
        };
    });

    constructor() {
        // Fills the form when it opens on a schedule or a task.
        effect(() => this.fill(this.draft()));

        let timer = 0;
        effect(() => {
            const value = this.value();
            clearTimeout(timer);
            if (!value) {
                this.preview.set([]);
                this.previewError.set($localize`Enter a date and a time.`);
                return;
            }
            timer = setTimeout(() => void this.loadPreview(value), PREVIEW_DELAY_MS);
        });
        inject(DestroyRef).onDestroy(() => clearTimeout(timer));
    }

    private fill(draft: ScheduleDraft): void {
        const schedule = draft.schedule;
        const start = schedule
            ? toLocalInputs(schedule.start)
            : toLocalInputs(new Date(Date.now() + 3600_000).toISOString());
        this.task.set(schedule?.task ?? draft.task ?? 'scan');
        this.path.set(schedule?.path ?? draft.path ?? '');
        this.repeat.set(schedule ? repeatOf(schedule.kind, schedule.days) : 'daily');
        this.date.set(start.date);
        this.time.set(schedule ? start.time : '03:00');
        this.enabled.set(schedule?.enabled ?? true);
        this.days.set(schedule?.kind === 'weekly' ? schedule.days : 0b011_1110);
        const minutes = schedule?.kind === 'interval' ? schedule.intervalMinutes : 360;
        const unit: Unit = minutes % 1440 === 0 ? 'days' : minutes % 60 === 0 ? 'hours' : 'minutes';
        this.unit.set(unit);
        this.every.set(minutes / UNIT_MINUTES[unit]);
        this.errors.set({});
    }

    protected hasDay(day: number): boolean {
        return (this.days() & (1 << day)) !== 0;
    }

    protected toggleDay(day: number): void {
        this.days.update((days) => days ^ (1 << day));
    }

    protected async save(): Promise<void> {
        const value = this.value();
        if (!value) return;
        try {
            const id = this.draft().schedule?.id;
            const saved = await whileBusy(this.saving, () =>
                id === undefined ? this.api.createSchedule(value) : this.api.updateSchedule(id, value),
            );
            this.saved.emit(saved);
        } catch (error) {
            this.errors.set(
                error instanceof ApiError && error.errors ? error.errors : { schedule: [(error as Error).message] },
            );
        }
    }

    private async loadPreview(value: TaskScheduleInput): Promise<void> {
        try {
            const { runs } = await this.api.previewSchedule(value);
            if (this.value() !== value) return;
            this.preview.set(runs);
            this.previewError.set(null);
        } catch (error) {
            if (this.value() !== value) return;
            this.preview.set([]);
            // The fields' own errors show next to them on saving; the preview says why it can't tell.
            this.previewError.set(
                error instanceof ApiError && error.errors
                    ? Object.values(error.errors).flat()[0]
                    : (error as Error).message,
            );
        }
    }

    protected valueOf<T extends string>(event: Event): T {
        return (event.target as HTMLInputElement | HTMLSelectElement).value as T;
    }

    protected numberOf(event: Event): number {
        return Number((event.target as HTMLInputElement).value) || 0;
    }

    protected checkedOf(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }
}
