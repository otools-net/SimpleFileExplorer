import { inject, Injectable } from '@angular/core';
import { Api } from './api.service';
import { ConfigService } from './config.service';
import { PlaceTransferResults } from './connection.models';
import { DeleteResults, FileConflict, FileOperationResult } from './models';
import type { AccessGain } from '../files/file.models';

/** How a move or copy on a connection treats names taken in the target. */
export type PlaceConflict = 'fail' | 'keepBoth' | 'replace';

/**
 * A move on a connection that would let more of the library's people see or change a folder (409 `access-gains`):
 * nothing was moved; asked again with `keepAccess` once the person chose.
 */
export class AccessQuestion extends Error {
    constructor(readonly gains: AccessGain[]) {
        super('More people would see or change the folder there.');
    }
}

/**
 * Changes to the files of the place listed (agents/docs/features/connections.md): the library's routes
 * (`api/files/…`), or those of the connection the listing shows (`api/connections/{id}/files/…`), which answer in the
 * same shapes. Paths are relative to the library's root or to the connection's.
 */
@Injectable({ providedIn: 'root' })
export class PlaceFiles {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);

    /** Creates the folder `name` in `path`; resolves with the conflict (409) as `Api.createFolder`. */
    createFolder(path: string, name: string): Promise<FileOperationResult> {
        const place = this.config.place();
        return place
            ? this.api.connectionFiles<FileOperationResult>(place.id, 'folders', { path, name })
            : this.api.createFolder(path, name);
    }

    /** Renames `path` to `name`; resolves with the conflict (409) as `Api.renameItem`. */
    rename(path: string, name: string): Promise<FileOperationResult> {
        const place = this.config.place();
        return place
            ? this.api.connectionFiles<FileOperationResult>(place.id, 'rename', { path, name })
            : this.api.renameItem(path, name);
    }

    /** Deletes `paths`; a connection's go to its provider's trash, if it has one (never for good from here). */
    async delete(paths: string[], permanent: boolean): Promise<DeleteResults> {
        const place = this.config.place();
        if (!place) return this.api.deleteItems(paths, permanent);
        return answered(await this.api.connectionFiles<DeleteResults>(place.id, 'delete', { paths, permanent }));
    }

    /**
     * Moves or copies `paths` into `target`, both on `connection` (the one listed when not given; its provider does
     * it, at once). `keepAccess`: the answer to an `AccessQuestion` (thrown when a move needs one).
     */
    async transfer(
        kind: 'move' | 'copy',
        paths: string[],
        target: string,
        conflict: PlaceConflict,
        { connection = this.config.place()?.id, keepAccess }: { connection?: number; keepAccess?: boolean } = {},
    ): Promise<PlaceTransferResults> {
        if (connection === undefined) throw new Error('Only the files of a connection are moved this way.');
        const body = { kind, paths, target, conflict, ...(keepAccess === undefined ? {} : { keepAccess }) };
        return answered(await this.api.connectionFiles<PlaceTransferResults>(connection, 'transfer', body));
    }
}

/**
 * A 409 resolves with its problem (`connection-needs-reconnecting`): an error here, as no item was done; the question
 * of a move letting more people in (`access-gains`) as an `AccessQuestion`.
 */
function answered<T extends { items: unknown[] }>(answer: T | FileConflict): T {
    if ('items' in answer && Array.isArray(answer.items)) return answer;
    const conflict = answer as FileConflict;
    if (conflict.type === 'access-gains') throw new AccessQuestion(conflict.gains ?? []);
    throw new Error(conflict.detail ?? 'The connection could not do it.');
}
