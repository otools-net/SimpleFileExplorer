import { inject, Injectable } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { PlaceTransferItem } from '../core/connection.models';
import { plural } from '../core/format';
import { baseName } from '../core/paths';
import { PlaceConflict, PlaceFiles } from '../core/place-files';
import { ToastService } from '../core/toast.service';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';
import { FileJobKind } from './file.models';

/** Said when items would go between a connection and the library (or another connection). */
export const BETWEEN_PLACES = 'Copying between a connection and the library comes with a later version.';

/**
 * Moves and copies on the connection listed (agents/docs/features/connections.md): its provider does them at once,
 * so there is no job, no folder rules and no tags to ask about. Names taken in the target are asked about (keep both,
 * replace) and asked again. Moves and copies between a connection and the library are refused for now; `FileTransfers`
 * asks here first.
 */
@Injectable({ providedIn: 'root' })
export class PlaceTransfers {
    private readonly config = inject(ConfigService);
    private readonly files = inject(PlaceFiles);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);

    /** Where the items cut or copied were: a connection's id, null for the library. */
    private keptIn: number | null = null;

    /** The connection listed, null for the library. */
    private here(): number | null {
        return this.config.place()?.id ?? null;
    }

    /** Ctrl+X, Ctrl+C: the items kept are of the place listed. */
    kept(): void {
        this.keptIn = this.here();
    }

    /** A paste of items kept in another place: refused (said); false when it may go on. */
    refusesPaste(): boolean {
        if (this.keptIn === this.here()) return false;
        this.toasts.error(BETWEEN_PLACES);
        return true;
    }

    /** Move to…, Copy to… pick a folder of the library: refused (said) on a connection. */
    refusesPick(): boolean {
        if (this.here() === null) return false;
        this.toasts.error(BETWEEN_PLACES);
        return true;
    }

    /** A paste or drop on the connection listed is done here: false in the library (a job does it there). */
    handles(kind: FileJobKind, paths: string[], target: string): boolean {
        if (this.here() === null) return false;
        if (kind === 'move' || kind === 'copy') void this.run(kind, paths, target, 'fail');
        return true;
    }

    private async run(kind: 'move' | 'copy', paths: string[], target: string, conflict: PlaceConflict): Promise<void> {
        let items: PlaceTransferItem[];
        try {
            items = (await this.files.transfer(kind, paths, target, conflict)).items;
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
        if (taken.length) this.askTaken(kind, taken, target, where);
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
    private askTaken(kind: 'move' | 'copy', paths: string[], target: string, where: string): void {
        const what = paths.length === 1 ? `“${baseName(paths[0])}”` : plural(paths.length, 'name');
        this.toasts.show(`${what} already in “${where}”: not ${kind === 'move' ? 'moved' : 'copied'}.`, {
            timeout: 15000,
            actions: [
                {
                    label: 'Keep both',
                    title: 'Give them a new name there',
                    run: () => void this.run(kind, paths, target, 'keepBoth'),
                },
                {
                    label: 'Replace',
                    title: 'Put them in place of those there',
                    run: () => void this.run(kind, paths, target, 'replace'),
                },
            ],
        });
    }
}
