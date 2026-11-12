import type { TaskInfo, TaskKind, TaskLogLine, TaskRunSummary } from '../core/task.models';

/** `weekly` with every day is shown as "every day". */
export type ScheduleKind = 'once' | 'interval' | 'weekly';

export interface TaskRunDetails extends TaskRunSummary {
    log: TaskLogLine[];
    /** Lines left out in the middle of a long log. */
    omitted: number;
}

export interface TaskScheduleInput {
    task: TaskKind;
    path: string | null;
    kind: ScheduleKind;
    /** ISO date: the first run, or the time of day (server clock) of a weekly schedule. */
    start: string;
    intervalMinutes: number;
    /** Weekly days: bit 0 = Sunday … bit 6 = Saturday. */
    days: number;
    enabled: boolean;
}

export interface TaskSchedule extends TaskScheduleInput {
    id: number;
    nextRun: string | null;
    lastRun: string | null;
}

export interface TaskOverview {
    tasks: TaskInfo[];
    schedules: TaskSchedule[];
    serverTime: string;
    /** The server's time zone (IANA id when known): weekly schedules follow its clock. */
    timeZone: string;
    logRetentionDays: number;
}
