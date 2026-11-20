import { Injectable, signal } from '@angular/core';

/** A file or folder being written, as the change feed's `writing` event tells it (`ItemWriting` on the server). */
export interface ItemWriting {
    /** Its name while written (`name.sfe-partial`). */
    name: string;
    /** The name it takes when done. */
    finalName: string;
    bytesDone: number;
    /** 0 when unknown. */
    bytesTotal: number;
}

/** The items being written in a folder now (`FolderWriting`): empty once the last one ended. */
export interface FolderWriting {
    path: string;
    items: ItemWriting[];
}

/** What changed in a folder since its last `writing` event: names (`name.sfe-partial`) that came or went. */
export interface WritingChanges {
    started: string[];
    ended: string[];
}

/**
 * How far each item being written is (copies, transfers into the library, compressions, extractions:
 * agents/docs/features/partial-files.md), by its path, from the change feed's `writing` events (a few a second per
 * folder at most). Listed items read it to show a progress bar.
 */
@Injectable({ providedIn: 'root' })
export class WritingProgress {
    private readonly items = signal<ReadonlyMap<string, ItemWriting>>(new Map());

    /** The item at `path` (root-relative, `….sfe-partial`) while it is being written. */
    of(path: string): ItemWriting | undefined {
        return this.items().get(path);
    }

    /** Takes the folder's items being written now, in place of those it had; tells which came and which went. */
    update(change: FolderWriting): WritingChanges {
        const prefix = change.path ? `${change.path}/` : '';
        const before = new Map(this.items());
        const ended = [...before.keys()]
            .filter((path) => inFolder(path, prefix))
            .filter((path) => !change.items.some((item) => prefix + item.name === path));
        const started = change.items.filter((item) => !before.has(prefix + item.name)).map((item) => item.name);
        ended.forEach((path) => before.delete(path));
        change.items.forEach((item) => before.set(prefix + item.name, item));
        this.items.set(before);
        return { started, ended: ended.map((path) => path.slice(prefix.length)) };
    }
}

/** `path` is directly in the folder whose paths start with `prefix` (empty for the root). */
function inFolder(path: string, prefix: string): boolean {
    return path.startsWith(prefix) && !path.slice(prefix.length).includes('/');
}

/** How far `item` is, in whole percents; null when its size is unknown. */
export function percentOf(item: ItemWriting): number | null {
    return item.bytesTotal > 0 ? Math.min(100, Math.floor((item.bytesDone * 100) / item.bytesTotal)) : null;
}
