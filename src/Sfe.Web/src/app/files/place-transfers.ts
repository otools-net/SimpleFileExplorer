import { inject, Injectable } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { PlaceTransferItem } from '../core/connection.models';
import { plural } from '../core/format';
import { baseName, parentPath } from '../core/paths';
import { PlaceConflict, PlaceFiles } from '../core/place-files';
import { ToastService } from '../core/toast.service';
import { UsableConnections } from '../core/usable-connections';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';
import { FileJobKind, FileJobView, TransferRequest } from './file.models';

/** The places of a move or copy: a connection's id, null for the library. */
export interface TransferPlaces {
    from: number | null;
    to: number | null;
}

/** The places of a job: those of a transfer between two, else the library's. */
export function jobPlaces(job: FileJobView): TransferPlaces {
    return { from: job.fromConnection ?? null, to: job.toConnection ?? null };
}

/**
 * Moves and copies on a connection (agents/docs/features/connections.md): its provider does them at once, so there is
 * no job, no folder rules and no tags to ask about. Names taken in the target are asked about (keep both, replace) and
 * asked again. Between two places (a connection and the library, or two connections) a transfer job does them
 * (`between`), followed by `FileTransfers` as a library job; `FileTransfers` asks here first.
 */
@Injectable({ providedIn: 'root' })
export class PlaceTransfers {
    private readonly config = inject(ConfigService);
    private readonly files = inject(PlaceFiles);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly connections = inject(UsableConnections);

    /** The connection listed, null for the library. */
    here(): number | null {
        return this.config.place()?.id ?? null;
    }

    /** The name of a place: the library's, or the connection's. */
    nameOf(connection: number | null): string {
        return connection === null ? this.config.libraryName() : this.connections.name(connection);
    }

    /** "from Dropbox › Trips to Library › Photos" for a job between two places; null for one in the library. */
    journey(job: FileJobView): string | null {
        const { from, to } = jobPlaces(job);
        if (from === null && to === null) return null;
        const source = job.results[0]?.path ?? job.current;
        return `from ${this.folderIn(from, source ? parentPath(source) : '')} to ${this.folderIn(to, job.target)}`;
    }

    /** From the place listed into it. */
    listed(): TransferPlaces {
        return { from: this.here(), to: this.here() };
    }

    /** The transfer job doing a move or copy between two places; null within one (its own routes). */
    between(kind: FileJobKind, paths: string[], target: string, places: TransferPlaces): TransferRequest | null {
        if (places.from === places.to || (kind !== 'move' && kind !== 'copy')) return null;
        return {
            kind,
            from: { connection: places.from, paths },
            to: { connection: places.to, path: target },
            conflicts: 'ask',
        };
    }

    /** A paste or drop within the connection `connection` is done here: false in the library (a job does it there). */
    handles(kind: FileJobKind, paths: string[], target: string, connection = this.here()): boolean {
        if (connection === null) return false;
        if (kind === 'move' || kind === 'copy') void this.run(kind, paths, target, 'fail', connection);
        return true;
    }

    /** "Dropbox › Trips"; the place's name alone for its root. */
    private folderIn(connection: number | null, folder: string): string {
        const place = this.nameOf(connection);
        return folder ? `${place} › ${baseName(folder)}` : place;
    }

    private async run(
        kind: 'move' | 'copy',
        paths: string[],
        target: string,
        conflict: PlaceConflict,
        connection: number,
    ): Promise<void> {
        let items: PlaceTransferItem[];
        try {
            items = (await this.files.transfer(kind, paths, target, conflict, connection)).items;
        } catch (error) {
            this.toasts.error(`Could not ${kind}: ${(error as Error).message}`);
            return;
        }
        const verb = kind === 'move' ? 'moved' : 'copied';
        const where = baseName(target) || this.config.place()?.name || 'the connection';
        const done = items.filter((item) => item.status === 'done');
        const taken = items.filter((item) => item.status === 'exists').map((item) => item.path);
        const failed = items.filter((item) => item.status !== 'done' && item.status !== 'exists');
        if (done.length) {
            this.toasts.show(`${plural(done.length, 'item')} ${verb} to “${where}”.`, { timeout: 8000 });
            void this.store.load({ ...currentPosition(), loaded: this.store.items().length + done.length });
        }
        if (taken.length) this.askTaken({ kind, paths: taken, target, connection }, where);
        if (failed.length)
            this.toasts.error(
                failed.length === 1
                    ? (failed[0].message ?? `“${baseName(failed[0].path)}” could not be ${verb}.`)
                    : `${plural(failed.length, 'item')} could not be ${verb}: ` +
                          (failed[0].message ?? baseName(failed[0].path)),
                8000,
            );
    }

    /** Names taken in the target: kept both (a new name) or replaced, as the person chooses, else left. */
    private askTaken(
        taken: { kind: 'move' | 'copy'; paths: string[]; target: string; connection: number },
        where: string,
    ) {
        const { kind, paths, target, connection } = taken;
        const what = paths.length === 1 ? `“${baseName(paths[0])}”` : plural(paths.length, 'name');
        this.toasts.show(`${what} already in “${where}”: not ${kind === 'move' ? 'moved' : 'copied'}.`, {
            timeout: 15000,
            actions: [
                {
                    label: 'Keep both',
                    title: 'Give them a new name there',
                    run: () => void this.run(kind, paths, target, 'keepBoth', connection),
                },
                {
                    label: 'Replace',
                    title: 'Put them in place of those there',
                    run: () => void this.run(kind, paths, target, 'replace', connection),
                },
            ],
        });
    }
}
