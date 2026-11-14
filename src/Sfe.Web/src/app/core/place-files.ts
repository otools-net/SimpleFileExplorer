import { inject, Injectable } from '@angular/core';
import { Api } from './api.service';
import { ConfigService } from './config.service';
import { PlaceTransferResults } from './connection.models';
import { DeleteResults, FileConflict, FileOperationResult } from './models';

/** How a move or copy on a connection treats names taken in the target. */
export type PlaceConflict = 'fail' | 'keepBoth' | 'replace';

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

    /** Moves or copies `paths` into `target`, both on `connection` (the one listed; its provider does it, at once). */
    async transfer(
        kind: 'move' | 'copy',
        paths: string[],
        target: string,
        conflict: PlaceConflict,
        connection = this.config.place()?.id,
    ): Promise<PlaceTransferResults> {
        if (connection === undefined) throw new Error('Only the files of a connection are moved this way.');
        const body = { kind, paths, target, conflict };
        return answered(await this.api.connectionFiles<PlaceTransferResults>(connection, 'transfer', body));
    }
}

/** A 409 resolves with its problem (`connection-needs-reconnecting`): an error here, as no item was done. */
function answered<T extends { items: unknown[] }>(answer: T | FileConflict): T {
    if ('items' in answer && Array.isArray(answer.items)) return answer;
    throw new Error((answer as FileConflict).detail ?? 'The connection could not do it.');
}
