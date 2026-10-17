import { formatDate, formatTime, plural } from '../core/format';
import { ScheduleKind, TaskInfo, TaskKind, TaskOutcome, TaskSchedule, TaskScheduleInput, TaskTrigger } from '../core/models';

/*
 * Wording of the background tasks, their schedules and runs, shared by the "Background tasks" dialog's parts.
 */

export const TASK_NAMES: Record<TaskKind, string> = {
    scan: 'Index scan',
    trickplay: 'Trickplay',
    similarVideos: 'Similar videos',
    thumbnails: 'Thumbnails',
    trashPurge: 'Trash purge',
};

export const TASK_PURPOSES: Record<TaskKind, string> = {
    scan: 'Brings the search index up to date with the folders on the disk.',
    trickplay: 'Looks for videos without trickplay (the frames shown above the seek bar) and creates it.',
    similarVideos: 'Compares the indexed videos to find copies in other files.',
    thumbnails: 'Rendered as the listings show photos and videos; they can be stopped, not scheduled.',
    trashPurge: 'Deletes for good what has been in the trash longer than its settings say. Also runs by itself when the trash is used.',
};

export const TASK_ICONS: Record<TaskKind, string> = {
    scan: 'scan',
    trickplay: 'trickplay',
    similarVideos: 'copy',
    thumbnails: 'image',
    trashPurge: 'trash',
};

/** The tasks a schedule can start, in the order of the form's list. */
export const SCHEDULABLE: TaskKind[] = ['scan', 'trickplay', 'similarVideos', 'trashPurge'];

export const OUTCOMES: Record<TaskOutcome, string> = {
    running: 'Running',
    completed: 'Completed',
    cancelled: 'Cancelled',
    failed: 'Failed',
    interrupted: 'Interrupted',
    skipped: 'Skipped',
};

export const OUTCOME_ICONS: Record<TaskOutcome, string> = {
    running: '',
    completed: 'check',
    cancelled: 'close',
    failed: 'close',
    interrupted: 'power',
    skipped: 'clock',
};

export const TRIGGERS: Record<TaskTrigger, string> = {
    manual: 'started by hand',
    schedule: 'scheduled',
    automatic: 'automatic',
};

/** Every day of the week, as a schedule's `days`. */
export const ALL_DAYS = 0b111_1111;

/** Short day names in the browser's language, Sunday first (bit 0 of `days`). 4 October 2026 is a Sunday. */
export const DAY_NAMES: string[] = Array.from({ length: 7 }, (_, day) =>
    new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(new Date(2026, 9, 4 + day)));

/** The week in the order of the browser's locale (Monday first in most places). */
export const WEEK_ORDER: number[] = (() => {
    const info = (new Intl.Locale(navigator.language || 'en') as Intl.Locale & { weekInfo?: { firstDay: number }; getWeekInfo?: () => { firstDay: number } });
    const first = (info.getWeekInfo?.() ?? info.weekInfo)?.firstDay ?? 1;
    return Array.from({ length: 7 }, (_, i) => (first + i) % 7);
})();

/** What a task covers: the folder of a scan. */
export function target(task: TaskKind, path: string | null | undefined): string | null {
    return task === 'scan' ? (path ? `“${path}”` : 'whole library') : null;
}

/** "Every 6 hours", "every 2 days", "every 90 minutes". */
export function formatInterval(minutes: number): string {
    if (minutes % 1440 === 0)
        return minutes === 1440 ? 'every day' : `every ${plural(minutes / 1440, 'day')}`;
    if (minutes % 60 === 0)
        return minutes === 60 ? 'every hour' : `every ${plural(minutes / 60, 'hour')}`;
    return `every ${plural(minutes, 'minute')}`;
}

/** "Mon, Wed, Fri", or "every day". */
export function formatDays(days: number): string {
    if ((days & ALL_DAYS) === ALL_DAYS)
        return 'every day';
    if (days === 0b011_1110)
        return 'on weekdays';
    if (days === 0b100_0001)
        return 'at weekends';
    return 'on ' + WEEK_ORDER.filter(day => days & (1 << day)).map(day => DAY_NAMES[day]).join(', ');
}

/** When a schedule runs, in words: "Every day at 03:00", "Once, 12 Oct 2026, 22:00". */
export function describeSchedule(schedule: Pick<TaskScheduleInput, 'kind' | 'start' | 'intervalMinutes' | 'days'>): string {
    switch (schedule.kind) {
        case 'once': return `Once, ${formatDate(schedule.start)}`;
        case 'interval': return `${capitalize(formatInterval(schedule.intervalMinutes))}, from ${formatDate(schedule.start)}`;
        default: return `${capitalize(formatDays(schedule.days))} at ${formatTime(schedule.start)}`;
    }
}

function capitalize(text: string): string {
    return text.charAt(0).toUpperCase() + text.slice(1);
}

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
const weekday = new Intl.DateTimeFormat(undefined, { weekday: 'long' });

/** "in 5 minutes", "2 hours ago", "tomorrow"… from `now`. */
export function formatRelative(value: string, now = Date.now()): string {
    const seconds = (Date.parse(value) - now) / 1000;
    const size = Math.abs(seconds);
    if (size < 45)
        return seconds >= 0 ? 'in a few seconds' : 'just now';
    if (size < 90 * 60)
        return relative.format(Math.round(seconds / 60), 'minute');
    if (size < 36 * 3600)
        return relative.format(Math.round(seconds / 3600), 'hour');
    return relative.format(Math.round(seconds / 86400), 'day');
}

/** A moment close to now the way people say it: "today 03:00", "tomorrow 22:30", "Monday 08:00", else the date. */
export function formatMoment(value: string, now = Date.now()): string {
    const date = new Date(value);
    const today = new Date(now);
    today.setHours(0, 0, 0, 0);
    const days = Math.round((new Date(date).setHours(0, 0, 0, 0) - today.getTime()) / 86_400_000);
    const time = formatTime(value);
    if (days === 0)
        return `today ${time}`;
    if (days === 1)
        return `tomorrow ${time}`;
    if (days === -1)
        return `yesterday ${time}`;
    if (days > 1 && days < 7)
        return `${weekday.format(date)} ${time}`;
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
        if (schedule.task === task.task && schedule.nextRun && (!next || Date.parse(schedule.nextRun) < Date.parse(next.at)))
            next = { at: schedule.nextRun, schedule };
    }
    return next;
}

/** Where a schedule stands when it has no next run. */
export function scheduleStatus(schedule: TaskSchedule, now = Date.now()): string {
    if (!schedule.enabled)
        return 'Off';
    if (schedule.kind === 'once')
        return schedule.lastRun ? 'Done' : Date.parse(schedule.start) <= now ? 'Missed (the server was off)' : '';
    return '';
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
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !/^\d{2}:\d{2}$/.test(time))
        return null;
    const moment = new Date(`${date}T${time}`);
    return Number.isNaN(moment.getTime()) ? null : moment.toISOString();
}

/** The repeat choices of the schedule form; "daily" is a weekly schedule on every day. */
export type RepeatChoice = 'once' | 'interval' | 'daily' | 'weekly';

export function repeatOf(kind: ScheduleKind, days: number): RepeatChoice {
    return kind === 'weekly' && (days & ALL_DAYS) === ALL_DAYS ? 'daily' : kind;
}
