import type { FileTags } from './tag.models';

export type ScanState = 'idle' | 'running' | 'completed' | 'cancelled' | 'failed';

export type TrickplayState = 'disabled' | 'unavailable' | 'idle' | 'running';

export type TrickplayFileState = 'pending' | 'running';

export interface TrickplayStates {
    running: boolean;
    files: Record<string, TrickplayFileState>;
}

export interface TrickplayManifest {
    interval: number;
    count: number;
    columns: number;
    rows: number;
    tileWidth: number;
    tileHeight: number;
    sheets: number;
    version: number;
}

export interface DuplicateSummary {
    groups: number;
    files: number;
    wastedBytes: number;
    unfingerprinted: number;
}

export interface RemovedDuplicate {
    removed: string;
    kept: FileTags;
    /** Its entry in the trash, for an Undo; null when it was deleted for good. */
    trashId?: number | null;
}

export type SimilarVideoState = 'idle' | 'running' | 'completed' | 'cancelled' | 'failed';

export interface RelinkPassResult {
    relinked: number;
    forgotten: number;
    remaining: number;
}

/** The background jobs of the "Background tasks" dialog. */
export type TaskKind = 'scan' | 'trickplay' | 'similarVideos' | 'thumbnails' | 'trashPurge';

export type TaskState = 'idle' | 'running' | 'disabled' | 'unavailable';

export type TaskOutcome = 'running' | 'completed' | 'cancelled' | 'failed' | 'interrupted' | 'skipped';

export type TaskTrigger = 'manual' | 'schedule' | 'automatic';

export interface TaskRunSummary {
    id: number;
    task: TaskKind;
    /** Folder a scan covered ('' for the library). */
    path: string | null;
    trigger: TaskTrigger;
    scheduleId: number | null;
    started: string;
    finished: string | null;
    outcome: TaskOutcome;
    summary: string | null;
}

export interface TaskLogLine {
    time: string;
    level: string;
    message: string;
}

export interface TaskRunPage {
    runs: TaskRunSummary[];
    /** `before` of the next (older) page; null on the last one. */
    next: number | null;
}

export interface TaskInfo {
    task: TaskKind;
    state: TaskState;
    path: string | null;
    startedAt: string | null;
    /** 0 to 1 when the total is known. */
    progress: number | null;
    detail: string;
    canStart: boolean;
    canCancel: boolean;
    canSchedule: boolean;
    /** When it runs by itself next (trickplay's periodic check). */
    automaticNextRun: string | null;
    /** Hours between those runs. */
    automaticEvery: number | null;
    lastRun: TaskRunSummary | null;
}

export type TaskStartResult = 'started' | 'alreadyRunning' | 'unavailable';

export interface TaskActionResult {
    result: TaskStartResult;
    task: TaskInfo;
}
