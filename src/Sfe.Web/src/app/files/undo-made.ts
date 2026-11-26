import { inject, Injectable } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { baseName } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { ListingStore } from '../library/listing.store';
import { deletedToast, notDeleted, notDeletedOne } from './file-dialog-texts';
import { changedSince, notTrashed } from './undo-texts';

/**
 * Dates on the disk may be a little behind or ahead of the time the action ended (FAT keeps two seconds, a copy's last
 * write lands just before the end): a change within this many ms of it isn't one.
 */
const SLACK = 2000;

/**
 * Undo of what an action made in the library (a new folder or file, copies): each item goes to the trash unless it
 * changed since (its date on the disk later than the action's end): someone's work in it is never taken away.
 */
@Injectable({ providedIn: 'root' })
export class MadeUndo {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);

    /**
     * Whether what was made in the library can be undone: the session may move items to the trash, and the space
     * keeps them there (one that deletes for good can't give them back).
     */
    possible(): boolean {
        return this.config.can(Rights.deleteFiles) && !this.config.config()?.files?.deletePermanently;
    }

    /** Moves `paths` (of the library) to the trash, those unchanged since `since` (the server's date of the end). */
    async trash(paths: readonly string[], since: string): Promise<void> {
        const unchanged: string[] = [];
        for (const path of paths) {
            const refused = await this.changeOf(path, Date.parse(since) + SLACK);
            if (refused) this.toasts.error(refused, 8000);
            else unchanged.push(path);
        }
        if (!unchanged.length) return;
        let results;
        try {
            results = (await this.api.deleteItems(unchanged, false)).items;
        } catch (error) {
            this.toasts.error(notDeleted(unchanged.length, (error as Error).message), 8000);
            return;
        }
        const done = results.filter((result) => result.status === 'done').map((result) => result.path);
        this.store.applyRemoved(done);
        if (done.length) this.toasts.show(deletedToast(done.length, 'trash'));
        for (const failed of results.filter((result) => result.status !== 'done'))
            this.toasts.error(failed.message ?? notDeletedOne(baseName(failed.path)), 8000);
    }

    /** Why `path` stays: it changed after `limit` (ms), or it can't be read; null when it can go. */
    private async changeOf(path: string, limit: number): Promise<string | null> {
        try {
            const modified = (await this.api.properties(path)).item.modified;
            return modified && Date.parse(modified) > limit ? changedSince(baseName(path)) : null;
        } catch (error) {
            const reason =
                error instanceof ApiError && error.status === 404
                    ? $localize`it isn't there any more.`
                    : (error as Error).message;
            return notTrashed(baseName(path), reason);
        }
    }
}
