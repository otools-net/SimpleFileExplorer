import { inject, Injectable } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { icu } from '../core/i18n/icu';
import { readPreference, writePreference } from '../core/preferences';
import { Rights } from '../core/rights';
import { TaskKind } from '../core/task.models';
import { ToastAction, ToastLevel, ToastService } from '../core/toast.service';

/** The change feed's `task` event: a run of a background task that ended (agents/docs/features/notifications.md). */
export interface TaskEnded {
    /** The run in the task history; negative when the server couldn't write it there. */
    id: number;
    task: TaskKind;
    /** A scan's folder, a connection index's connection; null for the others. */
    path: string | null;
    outcome: 'completed' | 'cancelled' | 'failed';
    summary: string | null;
    /** The server's clock. */
    finished: string;
    /** This account started it; false: a failure told to those who see the tasks. */
    mine: boolean;
}

const KEY = 'sfe.taskSince';

const LEVELS: Record<TaskEnded['outcome'], ToastLevel> = { completed: 'success', cancelled: 'info', failed: 'error' };

/**
 * The runs of background tasks that ended, told by the change feed, as notifications (a toast while the page is shown)
 * with a link to the run in the task history. The latest end told is kept per space and account (`since`): the feed
 * opened again asks for what ended after it, and what is not later is not told twice (another tab, a reconnection).
 */
@Injectable({ providedIn: 'root' })
export class TaskNotices {
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);

    /**
     * The time to give the feed: what ended after it is told when it opens. The first time, now (nothing older is
     * told), and null for the feed.
     */
    since(now = new Date()): string | null {
        const key = this.key();
        const since = readPreference(key);
        if (!since) writePreference(key, now.toISOString());
        return since;
    }

    /** A run ended: a notification, unless it was told already. */
    told(ended: TaskEnded): void {
        const key = this.key();
        const since = readPreference(key);
        if (since && Date.parse(ended.finished) <= Date.parse(since)) return;
        writePreference(key, ended.finished);
        const message = [taskEndedMessage(ended), ended.summary].filter(Boolean).join(' ');
        this.toasts.show(message, { level: LEVELS[ended.outcome], timeout: 8000, actions: this.actions(ended) });
    }

    private actions(ended: TaskEnded): ToastAction[] {
        if (ended.id <= 0 || !this.config.can(Rights.viewTasks)) return [];
        return [
            {
                label: $localize`:open a task's run:Open`,
                title: $localize`Open this run and its log in the task history`,
                link: `/admin?section=tasks&page=history&run=${ended.id}`,
            },
        ];
    }

    private key(): string {
        const config = this.config.config();
        return `${KEY}.${config?.tenant?.id ?? 0}.${config?.user?.id ?? 0}`;
    }
}

/** "The index scan of “Photos” is done.": one sentence per task and outcome. */
export function taskEndedMessage({ task, path, outcome }: TaskEnded): string {
    const kind = task === 'scan' && !path ? 'library' : task;
    const values = { kind, path: path ?? '' };
    switch (outcome) {
        case 'completed':
            return icu(
                $localize`:a background task ended:{kind, select, library {The index scan of the whole library is \
done.} scan {The index scan of “{path}” is done.} trickplay {Trickplay is up to date.} similarVideos {The search for \
similar videos is done.} trashPurge {The trash purge is done.} connectionIndex {The index of “{path}” is up to date.} \
other {A background task is done.}}`,
                values,
            );
        case 'cancelled':
            return icu(
                $localize`:a background task ended:{kind, select, library {The index scan of the whole library was \
cancelled.} scan {The index scan of “{path}” was cancelled.} trickplay {Making trickplay was cancelled.} \
similarVideos {The search for similar videos was cancelled.} trashPurge {The trash purge was cancelled.} \
connectionIndex {Bringing the index of “{path}” up to date was cancelled.} other {A background task was cancelled.}}`,
                values,
            );
        default:
            return icu(
                $localize`:a background task ended:{kind, select, library {The index scan of the whole library \
failed.} scan {The index scan of “{path}” failed.} trickplay {Making trickplay failed.} similarVideos {The search \
for similar videos failed.} trashPurge {The trash purge failed.} connectionIndex {The index of “{path}” could not \
be brought up to date.} other {A background task failed.}}`,
                values,
            );
    }
}
