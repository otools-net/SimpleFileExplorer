import type { TrickplayState } from '../core/task.models';

export interface TrickplayStatus {
    state: TrickplayState;
    checked: number;
    generated: number;
    failed: number;
    queued: number;
    pending: number;
    discovering: boolean;
    current: string[];
    lastPassFinished: string | null;
    passStarted?: string | null;
    nextPass?: string | null;
}

export interface TrickplayActivity {
    status: TrickplayStatus;
    running: { path: string; started: string }[];
    queued: string[];
    pending: string[];
    recent: { path: string; succeeded: boolean; error: string | null; finished: string; seconds: number }[];
}
