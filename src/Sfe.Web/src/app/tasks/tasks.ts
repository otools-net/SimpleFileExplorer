import { formatDate, formatTime } from '../core/format';
import { icu, textLocale } from '../core/i18n/icu';
import { TaskInfo, TaskKind, TaskOutcome, TaskTrigger } from '../core/task.models';
import { ScheduleKind, TaskSchedule, TaskScheduleInput } from './task.models';

/*
 * Wording of the background tasks, their schedules and runs, shared by the "Background tasks" dialog's parts.
 * Every sentence is one message (ICU for counts and choices): names are never lower-cased into another sentence.
 */

export const TASK_NAMES: Record<TaskKind, string> = {
    scan: $localize`:background task:Index scan`,
    trickplay: $localize`:background task:Trickplay`,
    similarVideos: $localize`:background task:Similar videos`,
    thumbnails: $localize`:background task:Thumbnails`,
    trashPurge: $localize`:background task:Trash purge`,
    transfer: $localize`:background task:Transfer`,
    connectionIndex: $localize`:background task:Connection index`,
};

export const TASK_PURPOSES: Record<TaskKind, string> = {
    scan: $localize`Brings the index up to date with the library's folders.`,
    trickplay: $localize`Finds videos without trickplay (the frames shown above the seek bar) and makes it.`,
    similarVideos: $localize`Compares the indexed videos to find the same video in other files.`,
    thumbnails: $localize`Made as listings show photos and videos. They can be stopped, not scheduled.`,
    trashPurge: $localize`Deletes for good what has been in the trash longer than its settings allow. \
Also runs by itself when the trash is used.`,
    transfer: $localize`Copies and moves items between the library and connections.`,
    connectionIndex: $localize`Brings the index of each connection opened in this space up to date with its files, \
from their names, sizes and dates (nothing is downloaded). Also runs when a connection is opened.`,
};

export const TASK_ICONS: Record<TaskKind, string> = {
    scan: 'scan',
    trickplay: 'trickplay',
    similarVideos: 'copy',
    thumbnails: 'image',
    trashPurge: 'trash',
    transfer: 'copy',
    connectionIndex: 'globe',
};

/** The tasks a schedule can start, in the order of the form's list. */
export const SCHEDULABLE: TaskKind[] = ['scan', 'trickplay', 'similarVideos', 'trashPurge', 'connectionIndex'];

export const OUTCOMES: Record<TaskOutcome, string> = {
    running: $localize`:outcome of a task's run:Running`,
    completed: $localize`:outcome of a task's run:Completed`,
    cancelled: $localize`:outcome of a task's run:Cancelled`,
    failed: $localize`:outcome of a task's run:Failed`,
    interrupted: $localize`:outcome of a task's run:Interrupted`,
    skipped: $localize`:outcome of a task's run:Skipped`,
};

export const OUTCOME_ICONS: Record<TaskOutcome, string> = {
    running: '',
    completed: 'check',
    cancelled: 'close',
    failed: 'close',
    interrupted: 'power',
    skipped: 'clock',
};

/** How a run was started, shown after its date and length. */
export const TRIGGERS: Record<TaskTrigger, string> = {
    manual: $localize`:how a task's run started:Started by hand`,
    schedule: $localize`:how a task's run started:Scheduled`,
    automatic: $localize`:how a task's run started:Automatic`,
};

/** Every day of the week, as a schedule's `days`. */
export const ALL_DAYS = 0b111_1111;

/** Short day names in the browser's language, Sunday first (bit 0 of `days`). 4 October 2026 is a Sunday. */
export const DAY_NAMES: string[] = Array.from({ length: 7 }, (_, day) =>
    new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(new Date(2026, 9, 4 + day)),
);

/** The week in the order of the browser's locale (Monday first in most places). */
export const WEEK_ORDER: number[] = (() => {
    const info = new Intl.Locale(navigator.language || 'en') as Intl.Locale & {
        weekInfo?: { firstDay: number };
        getWeekInfo?: () => { firstDay: number };
    };
    const first = (info.getWeekInfo?.() ?? info.weekInfo)?.firstDay ?? 1;
    return Array.from({ length: 7 }, (_, i) => (first + i) % 7);
})();

/** What a task covers: the folder of a scan, the connection a sync of the index ran on (unnamed: a private one). */
export function target(task: TaskKind, path: string | null | undefined): string | null {
    if (task === 'connectionIndex') return path ? `“${path}”` : null;
    return task === 'scan' ? (path ? `“${path}”` : $localize`:what a scan covers:Whole library`) : null;
}

/** An interval as whole days, hours or minutes, for the ICU messages below. */
function intervalParts(minutes: number): { unit: 'day' | 'hour' | 'minute'; n: number } {
    if (minutes % 1440 === 0) return { unit: 'day', n: minutes / 1440 };
    if (minutes % 60 === 0) return { unit: 'hour', n: minutes / 60 };
    return { unit: 'minute', n: minutes };
}

/** "every 6 hours", "every day", "every 90 minutes": inside a sentence. */
export function formatInterval(minutes: number): string {
    return icu(
        $localize`:how often, inside a sentence:{unit, select, \
day {{n, plural, one {every day} other {every # days}}} \
hour {{n, plural, one {every hour} other {every # hours}}} \
other {{n, plural, one {every minute} other {every # minutes}}}}`,
        intervalParts(minutes),
    );
}

/** Day names joined as the browser's language does ("Mon, Wed and Fri"). */
function dayList(days: number): string {
    const names = WEEK_ORDER.filter((day) => days & (1 << day)).map((day) => DAY_NAMES[day]);
    return new Intl.ListFormat(textLocale(), { type: 'conjunction' }).format(names);
}

/** The days of a weekly schedule, for the message of `describeSchedule`. */
function weekKind(days: number): string {
    if ((days & ALL_DAYS) === ALL_DAYS) return 'all';
    if (days === 0b011_1110) return 'weekdays';
    return days === 0b100_0001 ? 'weekends' : 'some';
}

/** When a schedule runs, in words: "Every day at 03:00", "Once, 12 Oct 2026, 22:00". */
export function describeSchedule(
    schedule: Pick<TaskScheduleInput, 'kind' | 'start' | 'intervalMinutes' | 'days'>,
): string {
    switch (schedule.kind) {
        case 'once':
            return $localize`:when a schedule runs:Once, ${formatDate(schedule.start)}:date:`;
        case 'interval':
            return icu(
                $localize`:when a schedule runs:{unit, select, \
day {{n, plural, one {Every day} other {Every # days}}} \
hour {{n, plural, one {Every hour} other {Every # hours}}} \
other {{n, plural, one {Every minute} other {Every # minutes}}}}, from {date}`,
                { ...intervalParts(schedule.intervalMinutes), date: formatDate(schedule.start) },
            );
        default:
            return icu(
                $localize`:when a schedule runs:{days, select, \
all {Every day at {time}} \
weekdays {Every weekday at {time}} \
weekends {At weekends, at {time}} \
other {On {list} at {time}}}`,
                { days: weekKind(schedule.days), list: dayList(schedule.days), time: formatTime(schedule.start) },
            );
    }
}

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
const weekday = new Intl.DateTimeFormat(undefined, { weekday: 'long' });

/** "in 5 minutes", "2 hours ago", "tomorrow"… from `now`. */
export function formatRelative(value: string, now = Date.now()): string {
    const seconds = (Date.parse(value) - now) / 1000;
    const size = Math.abs(seconds);
    if (size < 45) return seconds >= 0 ? $localize`in a few seconds` : $localize`just now`;
    if (size < 90 * 60) return relative.format(Math.round(seconds / 60), 'minute');
    if (size < 36 * 3600) return relative.format(Math.round(seconds / 3600), 'hour');
    return relative.format(Math.round(seconds / 86400), 'day');
}

/** A moment close to now the way people say it: "today 03:00", "tomorrow 22:30", "Monday 08:00", else the date. */
export function formatMoment(value: string, now = Date.now()): string {
    const date = new Date(value);
    const today = new Date(now);
    today.setHours(0, 0, 0, 0);
    const days = Math.round((new Date(date).setHours(0, 0, 0, 0) - today.getTime()) / 86_400_000);
    const time = formatTime(value);
    if (days === 0) return $localize`:a moment, alone or inside a sentence:today ${time}:time:`;
    if (days === 1) return $localize`:a moment, alone or inside a sentence:tomorrow ${time}:time:`;
    if (days === -1) return $localize`:a moment, alone or inside a sentence:yesterday ${time}:time:`;
    if (days > 1 && days < 7)
        return $localize`:a moment, day of the week then time:${weekday.format(date)}:weekday: ${time}:time:`;
    return formatDate(value);
}

/** The next run of a task: the earliest of its schedules and of its own periodic runs. */
export interface NextRun {
    at: string;
    /** The schedule, or null for the task's own periodic run. */
    schedule: TaskSchedule | null;
}

export function nextRunOf(task: TaskInfo, schedules: TaskSchedule[]): NextRun | null {
    let next: NextRun | null = task.automaticNextRun ? { at: task.automaticNextRun, schedule: null } : null;
    for (const schedule of schedules) {
        if (
            schedule.task === task.task &&
            schedule.nextRun &&
            (!next || Date.parse(schedule.nextRun) < Date.parse(next.at))
        )
            next = { at: schedule.nextRun, schedule };
    }
    return next;
}

/** Where a schedule stands when it has no next run. */
export function scheduleStatus(schedule: TaskSchedule, now = Date.now()): string {
    if (!schedule.enabled) return $localize`:a schedule turned off:Off`;
    if (schedule.kind !== 'once') return '';
    if (schedule.lastRun) return $localize`:a schedule that ran once:Done`;
    return Date.parse(schedule.start) <= now ? $localize`Missed (the server was off)` : '';
}

/** Local date ("2026-10-10") and time ("03:00") of an ISO moment, for date and time inputs. */
export function toLocalInputs(value: string): { date: string; time: string } {
    const date = new Date(value);
    const pad = (n: number) => String(n).padStart(2, '0');
    return {
        date: `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`,
        time: `${pad(date.getHours())}:${pad(date.getMinutes())}`,
    };
}

/** The ISO moment of a local date and time typed in inputs; null when incomplete. */
export function fromLocalInputs(date: string, time: string): string | null {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !/^\d{2}:\d{2}$/.test(time)) return null;
    const moment = new Date(`${date}T${time}`);
    return Number.isNaN(moment.getTime()) ? null : moment.toISOString();
}

/** The repeat choices of the schedule form; "daily" is a weekly schedule on every day. */
export type RepeatChoice = 'once' | 'interval' | 'daily' | 'weekly';

export function repeatOf(kind: ScheduleKind, days: number): RepeatChoice {
    return kind === 'weekly' && (days & ALL_DAYS) === ALL_DAYS ? 'daily' : kind;
}
