import { computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from './config.service';
import { ToastAction, ToastService } from './toast.service';

/** How many actions a tab keeps to undo; older ones are forgotten. */
export const UNDO_LIMIT = 20;

/** Something done in this tab that can be undone (agents/docs/features/undo.md). */
export interface UndoEntry {
    /** What was done, the sentence of its toast ("Renamed to “b.jpg”."): under Undo in the ⋮ menu. */
    done: string;
    /** What undoing does, a sentence: the hint of Undo ("Give it back its name “a.jpg”."). */
    hint: string;
    /** Undoes it; says itself, in toasts, what could not be undone and why. */
    run: () => Promise<void> | void;
    /**
     * Done on the files of the place listed then (`PlaceFiles`): undone only while that place is listed again (a
     * path means another item elsewhere). Absent: undone from anywhere (its places are known).
     */
    place?: UndoPlace;
}

/** The library (`id` null) or a connection. */
export interface UndoPlace {
    id: number | null;
    name: string;
}

/**
 * The actions of this tab that can be undone, the latest last, at most `UNDO_LIMIT`: the toast's Undo, Ctrl+Z and
 * the ⋮ menu's Undo take them from here, so each is undone once. Kept in memory: a reload, another tab or another
 * device starts with none, and nothing done by others is ever in it.
 */
@Injectable({ providedIn: 'root' })
export class UndoHistory {
    private readonly toasts = inject(ToastService);
    private readonly config = inject(ConfigService);
    private readonly entries = signal<readonly UndoEntry[]>([]);
    /** Undos run one after the other, in the order asked. */
    private queue: Promise<void> = Promise.resolve();

    /** What Ctrl+Z would undo; null when nothing. */
    readonly latest = computed(() => this.entries().at(-1) ?? null);
    /** Why the latest can't be undone here (it was done in another place); null when it can. */
    readonly blocked = computed(() => {
        const place = this.latest()?.place;
        return place && place.id !== (this.config.place()?.id ?? null) ? elsewhere(place) : null;
    });

    /** The place listed now, for an entry done on its files. */
    here(): UndoPlace {
        const place = this.config.place();
        return place ? { id: place.id, name: place.name } : { id: null, name: '' };
    }

    /** Keeps `entry`, forgetting the oldest past the limit; returns the Undo button of its toast. */
    add(entry: UndoEntry): ToastAction {
        this.entries.update((list) => [...list, entry].slice(-UNDO_LIMIT));
        return { label: $localize`Undo`, title: entry.hint, run: () => void this.undo(entry) };
    }

    /**
     * Ctrl+Z, the ⋮ menu: undoes the latest, saying what in a toast, or why it can't here (it stays to undo). False
     * when there is nothing to undo.
     */
    undoLatest(): boolean {
        const entry = this.latest();
        if (!entry) return false;
        const blocked = this.blocked();
        if (blocked) this.toasts.error(blocked);
        else {
            this.toasts.show($localize`Undoing: ${entry.done}:what:`);
            void this.undo(entry);
        }
        return true;
    }

    /** Undoes `entry`, once (it leaves the list at once), after the undos asked before it. */
    undo(entry: UndoEntry): Promise<void> {
        if (!this.entries().includes(entry)) return this.queue;
        if (entry.place && entry.place.id !== (this.config.place()?.id ?? null)) {
            this.toasts.error(elsewhere(entry.place));
            return this.queue;
        }
        this.entries.update((list) => list.filter((kept) => kept !== entry));
        this.queue = this.queue.then(async () => {
            try {
                await entry.run();
            } catch (error) {
                this.toasts.error($localize`Could not undo it: ${(error as Error).message}:reason:`);
            }
        });
        return this.queue;
    }
}

/** Why an action done in `place` can't be undone while another place is listed. */
function elsewhere(place: UndoPlace): string {
    return place.id === null
        ? $localize`It was done in the library: open the library to undo it.`
        : $localize`It was done in “${place.name}:connection:”: open it to undo it.`;
}
